#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using dnd_game.domain.aggregates;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.monitoring;

namespace dnd_game.infrastructure.event_store
{
    /// <summary>
    /// Результат проверки согласованности.
    /// </summary>
    public enum ConsistencyResult
    {
        Success,
        VersionConflict,
        InvariantViolation,
        GlobalRuleViolation,
        LockTimeout
    }

    /// <summary>
    /// Менеджер согласованности, гарантирующий соблюдение правил DnD при сохранении агрегатов.
    /// Отвечает за:
    /// - оптимистическую блокировку по версии агрегата,
    /// - проверку инвариантов конкретного агрегата,
    /// - глобальные инварианты (например, уникальность концентрации у персонажа),
    /// - принудительную блокировку ресурса на время сохранения.
    /// </summary>
    public interface IConsistencyManager
    {
        /// <summary>
        /// Проверить согласованность агрегата перед сохранением и при необходимости
        /// применить пессимистическую блокировку.
        /// Возвращает результат проверки.
        /// </summary>
        /// <param name="aggregate">Агрегат с несохранёнными событиями.</param>
        /// <param name="expectedVersion">Версия, ожидаемая клиентом (для оптимистической блокировки).</param>
        /// <param name="ownerId">Идентификатор пользователя/сессии, выполняющего сохранение.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<ConsistencyResult> EnforceConsistencyAsync(
            AggregateRoot aggregate,
            int expectedVersion,
            string ownerId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверить глобальные инварианты, не привязанные к одному агрегату
        /// (например, запрет двух заклинаний с концентрацией на одном персонаже).
        /// Должен вызываться перед сохранением после проверки версий.
        /// </summary>
        Task<bool> CheckGlobalInvariantsAsync(AggregateRoot aggregate, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Реализация <see cref="IConsistencyManager"/> с использованием EventStore и блокировок.
    /// </summary>
    public class ConsistencyManager : IConsistencyManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IDistributedLockManager _lockManager;
        private readonly ILogger<ConsistencyManager> _logger;
        private readonly IMetricsCollector _metrics;
        private readonly Lazy<IEventStore> _eventStore;

        public ConsistencyManager(
            IServiceProvider serviceProvider,
            IDistributedLockManager lockManager,
            ILogger<ConsistencyManager> logger,
            IMetricsCollector metrics)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

            _eventStore = new Lazy<IEventStore>(() =>
                _serviceProvider.GetRequiredService<IEventStore>());
        }

        /// <inheritdoc />
        public async Task<ConsistencyResult> EnforceConsistencyAsync(
            AggregateRoot aggregate,
            int expectedVersion,
            string ownerId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(aggregate);
            if (string.IsNullOrWhiteSpace(ownerId))
                throw new ArgumentException("Идентификатор владельца не может быть пустым.", nameof(ownerId));
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Пессимистическая блокировка для предотвращения одновременных изменений
            string lockKey = LockKeyFactory.ForAggregate(aggregate.Id);
            await using var lockHandle = await _lockManager.AcquireAsync(
                lockKey,
                LockMode.Exclusive,
                ownerId,
                TimeSpan.FromSeconds(5),
                cancellationToken);

            if (lockHandle == null)
            {
                _logger.LogWarning("Таймаут блокировки согласованности для агрегата {AggregateId}", aggregate.Id);
                _metrics.IncrementCounter("dnd.consistency.lock_timeout");
                return ConsistencyResult.LockTimeout;
            }

            // 2. Оптимистическая блокировка по версии
            if (aggregate.OriginalVersion != expectedVersion)
            {
                _logger.LogWarning(
                    "Конфликт версий для агрегата {AggregateId}: ожидалась {ExpectedVersion}, фактическая {ActualVersion}",
                    aggregate.Id, expectedVersion, aggregate.OriginalVersion);
                _metrics.IncrementCounter("dnd.consistency.version_conflict");
                return ConsistencyResult.VersionConflict;
            }

            // 3. Проверка инвариантов самого агрегата
            try
            {
                aggregate.EnsureInvariants();
            }
            catch (RuleViolation ex)
            {
                _logger.LogWarning(
                    "Нарушение инвариантов в агрегате {AggregateId}: {Message}",
                    aggregate.Id, ex.Message);
                _metrics.IncrementCounter("dnd.consistency.invariant_violation");
                return ConsistencyResult.InvariantViolation;
            }
            catch (Exception ex)
            {
                // На случай других ошибок валидации
                _logger.LogError(ex,
                    "Неожиданная ошибка при проверке инвариантов агрегата {AggregateId}",
                    aggregate.Id);
                _metrics.IncrementCounter("dnd.consistency.invariant_error");
                return ConsistencyResult.InvariantViolation;
            }

            // 4. Проверка глобальных инвариантов
            if (!await CheckGlobalInvariantsAsync(aggregate, cancellationToken))
            {
                _metrics.IncrementCounter("dnd.consistency.global_rule_violation");
                return ConsistencyResult.GlobalRuleViolation;
            }

            return ConsistencyResult.Success;
        }

        /// <inheritdoc />
        public async Task<bool> CheckGlobalInvariantsAsync(
            AggregateRoot aggregate,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(aggregate);
            cancellationToken.ThrowIfCancellationRequested();

            // Примеры глобальных правил, основанных на правилах D&D:

            // Если агрегат — персонаж, проверяем, что у него нет двух активных концентраций
            if (aggregate is CharacterAggregate character)
            {
                // Проверка концентрации: в рамках одного агрегата это уже контролируется
                // методами самого агрегата, но дополнительно проверяем последнее сохранённое состояние
                if (character.Concentrating)
                {
                    var existing = await _eventStore.Value.Load<CharacterAggregate>(
                        character.Id,
                        cancellationToken);

                    if (existing != null &&
                        existing.Concentrating &&
                        existing.ConcentratingOnSpellId != character.ConcentratingOnSpellId)
                    {
                        _logger.LogWarning(
                            "Персонаж {CharacterId} пытается сконцентрироваться на {NewSpell}, уже концентрируясь на {ExistingSpell}",
                            character.Id,
                            character.ConcentratingOnSpellId,
                            existing.ConcentratingOnSpellId);
                        return false;
                    }
                }

                // Проверка максимального уровня
                if (character.Level > 20)
                {
                    _logger.LogWarning(
                        "Персонаж {CharacterId} имеет уровень {Level}, превышающий максимум 20",
                        character.Id, character.Level);
                    return false;
                }
            }

            // Здесь могут быть другие глобальные правила, например:
            // - уникальность артефактов,
            // - запрет на одновременное участие в двух боях,
            // - соблюдение лимитов аттунемента и т.д.

            return true;
        }
    }

    /// <summary>
    /// Дополнительные методы для LockKeyFactory.
    /// </summary>
    public static partial class LockKeyFactory
    {
        /// <summary>
        /// Создаёт ключ блокировки для агрегата.
        /// </summary>
        public static string ForAggregate(Guid aggregateId) => $"Aggregate:{aggregateId}";
    }
}