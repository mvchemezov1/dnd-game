#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.monitoring
{
    /// <summary>
    /// Статус компонента по результатам проверки здоровья.
    /// </summary>
    public enum HealthStatus
    {
        /// <summary>Компонент работает нормально.</summary>
        Healthy,

        /// <summary>Компонент работает с ограничениями.</summary>
        Degraded,

        /// <summary>Компонент не работает.</summary>
        Unhealthy
    }

    /// <summary>
    /// Результат проверки здоровья отдельного компонента.
    /// </summary>
    public class HealthCheckResult
    {
        /// <summary>Название проверяемого компонента.</summary>
        public string ComponentName { get; set; } = string.Empty;

        /// <summary>Статус компонента.</summary>
        public HealthStatus Status { get; set; }

        /// <summary>Описание состояния или ошибки.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Время, затраченное на проверку.</summary>
        public TimeSpan ResponseTime { get; set; }

        /// <summary>Дополнительные сведения о проверке.</summary>
        public Dictionary<string, object> Details { get; set; } = [];
    }

    /// <summary>
    /// Интерфейс проверки здоровья компонента.
    /// </summary>
    public interface IHealthCheck
    {
        /// <summary>
        /// Выполняет проверку и возвращает результат.
        /// </summary>
        Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Агрегированная проверка здоровья для приложения DnD.
    /// Проверяет основные зависимости: базу данных, шину сообщений, распределённые блокировки.
    /// </summary>
    public class DndHealthCheck(
        IEventStore eventStore,
        RabbitMqBus? rabbitMqBus,
        IDistributedLockManager? distributedLockManager,
        IConfiguration configuration,
        ILogger<DndHealthCheck> logger) : IHealthCheck
    {
        private readonly IEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        private readonly RabbitMqBus? _rabbitMqBus = rabbitMqBus;
        private readonly IDistributedLockManager? _distributedLockManager = distributedLockManager;
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                                ?? throw new ArgumentException("Строка подключения 'DefaultConnection' не задана.");
        private readonly ILogger<DndHealthCheck> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var overallResult = new HealthCheckResult
            {
                ComponentName = "DnD Application",
                Status = HealthStatus.Healthy,
                Description = "Общий статус приложения"
            };

            var checks = new List<Task<HealthCheckResult>>
            {
                CheckEventStoreAsync(cancellationToken),
                CheckDatabaseAsync(cancellationToken),
                CheckMessageBusAsync(cancellationToken),
                CheckLockManagerAsync(cancellationToken)
            };

            var results = await Task.WhenAll(checks).ConfigureAwait(false);

            // Если хотя бы один компонент не работает, приложение считается неработоспособным.
            if (results.Any(r => r.Status == HealthStatus.Unhealthy))
                overallResult.Status = HealthStatus.Unhealthy;
            else if (results.Any(r => r.Status == HealthStatus.Degraded))
                overallResult.Status = HealthStatus.Degraded;

            foreach (var result in results)
                overallResult.Details[result.ComponentName] = result;

            return overallResult;
        }

        /// <summary>
        /// Проверяет доступность EventStore (PostgreSQL).
        /// </summary>
        private async Task<HealthCheckResult> CheckEventStoreAsync(CancellationToken ct)
        {
            var result = new HealthCheckResult { ComponentName = "EventStore" };
            var start = DateTime.UtcNow;

            try
            {
                // Используем IEventStore для проверки наличия хотя бы одного агрегата.
                // В реальной системе можно выполнить более точную проверку, например,
                // через вызов GetCurrentVersionAsync с известным идентификатором.
                // Здесь для простоты проверяем подключение к базе данных, где хранится EventStore.
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using var cmd = new NpgsqlCommand("SELECT 1", conn);
                await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                result.Status = HealthStatus.Healthy;
                result.Description = "EventStore работает корректно.";
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Description = $"Ошибка проверки EventStore: {ex.Message}";
                _logger.LogError(ex, "Проверка EventStore завершилась ошибкой.");
            }

            result.ResponseTime = DateTime.UtcNow - start;
            return result;
        }

        /// <summary>
        /// Проверяет общее подключение к базе данных.
        /// </summary>
        private async Task<HealthCheckResult> CheckDatabaseAsync(CancellationToken ct)
        {
            var result = new HealthCheckResult { ComponentName = "Database" };
            var start = DateTime.UtcNow;

            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using var cmd = new NpgsqlCommand("SELECT 1", conn);
                await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                result.Status = HealthStatus.Healthy;
                result.Description = "Подключение к базе данных установлено.";
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Description = $"Ошибка подключения к базе данных: {ex.Message}";
                _logger.LogError(ex, "Проверка базы данных завершилась ошибкой.");
            }

            result.ResponseTime = DateTime.UtcNow - start;
            return result;
        }

        /// <summary>
        /// Проверяет состояние шины сообщений (RabbitMQ или InMemory).
        /// </summary>
        private Task<HealthCheckResult> CheckMessageBusAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var result = new HealthCheckResult { ComponentName = "MessageBus (RabbitMQ)" };
            var start = DateTime.UtcNow;

            // Если RabbitMQ не настроен, считаем состояние "с ограничениями".
            if (_rabbitMqBus == null)
            {
                result.Status = HealthStatus.Degraded;
                result.Description = "RabbitMQ не настроен; используется шина в памяти.";
                result.ResponseTime = DateTime.UtcNow - start;
                return Task.FromResult(result);
            }

            try
            {
                if (!_rabbitMqBus.IsHealthy())
                {
                    result.Status = HealthStatus.Unhealthy;
                    result.Description = "Соединение с RabbitMQ закрыто.";
                }
                else
                {
                    result.Status = HealthStatus.Healthy;
                    result.Description = "Соединение с RabbitMQ активно.";
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Description = $"Ошибка проверки RabbitMQ: {ex.Message}";
                _logger.LogError(ex, "Проверка шины сообщений завершилась ошибкой.");
            }

            result.ResponseTime = DateTime.UtcNow - start;
            return Task.FromResult(result);
        }

        /// <summary>
        /// Проверяет работоспособность распределённых блокировок (Redis).
        /// </summary>
        private async Task<HealthCheckResult> CheckLockManagerAsync(CancellationToken ct)
        {
            var result = new HealthCheckResult { ComponentName = "DistributedLockManager (Redis)" };
            var start = DateTime.UtcNow;

            if (_distributedLockManager == null)
            {
                result.Status = HealthStatus.Degraded;
                result.Description = "Менеджер распределённых блокировок не настроен.";
                result.ResponseTime = DateTime.UtcNow - start;
                return result;
            }

            try
            {
                // Выполняем тестовую блокировку для проверки доступности Redis.
                string testKey = $"health_check:{Guid.NewGuid():N}";
                await using var lockHandle = await _distributedLockManager.AcquireAsync(
                    testKey,
                    LockMode.Exclusive,
                    "health_check",
                    TimeSpan.FromSeconds(2),
                    ct).ConfigureAwait(false);

                if (lockHandle != null)
                {
                    await lockHandle.DisposeAsync().ConfigureAwait(false);
                    result.Status = HealthStatus.Healthy;
                    result.Description = "Распределённые блокировки работают корректно.";
                }
                else
                {
                    result.Status = HealthStatus.Degraded;
                    result.Description = "Не удалось захватить тестовую блокировку, возможна высокая конкуренция.";
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Description = $"Ошибка проверки менеджера блокировок: {ex.Message}";
                _logger.LogError(ex, "Проверка распределённых блокировок завершилась ошибкой.");
            }

            result.ResponseTime = DateTime.UtcNow - start;
            return result;
        }
    }
}