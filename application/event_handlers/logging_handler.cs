using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.events;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Обработчик логирования игровых событий.
    /// Пишет информационные и предупреждающие сообщения в журнал на русском языке.
    /// </summary>
    public class LoggingHandler(ILogger<LoggingHandler> logger) : IEventHandler<CharacterCreated>,
                                  IEventHandler<CharacterDamageTaken>,
                                  IEventHandler<CharacterHealed>,
                                  IEventHandler<CharacterDied>,
                                  IEventHandler<CombatStarted>,
                                  IEventHandler<CombatEnded>,
                                  IEventHandler<SpellCast>,
                                  IEventHandler<ConditionApplied>,
                                  IEventHandler<ConditionRemoved>
    {
        private readonly ILogger<LoggingHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Task Handle(CharacterCreated e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Создан персонаж: {Name} ({Id})", e.Name, e.CharacterId);
            return Task.CompletedTask;
        }

        public Task Handle(CharacterDamageTaken e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} получает {Amount} урона", e.CharacterId, e.Amount);
            return Task.CompletedTask;
        }

        public Task Handle(CharacterHealed e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} исцелён на {Amount}", e.CharacterId, e.Amount);
            return Task.CompletedTask;
        }

        public Task Handle(CharacterDied e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogWarning("Персонаж {Id} погиб!", e.CharacterId);
            return Task.CompletedTask;
        }

        public Task Handle(CombatStarted e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Бой {CombatId} начался с {Count} участниками", e.CombatId, e.Participants.Count);
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
            _logger.LogInformation("Заклинатель {CasterId} применил заклинание {SpellId} (цель: {TargetId})",
                e.CasterId, e.SpellId, e.TargetId);
            return Task.CompletedTask;
        }

        public Task Handle(ConditionApplied e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} получил состояние: {Condition}", e.CharacterId, e.Condition);
            return Task.CompletedTask;
        }

        public Task Handle(ConditionRemoved e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("Персонаж {Id} потерял состояние: {Condition}", e.CharacterId, e.Condition);
            return Task.CompletedTask;
        }
    }
}