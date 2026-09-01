// tests/loadtest/Program.cs
using dnd_game.application.security;
using dnd_game.domain.aggregates;
using dnd_game.domain.events;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace dnd_game.tests.loadtest;

public static class Program
{
    // ---------- Константы для Event Store теста ----------
    private const int ParticipantsPerCombat = 4;
    private const int SpectatorsPerCombat = 2;
    private const int DefaultMaxConcurrentDbOperations = 50;

    // ---------- Константы для API теста ----------
    private const int DefaultConcurrentUsers = 10;
    private const int DefaultRequestsPerUser = 20;
    private static readonly CancellationToken cancellationToken = CancellationToken.None;

    public static async Task<int> Main(string[] args)
    {
        var mode = ParseArg(args, "--mode") ?? "eventstore";

        if (mode.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            await RunApiLoadTest(args);
            return 0;
        }

        // Режим eventstore (оригинальный тест)
        await RunEventStoreLoadTest(args);
        return 0;
    }

    // ----------------------------------------------------------------------
    // 1. Нагрузочный тест Event Store (оригинальный)
    // ----------------------------------------------------------------------
    private static async Task RunEventStoreLoadTest(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DND_LOADTEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("ОШИБКА: переменная окружения DND_LOADTEST_POSTGRES_CONNECTION не задана.");
            Console.WriteLine("Укажите connection string на тестовую (не боевую!) базу PostgreSQL и запустите снова.");
            return;
        }

        var levels = ParseLevels(args) ?? new[] { 50, 200, 500 };
        var maxConcurrentDbOps = ParseIntArg(args, "--max-concurrent-db-ops") ?? DefaultMaxConcurrentDbOperations;

        Console.WriteLine("=== Нагрузочный тест Event Store: смешанная нагрузка (запись + чтение) ===");
        Console.WriteLine($"Уровни одновременных боёв для проверки: {string.Join(", ", levels)}");
        Console.WriteLine($"Лимит одновременных запросов к Postgres: {maxConcurrentDbOps} (--max-concurrent-db-ops чтобы изменить)");
        Console.WriteLine();

        var results = new List<LevelResult>();
        foreach (var level in levels)
        {
            Console.WriteLine($"--- Уровень: {level} одновременных боёв ({level * SpectatorsPerCombat} зрителей) ---");
            var result = await RunEventStoreLevel(connectionString, level, maxConcurrentDbOps);
            results.Add(result);
            result.Print();
            Console.WriteLine();
        }

