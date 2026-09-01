using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.events;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Сборщик метрик игровых событий. Логирует ключевые показатели.
    /// В будущем может быть расширен отправкой в Prometheus/StatsD.
    /// </summary>
    public class MetricHandler(ILogger<MetricHandler> logger) : IEventHandler<CharacterCreated>,
                                 IEventHandler<CharacterDamageTaken>,
                                 IEventHandler<CharacterHealed>,
                                 IEventHandler<CharacterDied>,
                                 IEventHandler<CombatStarted>,
                                 IEventHandler<CombatEnded>,
                                 IEventHandler<SpellCast>,
                                 IEventHandler<ExperienceGained>,
                                 IEventHandler<RestStarted>,
                                 IEventHandler<RestCompleted>,
                                 IEventHandler<ConditionApplied>,
                                 IEventHandler<ConditionRemoved>
    {
        private readonly ILogger<MetricHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Task Handle(CharacterCreated e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Создан персонаж: {Name} ({Id})", e.Name, e.CharacterId);
            return Task.CompletedTask;
        }

        public Task Handle(CharacterDamageTaken e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Нанесён урон персонажу {Id}: {Amount}", e.CharacterId, e.Amount);
            return Task.CompletedTask;
        }

        public Task Handle(CharacterHealed e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Исцеление персонажа {Id}: {Amount}", e.CharacterId, e.Amount);
            return Task.CompletedTask;
        }

        public Task Handle(CharacterDied e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogWarning("Персонаж {Id} погиб", e.CharacterId);
            return Task.CompletedTask;
        }

        public Task Handle(CombatStarted e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Бой {CombatId} начался, участников: {Count}", e.CombatId, e.Participants.Count);
            return Task.CompletedTask;
        }

        public Task Handle(CombatEnded e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Бой {CombatId} завершён", e.CombatId);
            return Task.CompletedTask;
        }

        public Task Handle(SpellCast e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Заклинание {SpellId} применено заклинателем {CasterId}", e.SpellId, e.CasterId);
            return Task.CompletedTask;
        }

        public Task Handle(ExperienceGained e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} получил {Amount} опыта", e.CharacterId, e.Amount);
            return Task.CompletedTask;
        }

        public Task Handle(RestStarted e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} начал {RestType} отдых", e.CharacterId, e.RestType);
            return Task.CompletedTask;
        }

        public Task Handle(RestCompleted e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} завершил {RestType} отдых (восстановлено HP: {Hp})", e.CharacterId, e.RestType, e.HitPointsRestored);
            return Task.CompletedTask;
        }

        public Task Handle(ConditionApplied e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} получил состояние {Condition}", e.CharacterId, e.Condition);
            return Task.CompletedTask;
        }

        public Task Handle(ConditionRemoved e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} потерял состояние {Condition}", e.CharacterId, e.Condition);
            return Task.CompletedTask;
        }
    }
}