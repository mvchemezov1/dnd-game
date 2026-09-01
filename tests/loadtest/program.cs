#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using dnd_game.domain.aggregates;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.monitoring;
using dnd_game.domain.events;

namespace dnd_game.tests.loadtest
{
    /// <summary>
    /// Нагрузочный тест для Event Store и API + WebSocket.
    /// Запуск: dotnet run --project tests/loadtest -- [--mode eventstore|api] [параметры]
    /// </summary>
    public static class Program
    {
        // ---------- Константы ----------
        private const int ParticipantsPerCombat = 4;
        private const int SpectatorsPerCombat = 2;
        private const int DefaultMaxConcurrentDbOperations = 50;
        private const int DefaultConcurrentUsers = 10;
        private const int DefaultRequestsPerUser = 20;

        public static async Task<int> Main(string[] args)
        {
            var mode = ParseArg(args, "--mode") ?? "eventstore";

            return mode.Equals("api", StringComparison.OrdinalIgnoreCase)
                ? await RunApiLoadTestAsync(args)
                : await RunEventStoreLoadTestAsync(args);
        }

        // =====================================================================
        // 1. Нагрузочный тест Event Store
        // =====================================================================

        private static async Task<int> RunEventStoreLoadTestAsync(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("DND_LOADTEST_POSTGRES_CONNECTION")
                ?? ParseArg(args, "--connection-string");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine("ОШИБКА: укажите строку подключения PostgreSQL через переменную окружения DND_LOADTEST_POSTGRES_CONNECTION или аргумент --connection-string.");
                return 1;
            }

            var levels = ParseLevels(args) ?? new[] { 50, 200, 500 };
            var maxConcurrentDbOps = ParseIntArg(args, "--max-concurrent-db-ops") ?? DefaultMaxConcurrentDbOperations;

            Console.WriteLine("=== Нагрузочный тест Event Store: запись + чтение ===");
            Console.WriteLine($"Уровни: {string.Join(", ", levels)} одновременных боёв");
            Console.WriteLine($"Лимит соединений с БД: {maxConcurrentDbOps}");
            Console.WriteLine();

            var results = new List<LevelResult>();
            foreach (var level in levels)
            {
                Console.WriteLine($"--- Уровень {level} боёв ---");
                var result = await RunEventStoreLevelAsync(connectionString, level, maxConcurrentDbOps);
                results.Add(result);
                result.Print();
                Console.WriteLine();
            }

            PrintEventStoreSummary(results);
            return 0;
        }

        private static async Task<LevelResult> RunEventStoreLevelAsync(
            string connectionString,
            int combatCount,
            int maxConcurrentDbOps)
        {
            // Создание зависимостей для PostgresEventStore
            var snapshotConfig = new SnapshotConfiguration { EventCountInterval = 100 };
            var snapshotStore = new SnapshotStore(connectionString, snapshotConfig);

            var metrics = Mock.Of<IMetricsCollector>();
            var lockManager = new InMemoryLockManager(
                Mock.Of<dnd_game.application.security.PermissionChecker>(),
                NullLogger<InMemoryLockManager>.Instance);

            var eventBus = Mock.Of<IEventBus>();
            var eventStore = new PostgresEventStore(
                connectionString,
                snapshotStore,
                new ConsistencyManager(
                    new StaticServiceProvider(eventStore: null!), // placeholder, заменяется ниже
                    lockManager,
                    NullLogger<ConsistencyManager>.Instance,
                    metrics),
                NullLogger<PostgresEventStore>.Instance,
                metrics,
                eventBus);

            // Обход циклической зависимости: ConsistencyManager использует IEventStore через Lazy
            // Мы можем пересоздать ConsistencyManager с корректным провайдером.
            // Для простоты создадим ConsistencyManager, который не использует EventStore (для нагрузочного теста это не критично).
            var simpleConsistency = new ConsistencyManager(
                new StaticServiceProvider(eventStore),
                lockManager,
                NullLogger<ConsistencyManager>.Instance,
                metrics);

            // Пересоздаём EventStore с этим consistency
            eventStore = new PostgresEventStore(
                connectionString,
                snapshotStore,
                simpleConsistency,
                NullLogger<PostgresEventStore>.Instance,
                metrics,
                eventBus);

            using var dbGate = new SemaphoreSlim(maxConcurrentDbOps, maxConcurrentDbOps);

            var writeLatencies = new ConcurrentBag<double>();
            var readLatencies = new ConcurrentBag<double>();
            var writeErrors = 0;
            var readErrors = 0;
            var combatIds = new ConcurrentBag<Guid>();

            var overallStopwatch = Stopwatch.StartNew();

            // Писатели — жизненный цикл боя
            var writers = Enumerable.Range(0, combatCount).Select(async _ =>
            {
                try
                {
                    await RunOneCombatLifecycleAsync(eventStore, writeLatencies, combatIds, dbGate);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref writeErrors);
                    Console.WriteLine($"[Ошибка записи] {ex.GetType().Name}: {ex.Message}");
                }
            }).ToArray();

