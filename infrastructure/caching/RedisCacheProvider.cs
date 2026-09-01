#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.caching
{
    /// <summary>
    /// Реализация кэш-провайдера на базе Redis.
    /// Хранит данные в виде JSON-строк и поддерживает время жизни записей.
    /// </summary>
    public class RedisCacheProvider : ICacheProvider
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger<RedisCacheProvider> _logger;

        /// <summary>
        /// Создаёт экземпляр провайдера, используя указанное подключение к Redis.
        /// </summary>
        /// <param name="redis">Подключение к Redis.</param>
        /// <param name="logger">Логгер для диагностики (необязательный).</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="redis"/> равен <c>null</c>.</exception>
        public RedisCacheProvider(IConnectionMultiplexer redis, ILogger<RedisCacheProvider>? logger = null)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _db = redis.GetDatabase();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            _logger = logger ?? NullLogger<RedisCacheProvider>.Instance;
        }

        /// <inheritdoc />
        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            ValidateKey(key);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                RedisValue value = await _db.StringGetAsync(key).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested(); // проверяем после ожидания

                if (value.IsNullOrEmpty)
                    return null;

                var result = JsonSerializer.Deserialize<T>(value!, _jsonOptions);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении записи из Redis по ключу {Key}", key);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
        {
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            cancellationToken.ThrowIfCancellationRequested();

            if (expiry.HasValue && expiry.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(expiry), "Время жизни записи должно быть положительным.");

            string json = JsonSerializer.Serialize(value, _jsonOptions);

            try
            {
                if (expiry.HasValue)
                    await _db.StringSetAsync(key, json, expiry.Value).ConfigureAwait(false);
                else
                    await _db.StringSetAsync(key, json).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении записи в Redis по ключу {Key}", key);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _db.KeyDeleteAsync(key).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении записи из Redis по ключу {Key}", key);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateKey(key);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                bool exists = await _db.KeyExistsAsync(key).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return exists;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке существования ключа {Key} в Redis", key);
                throw;
            }
        }

        /// <summary>
        /// Проверяет, что ключ не пуст и не состоит только из пробелов.
        /// </summary>
        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Ключ кэша не может быть пустым или содержать только пробелы.", nameof(key));
        }

        // В классе RedisCacheProvider
        public void RemoveSync(string key)
        {
            ValidateKey(key);
            try
            {
                // Синхронное удаление через StackExchange.Redis
                _db.KeyDelete(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка синхронного удаления ключа {Key} из Redis", key);
                throw;
            }
        }
    }
}