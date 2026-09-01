#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.notifications;
using dnd_game.domain.events;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Обработчик уведомлений: при значимых событиях создаёт уведомления для пользователей.
    /// </summary>
    public class NotificationHandler : IEventHandler<CharacterDied>,
                                       IEventHandler<CombatStarted>,
                                       IEventHandler<CharacterHealed>,
                                       IEventHandler<ConditionApplied>,
                                       IEventHandler<ConditionRemoved>,
                                       IEventHandler<SpellCast>
    {
        private readonly INotificationService _notificationService;

        public NotificationHandler(INotificationService notificationService)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        public async Task Handle(CharacterDied e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var message = new NotificationMessage(
                RecipientUserId: Guid.Empty, // здесь нужен идентификатор владельца персонажа, но его нет в событии
                Title: "Персонаж погиб",
                Body: $"Персонаж {e.CharacterId} погиб.",
                CreatedAt: DateTime.UtcNow);
            await _notificationService.SendAsync(message, ct);
        }

        public async Task Handle(CombatStarted e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var message = new NotificationMessage(
                RecipientUserId: Guid.Empty,
                Title: "Бой начался",
                Body: $"Бой {e.CombatId} начался с {e.Participants.Count} участниками.",
                CreatedAt: DateTime.UtcNow);
            await _notificationService.SendAsync(message, ct);
        }

        public async Task Handle(CharacterHealed e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var message = new NotificationMessage(
                RecipientUserId: Guid.Empty,
                Title: "Исцеление",
                Body: $"Персонаж {e.CharacterId} исцелён на {e.Amount}.",
                CreatedAt: DateTime.UtcNow);
            await _notificationService.SendAsync(message, ct);
        }

        public async Task Handle(ConditionApplied e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var message = new NotificationMessage(
                RecipientUserId: Guid.Empty,
                Title: "Наложено состояние",
                Body: $"Персонаж {e.CharacterId} получил состояние {e.Condition}.",
                CreatedAt: DateTime.UtcNow);
            await _notificationService.SendAsync(message, ct);
        }

        public async Task Handle(ConditionRemoved e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var message = new NotificationMessage(
                RecipientUserId: Guid.Empty,
                Title: "Состояние снято",
                Body: $"Персонаж {e.CharacterId} потерял состояние {e.Condition}.",
                CreatedAt: DateTime.UtcNow);
            await _notificationService.SendAsync(message, ct);
        }

        public async Task Handle(SpellCast e, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var message = new NotificationMessage(
                RecipientUserId: Guid.Empty,
                Title: "Применено заклинание",
                Body: $"Заклинатель {e.CasterId} применил заклинание {e.SpellId}.",
                CreatedAt: DateTime.UtcNow);
            await _notificationService.SendAsync(message, ct);
        }
    }
}