        PrintEventStoreSummary(results);
    }

    private static async Task<LevelResult> RunEventStoreLevel(string connectionString, int combatCount, int maxConcurrentDbOps)
    {
        var config = new SnapshotConfiguration { EventCountInterval = 100 };
        var snapshots = new SnapshotStore(connectionString, config);
        var serviceProvider = new NoOpServiceProvider();
        var metricsCollector = Mock.Of<IMetricsCollector>();
        var consistencyManager = new ConsistencyManager(
            serviceProvider,
            new InMemoryLockManager(
            new PermissionChecker(
                Mock.Of<IUserSecurityContextProvider>(),
                Mock.Of<ICharacterOwnershipRepository>()),
            NullLogger<InMemoryLockManager>.Instance),
            NullLogger<ConsistencyManager>.Instance,
            metricsCollector);

        var storeLogger = NullLogger<PostgresEventStore>.Instance;
        var storeMetrics = Mock.Of<IMetricsCollector>();
        var storeEventBus = Mock.Of<IEventBus>();
        var store = new PostgresEventStore(
            connectionString,
            snapshots,
            consistencyManager,
            storeLogger,
            storeMetrics,
            storeEventBus);

        using var dbGate = new SemaphoreSlim(maxConcurrentDbOps, maxConcurrentDbOps);

        var writeLatenciesMs = new ConcurrentBag<double>();
        var readLatenciesMs = new ConcurrentBag<double>();
        var writeErrors = 0;
        var readErrors = 0;
        var combatIds = new ConcurrentBag<Guid>();

        var overallStopwatch = Stopwatch.StartNew();

        var writers = Enumerable.Range(0, combatCount).Select(async _ =>
        {
            try
            {
                await RunOneCombatLifecycle(store, writeLatenciesMs, combatIds, dbGate, cancellationToken);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref writeErrors);
                Console.WriteLine($"[Ошибка записи] {ex.GetType().Name}: {ex.Message}");
            }
        }).ToArray();

        using var readCts = new CancellationTokenSource();
        var readers = Enumerable.Range(0, combatCount * SpectatorsPerCombat).Select(async _ =>
        {
            await Task.Delay(50);
            var random = new Random();
            while (!readCts.IsCancellationRequested)
            {
                if (combatIds.IsEmpty) { await Task.Delay(20); continue; }
                var ids = combatIds.ToArray();
                var targetId = ids[random.Next(ids.Length)];
                await dbGate.WaitAsync();
                var sw = Stopwatch.StartNew();
                try
                {
                    await store.Load<CombatAggregate>(targetId, cancellationToken);
                    sw.Stop();
                    readLatenciesMs.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref readErrors);
                    Console.WriteLine($"[Ошибка чтения] {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    dbGate.Release();
                }
                await Task.Delay(random.Next(200, 800));
            }
        }).ToArray();

        await Task.WhenAll(writers);
        readCts.Cancel();
        try { await Task.WhenAll(readers); } catch (TaskCanceledException) { /* ожидаемо */ }

        overallStopwatch.Stop();

        return new LevelResult(
            CombatCount: combatCount,
            TotalDuration: overallStopwatch.Elapsed,
            WriteLatenciesMs: writeLatenciesMs.ToArray(),
            ReadLatenciesMs: readLatenciesMs.ToArray(),
            WriteErrors: writeErrors,
            ReadErrors: readErrors);
    }

    private static async Task RunOneCombatLifecycle(PostgresEventStore store, ConcurrentBag<double> writeLatenciesMs, ConcurrentBag<Guid> combatIds, SemaphoreSlim dbGate, CancellationToken cancellationToken)
    {
        var combatId = Guid.NewGuid();
        var participantIds = Enumerable.Range(0, ParticipantsPerCombat).Select(_ => Guid.NewGuid()).ToList();
        var random = new Random();

        async Task Measure(string step, Func<Task> action)
        {
            await dbGate.WaitAsync();
            try
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"[Шаг: {step}] {ex.GetType().Name}: {ex.Message}", ex);
                }
                sw.Stop();
                writeLatenciesMs.Add(sw.Elapsed.TotalMilliseconds);
            }
            finally
            {
                dbGate.Release();
            }
        }

        await Measure("CreateCombat", async () =>
        {
            var participantsWithSpeed = participantIds.Select(id => (id, 30)).ToList();
            var combat = new CombatAggregate(combatId, participantsWithSpeed);
            await store.SaveWithMetadata(combat, new EventMetadata(), cancellationToken);
        });
        combatIds.Add(combatId);

        foreach (var participantId in participantIds)
        {
            await Measure("RollInitiative", async () =>
            {
                var combat = await store.Load<CombatAggregate>(combatId, cancellationToken) ?? throw new InvalidOperationException("Combat not found");
                combat.RollInitiative(participantId, random.Next(1, 21), random.Next(-1, 5));
                await store.SaveWithMetadata(combat, new EventMetadata(), cancellationToken);
            });
        }

        await Measure("StartRound", async () =>
        {
            var combat = await store.Load<CombatAggregate>(combatId, cancellationToken) ?? throw new InvalidOperationException("Combat not found");
            combat.StartRound();
            await store.SaveWithMetadata(combat, new EventMetadata(), cancellationToken);
        });

        foreach (var participantId in participantIds)
        {
            await Measure("UseMovement", async () =>
            {
                var combat = await store.Load<CombatAggregate>(combatId, cancellationToken) ?? throw new InvalidOperationException("Combat not found");
                combat.UseMovement(participantId, 15);
                await store.SaveWithMetadata(combat, new EventMetadata(), cancellationToken);
            });
            await Measure("UseAction", async () =>
            {
                var combat = await store.Load<CombatAggregate>(combatId, cancellationToken) ?? throw new InvalidOperationException("Combat not found");
                combat.UseAction(participantId);
                await store.SaveWithMetadata(combat, new EventMetadata(), cancellationToken);
            });
        }

        await Measure("EndCombat", async () =>
        {
            var combat = await store.Load<CombatAggregate>(combatId, cancellationToken) ?? throw new InvalidOperationException("Combat not found");
            combat.EndCombat();
            await store.SaveWithMetadata(combat, new EventMetadata(), cancellationToken);
        });
    }

    private static int[]? ParseLevels(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--levels")
            {
                return args[i + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToArray();
            }
        }
        return null; // если не нашли
    }

    private static int? ParseIntArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name && int.TryParse(args[i + 1], out var value))
                return value;
        }
        return null; // если не нашли
    }

    private static void PrintEventStoreSummary(List<LevelResult> results)
    {
        Console.WriteLine("=== Сводная таблица ===");
        string header = string.Join(" | ", new[]
        {
            "Боёв".PadLeft(6),
            "Запись p50".PadLeft(11),
            "Запись p95".PadLeft(11),
            "Запись p99".PadLeft(11),
            "Чтение p50".PadLeft(11),
            "Чтение p95".PadLeft(11),
            "Чтение p99".PadLeft(11),
            "Ошибки".PadLeft(8),
        });
        Console.WriteLine(header);
        foreach (var r in results)
        {
            string row = string.Join(" | ", new[]
            {
                r.CombatCount.ToString().PadLeft(6),
                (Percentile(r.WriteLatenciesMs, 50).ToString("F1") + "мс").PadLeft(11),
                (Percentile(r.WriteLatenciesMs, 95).ToString("F1") + "мс").PadLeft(11),
                (Percentile(r.WriteLatenciesMs, 99).ToString("F1") + "мс").PadLeft(11),
                (Percentile(r.ReadLatenciesMs, 50).ToString("F1") + "мс").PadLeft(11),
                (Percentile(r.ReadLatenciesMs, 95).ToString("F1") + "мс").PadLeft(11),
                (Percentile(r.ReadLatenciesMs, 99).ToString("F1") + "мс").PadLeft(11),
                (r.WriteErrors + r.ReadErrors).ToString().PadLeft(8),
            });
            Console.WriteLine(row);
        }
    }

    internal static double Percentile(double[] values, int percentile)
    {
        if (values.Length == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        index = Math.Clamp(index, 0, sorted.Length - 1);
        return sorted[index];
    }

    private class NoOpServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // ----------------------------------------------------------------------
    // 2. Нагрузочный тест API + WebSocket
    // ----------------------------------------------------------------------
    private static async Task RunApiLoadTest(string[] args)
    {
        var apiUrl = ParseArg(args, "--api-url") ?? "http://localhost:5000";
        var concurrentUsers = ParseIntArg(args, "--concurrent-users") ?? DefaultConcurrentUsers;
        var requestsPerUser = ParseIntArg(args, "--requests-per-user") ?? DefaultRequestsPerUser;

        Console.WriteLine("=== Нагрузочный тест API + WebSocket ===");
        Console.WriteLine($"Сервер: {apiUrl}");
        Console.WriteLine($"Параллельных пользователей: {concurrentUsers}");
        Console.WriteLine($"Запросов на пользователя: {requestsPerUser}");
        Console.WriteLine();

        using var httpClient = new HttpClient { BaseAddress = new Uri(apiUrl) };

        // 1. Аутентификация (получаем токен)
        var token = await AuthenticateAsync(httpClient);
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Ошибка: не удалось получить JWT токен.");
            return;
        }
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var latencies = new ConcurrentBag<double>();
        var errors = 0;
        var totalRequests = 0;

        var stopwatch = Stopwatch.StartNew();

        // Запускаем параллельных пользователей
        var tasks = Enumerable.Range(0, concurrentUsers).Select(async _ =>
        {
            var userToken = await AuthenticateAsync(httpClient) ?? token; // можно переиспользовать один токен
            var client = new HttpClient { BaseAddress = new Uri(apiUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            for (int i = 0; i < requestsPerUser; i++)
            {
                // REST: создание персонажа
                await MeasureAsync(async () =>
                {
                    var characterId = Guid.NewGuid();
                    var response = await client.PostAsync("/api/characters",
                        new StringContent(
                            JsonSerializer.Serialize(new { characterId, name = $"LoadTest_{Guid.NewGuid():N}", maxHitPoints = 20 }),
                            Encoding.UTF8, "application/json"));
                    if (!response.IsSuccessStatusCode) Interlocked.Increment(ref errors);
                }, latencies);

                // REST: создание боя
                await MeasureAsync(async () =>
                {
                    var combatId = Guid.NewGuid();
                    var participants = new[] { Guid.NewGuid(), Guid.NewGuid() };
                    var response = await client.PostAsync("/api/combat",
                        new StringContent(
                            JsonSerializer.Serialize(new { combatId, participants }),
                            Encoding.UTF8, "application/json"));
                    if (!response.IsSuccessStatusCode) Interlocked.Increment(ref errors);
                }, latencies);

                // REST: создание квеста (если есть кампания)
                await MeasureAsync(async () =>
                {
                    var campaignId = Guid.NewGuid();
                    // сначала создадим кампанию (упрощённо)
                    var response = await client.PostAsync("/api/campaign",
                        new StringContent(
                            JsonSerializer.Serialize(new { campaignId, name = "Test Campaign", gameMasterId = Guid.NewGuid() }),
                            Encoding.UTF8, "application/json"));
                    if (!response.IsSuccessStatusCode) { Interlocked.Increment(ref errors); return; }
                    // затем квест
                    var questId = Guid.NewGuid();
                    response = await client.PostAsync($"/api/campaign/{campaignId}/quests",
                        new StringContent(
                            JsonSerializer.Serialize(new { questId, title = "Load Test Quest", objectives = new object[0], rewards = new object[0], participantIds = new Guid[0] }),
                            Encoding.UTF8, "application/json"));
                    if (!response.IsSuccessStatusCode) Interlocked.Increment(ref errors);
                }, latencies);

                // WebSocket: открыть соединение и отправить команду
                await MeasureAsync(async () =>
                {
                    using var ws = new ClientWebSocket();
                    var uri = new Uri(apiUrl.Replace("http", "ws") + $"/ws?token={userToken}");
                    await ws.ConnectAsync(uri, CancellationToken.None);
                    if (ws.State == WebSocketState.Open)
                    {
                        var pingMsg = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
                        await ws.SendAsync(new ArraySegment<byte>(pingMsg), WebSocketMessageType.Text, true, CancellationToken.None);
                        var buffer = new byte[1024];
                        await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
                    }
                }, latencies);

                Interlocked.Increment(ref totalRequests);
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var allLatencies = latencies.ToArray();
        Console.WriteLine("=== Результаты API + WebSocket ===");
        Console.WriteLine($"Длительность: {stopwatch.Elapsed.TotalSeconds:F1}с");
        Console.WriteLine($"Запросов: {totalRequests}");
        Console.WriteLine($"Ошибок: {errors}");
        Console.WriteLine($"Запросов/сек: {totalRequests / stopwatch.Elapsed.TotalSeconds:F1}");
        Console.WriteLine($"p50 задержки: {Percentile(allLatencies, 50):F1}мс");
        Console.WriteLine($"p95 задержки: {Percentile(allLatencies, 95):F1}мс");
        Console.WriteLine($"p99 задержки: {Percentile(allLatencies, 99):F1}мс");
    }

    private static async Task MeasureAsync(Func<Task> action, ConcurrentBag<double> latencies)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await action();
        }
        catch
        {
            // ошибки считаются отдельно
        }
        sw.Stop();
        latencies.Add(sw.Elapsed.TotalMilliseconds);
    }

    private static async Task<string?> AuthenticateAsync(HttpClient client)
    {
        var loginRequest = new { username = "testuser", password = "123456" };
        var response = await client.PostAsync("/api/auth/login",
            new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return data?.GetValueOrDefault("token");
    }

    private static string? ParseArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
                return args[i + 1];
        }
        return null;
    }

    // ----------------------------------------------------------------------
    // Вспомогательные классы
    // ----------------------------------------------------------------------
    public record LevelResult(int CombatCount, TimeSpan TotalDuration, double[] WriteLatenciesMs, double[] ReadLatenciesMs, int WriteErrors, int ReadErrors)
    {
        public void Print()
        {
            var totalWrites = WriteLatenciesMs.Length;
            var totalReads = ReadLatenciesMs.Length;
            var writesPerSec = TotalDuration.TotalSeconds > 0 ? totalWrites / TotalDuration.TotalSeconds : 0;
            Console.WriteLine($"Длительность: {TotalDuration.TotalSeconds:F1}с | Записей: {totalWrites} ({writesPerSec:F1}/с) | Чтений: {totalReads} | Ошибок записи: {WriteErrors} | Ошибок чтения: {ReadErrors}");
            Console.WriteLine($"Запись  p50={Program.Percentile(WriteLatenciesMs, 50):F1}мс  p95={Program.Percentile(WriteLatenciesMs, 95):F1}мс  p99={Program.Percentile(WriteLatenciesMs, 99):F1}мс");
            Console.WriteLine($"Чтение  p50={Program.Percentile(ReadLatenciesMs, 50):F1}мс  p95={Program.Percentile(ReadLatenciesMs, 95):F1}мс  p99={Program.Percentile(ReadLatenciesMs, 99):F1}мс");
        }
    }
}