            // Читатели — случайная загрузка боёв
            using var readCts = new CancellationTokenSource();
            var readers = Enumerable.Range(0, combatCount * SpectatorsPerCombat).Select(async _ =>
            {
                await Task.Delay(50);
                while (!readCts.IsCancellationRequested)
                {
                    if (combatIds.IsEmpty)
                    {
                        await Task.Delay(20);
                        continue;
                    }
                    var id = combatIds.ToArray()[Random.Shared.Next(combatIds.Count)];
                    await dbGate.WaitAsync();
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await eventStore.Load<CombatAggregate>(id);
                        sw.Stop();
                        readLatencies.Add(sw.Elapsed.TotalMilliseconds);
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
                    await Task.Delay(Random.Shared.Next(200, 800));
                }
            }).ToArray();

            await Task.WhenAll(writers);
            readCts.Cancel();
            try { await Task.WhenAll(readers); } catch (TaskCanceledException) { }

            overallStopwatch.Stop();

            return new LevelResult(
                combatCount,
                overallStopwatch.Elapsed,
                writeLatencies.ToArray(),
                readLatencies.ToArray(),
                writeErrors,
                readErrors);
        }

        private static async Task RunOneCombatLifecycleAsync(
            PostgresEventStore store,
            ConcurrentBag<double> writeLatencies,
            ConcurrentBag<Guid> combatIds,
            SemaphoreSlim dbGate)
        {
            var combatId = Guid.NewGuid();
            var participants = Enumerable.Range(0, ParticipantsPerCombat).Select(_ => Guid.NewGuid()).ToList();

            async Task Measure(Func<Task> action)
            {
                await dbGate.WaitAsync();
                try
                {
                    var sw = Stopwatch.StartNew();
                    await action();
                    sw.Stop();
                    writeLatencies.Add(sw.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    dbGate.Release();
                }
            }

            // Создание боя
            await Measure(async () =>
            {
                var combat = new CombatAggregate(combatId, participants.Select(id => (id, 30)));
                await store.SaveWithMetadata(combat, new EventMetadata());
            });
            combatIds.Add(combatId);

            // Инициатива
            foreach (var pid in participants)
            {
                await Measure(async () =>
                {
                    var combat = await store.Load<CombatAggregate>(combatId) ?? throw new InvalidOperationException("Combat not found");
                    combat.RollInitiative(pid, Random.Shared.Next(1, 21), Random.Shared.Next(-1, 5));
                    await store.SaveWithMetadata(combat, new EventMetadata());
                });
            }

            // Начало раунда
            await Measure(async () =>
            {
                var combat = await store.Load<CombatAggregate>(combatId) ?? throw new InvalidOperationException();
                combat.StartRound();
                await store.SaveWithMetadata(combat, new EventMetadata());
            });

            // Действия
            foreach (var pid in participants)
            {
                await Measure(async () =>
                {
                    var combat = await store.Load<CombatAggregate>(combatId) ?? throw new InvalidOperationException();
                    combat.UseMovement(pid, 15);
                    await store.SaveWithMetadata(combat, new EventMetadata());
                });
                await Measure(async () =>
                {
                    var combat = await store.Load<CombatAggregate>(combatId) ?? throw new InvalidOperationException();
                    combat.UseAction(pid);
                    await store.SaveWithMetadata(combat, new EventMetadata());
                });
            }

            // Завершение боя
            await Measure(async () =>
            {
                var combat = await store.Load<CombatAggregate>(combatId) ?? throw new InvalidOperationException();
                combat.EndCombat();
                await store.SaveWithMetadata(combat, new EventMetadata());
            });
        }

        private static int[]? ParseLevels(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--levels")
                {
                    return args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(int.Parse).ToArray();
                }
            }
            return null;
        }

        private static int? ParseIntArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name && int.TryParse(args[i + 1], out var value))
                    return value;
            }
            return null;
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

        private static void PrintEventStoreSummary(List<LevelResult> results)
        {
            Console.WriteLine("=== Сводная таблица ===");
            string header = string.Join(" | ",
                "Боёв".PadLeft(6),
                "Запись p50".PadLeft(11),
                "Запись p95".PadLeft(11),
                "Запись p99".PadLeft(11),
                "Чтение p50".PadLeft(11),
                "Чтение p95".PadLeft(11),
                "Чтение p99".PadLeft(11),
                "Ошибки".PadLeft(8));
            Console.WriteLine(header);
            foreach (var r in results)
            {
                string row = string.Join(" | ",
                    r.CombatCount.ToString().PadLeft(6),
                    $"{Percentile(r.WriteLatenciesMs, 50):F1}мс".PadLeft(11),
                    $"{Percentile(r.WriteLatenciesMs, 95):F1}мс".PadLeft(11),
                    $"{Percentile(r.WriteLatenciesMs, 99):F1}мс".PadLeft(11),
                    $"{Percentile(r.ReadLatenciesMs, 50):F1}мс".PadLeft(11),
                    $"{Percentile(r.ReadLatenciesMs, 95):F1}мс".PadLeft(11),
                    $"{Percentile(r.ReadLatenciesMs, 99):F1}мс".PadLeft(11),
                    (r.WriteErrors + r.ReadErrors).ToString().PadLeft(8));
                Console.WriteLine(row);
            }
        }

        internal static double Percentile(double[] values, int percentile)
        {
            if (values.Length == 0) return 0;
            var sorted = values.OrderBy(v => v).ToArray();
            var index = Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1, 0, sorted.Length - 1);
            return sorted[index];
        }

        // =====================================================================
        // 2. Нагрузочный тест API + WebSocket
        // =====================================================================

        private static async Task<int> RunApiLoadTestAsync(string[] args)
        {
            var apiUrl = ParseArg(args, "--api-url") ?? "http://localhost:5000";
            var concurrentUsers = ParseIntArg(args, "--concurrent-users") ?? DefaultConcurrentUsers;
            var requestsPerUser = ParseIntArg(args, "--requests-per-user") ?? DefaultRequestsPerUser;

            Console.WriteLine("=== Нагрузочный тест API + WebSocket ===");
            Console.WriteLine($"Сервер: {apiUrl}");
            Console.WriteLine($"Пользователей: {concurrentUsers}, запросов/пользователь: {requestsPerUser}");
            Console.WriteLine();

            using var authClient = new HttpClient { BaseAddress = new Uri(apiUrl) };
            var token = await AuthenticateAsync(authClient);
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("Ошибка: не удалось получить JWT токен.");
                return 1;
            }

            var latencies = new ConcurrentBag<double>();
            var errors = 0;
            var totalRequests = 0;
            var stopwatch = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, concurrentUsers).Select(async _ =>
            {
                using var client = new HttpClient { BaseAddress = new Uri(apiUrl) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                for (int i = 0; i < requestsPerUser; i++)
                {
                    // Создание персонажа
                    await MeasureApiAsync(async () =>
                    {
                        var characterId = Guid.NewGuid();
                        var response = await client.PostAsync("/api/characters",
                            new StringContent(
                                JsonSerializer.Serialize(new { name = $"LoadTest_{Guid.NewGuid():N}", maxHitPoints = 20 }),
                                Encoding.UTF8, "application/json"));
                        if (!response.IsSuccessStatusCode) Interlocked.Increment(ref errors);
                    }, latencies);

                    // Создание боя
                    await MeasureApiAsync(async () =>
                    {
                        var combatId = Guid.NewGuid();
                        var participants = new[] { Guid.NewGuid(), Guid.NewGuid() };
                        var response = await client.PostAsync("/api/combat",
                            new StringContent(
                                JsonSerializer.Serialize(new { combatId, participants }),
                                Encoding.UTF8, "application/json"));
                        if (!response.IsSuccessStatusCode) Interlocked.Increment(ref errors);
                    }, latencies);

                    // WebSocket ping
                    await MeasureApiAsync(async () =>
                    {
                        using var ws = new ClientWebSocket();
                        var uri = new Uri(apiUrl.Replace("http", "ws") + $"/ws?token={token}");
                        await ws.ConnectAsync(uri, CancellationToken.None);
                        if (ws.State == WebSocketState.Open)
                        {
                            var ping = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
                            await ws.SendAsync(new ArraySegment<byte>(ping), WebSocketMessageType.Binary, true, CancellationToken.None);
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
            Console.WriteLine($"p50: {Percentile(allLatencies, 50):F1}мс");
            Console.WriteLine($"p95: {Percentile(allLatencies, 95):F1}мс");
            Console.WriteLine($"p99: {Percentile(allLatencies, 99):F1}мс");
            return 0;
        }

        private static async Task MeasureApiAsync(Func<Task> action, ConcurrentBag<double> latencies)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await action();
            }
            catch
            {
                // ошибки считаются в action
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

        // =====================================================================
        // Вспомогательные классы
        // =====================================================================

        /// <summary>
        /// Простой статический провайдер сервисов для передачи зависимостей.
        /// </summary>
        private sealed class StaticServiceProvider : IServiceProvider
        {
            private readonly IEventStore? _eventStore;

            public StaticServiceProvider(IEventStore? eventStore = null)
            {
                _eventStore = eventStore;
            }

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IEventStore) && _eventStore != null)
                    return _eventStore;
                return null;
            }
        }

        public sealed record LevelResult(
            int CombatCount,
            TimeSpan TotalDuration,
            double[] WriteLatenciesMs,
            double[] ReadLatenciesMs,
            int WriteErrors,
            int ReadErrors)
        {
            public void Print()
            {
                var totalWrites = WriteLatenciesMs.Length;
                var totalReads = ReadLatenciesMs.Length;
                var writesPerSec = TotalDuration.TotalSeconds > 0 ? totalWrites / TotalDuration.TotalSeconds : 0;
                Console.WriteLine($"Длительность: {TotalDuration.TotalSeconds:F1}с | Записей: {totalWrites} ({writesPerSec:F1}/с) | Чтений: {totalReads} | Ошибки записи: {WriteErrors} | Ошибки чтения: {ReadErrors}");
                Console.WriteLine($"Запись  p50={Percentile(WriteLatenciesMs, 50):F1}мс  p95={Percentile(WriteLatenciesMs, 95):F1}мс  p99={Percentile(WriteLatenciesMs, 99):F1}мс");
                Console.WriteLine($"Чтение  p50={Percentile(ReadLatenciesMs, 50):F1}мс  p95={Percentile(ReadLatenciesMs, 95):F1}мс  p99={Percentile(ReadLatenciesMs, 99):F1}мс");
            }
        }
    }
}