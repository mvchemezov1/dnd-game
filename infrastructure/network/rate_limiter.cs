#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace dnd_game.infrastructure.network
{
    // =====================================================================
    // Конфигурация и политики ограничения частоты запросов (Rate Limiting)
    // =====================================================================

    /// <summary>
    /// Алгоритм ограничения частоты.
    /// </summary>
    public enum RateLimitAlgorithm
    {
        /// <summary>Алгоритм «ведро с токенами».</summary>
        TokenBucket,

        /// <summary>Алгоритм «скользящее окно».</summary>
        SlidingWindow
    }

    /// <summary>
    /// Политика ограничения частоты для отдельного правила.
    /// </summary>
    public class RateLimitPolicy
    {
        /// <summary>Название политики (например, «global», «auth», «commands»).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Максимальное количество запросов за окно.</summary>
        public int MaxRequests { get; set; } = 30;

        /// <summary>Временное окно для подсчёта запросов.</summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Алгоритм ограничения.</summary>
        public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.TokenBucket;
    }

    /// <summary>
    /// Конфигурация ограничения частоты запросов.
    /// </summary>
    public class RateLimitConfiguration
    {
        /// <summary>Включено ли ограничение частоты.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Словарь политик по имени.</summary>
        public Dictionary<string, RateLimitPolicy> Policies { get; set; } = [];

        /// <summary>Количество токенов, добавляемых при пополнении (для TokenBucket).</summary>
        public int TokenBucketRefillAmount { get; set; } = 1;
    }

    // =====================================================================
    // Интерфейс ограничителя частоты
    // =====================================================================

    /// <summary>
    /// Интерфейс сервиса ограничения частоты запросов.
    /// </summary>
    public interface IRateLimiter
    {
        /// <summary>
        /// Проверяет, разрешён ли запрос для указанного клиента в рамках политики.
        /// </summary>
        /// <param name="clientId">Идентификатор клиента (пользователь, подключение).</param>
        /// <param name="policyName">Название политики. Если не указано, используется «global».</param>
        /// <returns><c>true</c>, если запрос разрешён; иначе <c>false</c>.</returns>
        bool IsAllowed(Guid clientId, string? policyName = null);

        /// <summary>
        /// Асинхронно проверяет и потребляет квоту для запроса.
        /// </summary>
        Task<bool> TryConsumeAsync(Guid clientId, string policyName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает оставшееся количество допустимых запросов.
        /// </summary>
        int GetRemainingAllowance(Guid clientId, string policyName);
    }

    /// <summary>
    /// Реализация сервиса ограничения частоты запросов.
    /// Поддерживает алгоритмы TokenBucket и SlidingWindow, периодически очищает устаревшие записи.
    /// </summary>
    public class RateLimiter : IRateLimiter, IDisposable
    {
        private readonly RateLimitConfiguration _config;
        private readonly ILogger<RateLimiter> _logger;
        private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(10);
        private readonly TimeSpan _expirationThreshold = TimeSpan.FromMinutes(30);
        private readonly Timer? _cleanupTimer;
        private bool _disposed;

        public RateLimiter(IOptions<RateLimitConfiguration> config, ILogger<RateLimiter> logger)
        {
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Если политики не заданы, заполняем стандартными
            EnsureDefaultPolicies();

            _cleanupTimer = new Timer(
                _ => CleanupExpiredEntries(),
                null,
                _cleanupInterval,
                _cleanupInterval);
        }

        /// <summary>
        /// Добавляет стандартные политики, если пользователь не задал свои.
        /// </summary>
        private void EnsureDefaultPolicies()
        {
            if (_config.Policies != null && _config.Policies.Count > 0)
                return;

            _config.Policies = new Dictionary<string, RateLimitPolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["login"] = new RateLimitPolicy
                {
                    Name = "login",
                    MaxRequests = 5,
                    Window = TimeSpan.FromMinutes(1),
                    Algorithm = RateLimitAlgorithm.SlidingWindow
                },
                ["attack"] = new RateLimitPolicy
                {
                    Name = "attack",
                    MaxRequests = 5,
                    Window = TimeSpan.FromSeconds(10),
                    Algorithm = RateLimitAlgorithm.TokenBucket
                },
                ["spell"] = new RateLimitPolicy
                {
                    Name = "spell",
                    MaxRequests = 3,
                    Window = TimeSpan.FromSeconds(15),
                    Algorithm = RateLimitAlgorithm.TokenBucket
                },
                ["movement"] = new RateLimitPolicy
                {
                    Name = "movement",
                    MaxRequests = 10,
                    Window = TimeSpan.FromSeconds(10),
                    Algorithm = RateLimitAlgorithm.TokenBucket
                },
                ["rest"] = new RateLimitPolicy
                {
                    Name = "rest",
                    MaxRequests = 2,
                    Window = TimeSpan.FromMinutes(1),
                    Algorithm = RateLimitAlgorithm.TokenBucket
                },
                ["global"] = new RateLimitPolicy
                {
                    Name = "global",
                    MaxRequests = 30,
                    Window = TimeSpan.FromSeconds(10),
                    Algorithm = RateLimitAlgorithm.TokenBucket
                },
                ["websocket-connect"] = new RateLimitPolicy
                {
                    Name = "websocket-connect",
                    MaxRequests = 10,
                    Window = TimeSpan.FromMinutes(1),
                    Algorithm = RateLimitAlgorithm.SlidingWindow
                },
                ["websocket-message"] = new RateLimitPolicy
                {
                    Name = "websocket-message",
                    MaxRequests = 60,
                    Window = TimeSpan.FromSeconds(10),
                    Algorithm = RateLimitAlgorithm.TokenBucket
                }
            };
        }

        /// <inheritdoc />
        public bool IsAllowed(Guid clientId, string? policyName = null)
        {
            ValidateClientId(clientId);
            if (!_config.Enabled)
                return true;

            string effectivePolicy = string.IsNullOrWhiteSpace(policyName) ? "global" : policyName;
            var policy = GetPolicy(effectivePolicy);
            if (policy is null)
            {
                _logger.LogWarning("Политика ограничения '{PolicyName}' не найдена. Запрос разрешён по умолчанию.", effectivePolicy);
                return true;
            }

            string key = BuildKey(clientId, effectivePolicy);

            return policy.Algorithm switch
            {
                RateLimitAlgorithm.TokenBucket => _buckets.GetOrAdd(
                    key,
                    _ => new TokenBucket(policy.MaxRequests, policy.MaxRequests, policy.Window, _config.TokenBucketRefillAmount)).Consume(),
                RateLimitAlgorithm.SlidingWindow => _windows.GetOrAdd(
                    key,
                    _ => new SlidingWindow(policy.MaxRequests, policy.Window)).Consume(),
                _ => false
            };
        }

        /// <inheritdoc />
        public Task<bool> TryConsumeAsync(Guid clientId, string policyName, CancellationToken cancellationToken = default)
        {
            ValidateClientId(clientId);
            if (string.IsNullOrWhiteSpace(policyName))
                throw new ArgumentException("Название политики не может быть пустым.", nameof(policyName));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(IsAllowed(clientId, policyName));
        }

        /// <inheritdoc />
        public int GetRemainingAllowance(Guid clientId, string policyName)
        {
            ValidateClientId(clientId);
            if (string.IsNullOrWhiteSpace(policyName))
                throw new ArgumentException("Название политики не может быть пустым.", nameof(policyName));

            string key = BuildKey(clientId, policyName);
            var policy = GetPolicy(policyName);
            if (policy is null)
                return int.MaxValue; // Политика не определена — ограничений нет.

            return policy.Algorithm switch
            {
                RateLimitAlgorithm.TokenBucket => _buckets.TryGetValue(key, out var bucket)
                    ? bucket.CurrentTokens
                    : policy.MaxRequests,
                RateLimitAlgorithm.SlidingWindow => _windows.TryGetValue(key, out var window)
                    ? window.Remaining
                    : policy.MaxRequests,
                _ => 0
            };
        }

        // ---------- Вспомогательные методы ----------

        private RateLimitPolicy? GetPolicy(string name)
        {
            _config.Policies.TryGetValue(name, out var policy);
            return policy;
        }

        private static string BuildKey(Guid clientId, string policyName)
            => $"{clientId}:{policyName}";

        private static void ValidateClientId(Guid clientId)
        {
            if (clientId == Guid.Empty)
                throw new ArgumentException("Идентификатор клиента не может быть пустым.", nameof(clientId));
        }

        /// <summary>
        /// Удаляет записи, которые не использовались дольше порога устаревания.
        /// </summary>
        private void CleanupExpiredEntries()
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;

            foreach (var key in _buckets.Keys.ToList())
            {
                if (_buckets.TryGetValue(key, out var bucket) && now - bucket.LastUsedUtc > _expirationThreshold)
                {
                    _buckets.TryRemove(key, out _);
                }
            }

            foreach (var key in _windows.Keys.ToList())
            {
                if (_windows.TryGetValue(key, out var window) && now - window.LastUsedUtc > _expirationThreshold)
                {
                    _windows.TryRemove(key, out _);
                }
            }

            _logger.LogDebug("Очистка устаревших записей ограничителя частоты завершена.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _cleanupTimer?.Dispose();
            _buckets.Clear();
            _windows.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        // =====================================================================
        // Внутренние структуры данных алгоритмов
        // =====================================================================

        /// <summary>
        /// Реализация алгоритма «ведро с токенами».
        /// </summary>
        private sealed class TokenBucket(int maxTokens, int initialTokens, TimeSpan refillInterval, int refillAmount)
        {
            private readonly object _lock = new();
            private readonly int _maxTokens = maxTokens;
            private readonly TimeSpan _refillInterval = refillInterval;
            private readonly int _refillAmount = refillAmount;
            private int _currentTokens = initialTokens;
            private DateTime _lastRefillUtc = DateTime.UtcNow;
            private DateTime _lastUsedUtc = DateTime.UtcNow;

            public int CurrentTokens
            {
                get { lock (_lock) return _currentTokens; }
            }

            public DateTime LastUsedUtc => _lastUsedUtc;

            public bool Consume()
            {
                lock (_lock)
                {
                    _lastUsedUtc = DateTime.UtcNow;
                    Refill();
                    if (_currentTokens > 0)
                    {
                        _currentTokens--;
                        return true;
                    }
                    return false;
                }
            }

            private void Refill()
            {
                var now = DateTime.UtcNow;
                if (now < _lastRefillUtc + _refillInterval)
                    return;

                int intervals = (int)((now - _lastRefillUtc).Ticks / _refillInterval.Ticks);
                if (intervals <= 0)
                    return;

                _currentTokens = Math.Min(_maxTokens, _currentTokens + intervals * _refillAmount);
                _lastRefillUtc = _lastRefillUtc.Add(TimeSpan.FromTicks(intervals * _refillInterval.Ticks));
            }
        }

        /// <summary>
        /// Реализация алгоритма «скользящее окно».
        /// </summary>
        private sealed class SlidingWindow(int maxRequests, TimeSpan window)
        {
            private readonly object _lock = new();
            private readonly int _maxRequests = maxRequests;
            private readonly TimeSpan _window = window;
            private readonly Queue<DateTime> _timestamps = new();
            private DateTime _lastUsedUtc = DateTime.UtcNow;

            public int Remaining
            {
                get
                {
                    lock (_lock)
                    {
                        RemoveExpired(DateTime.UtcNow);
                        return Math.Max(0, _maxRequests - _timestamps.Count);
                    }
                }
            }

            public DateTime LastUsedUtc => _lastUsedUtc;

            public bool Consume()
            {
                lock (_lock)
                {
                    _lastUsedUtc = DateTime.UtcNow;
                    var now = DateTime.UtcNow;
                    RemoveExpired(now);

                    if (_timestamps.Count < _maxRequests)
                    {
                        _timestamps.Enqueue(now);
                        return true;
                    }
                    return false;
                }
            }

            private void RemoveExpired(DateTime now)
            {
                while (_timestamps.Count > 0 && now - _timestamps.Peek() > _window)
                    _timestamps.Dequeue();
            }
        }
    }
}