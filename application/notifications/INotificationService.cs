#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.notifications
{
    /// <summary>Сообщение уведомления игроку.</summary>
    public sealed record NotificationMessage(
        Guid RecipientUserId,
        string Title,
        string Body,
        DateTime CreatedAt);

    /// <summary>
    /// Сервис доставки уведомлений пользователям.
    /// Базовая реализация складывает сообщения в очередь; в production
    /// можно подключить SignalR, email, push.
    /// </summary>
    public interface INotificationService
    {
        Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
        IReadOnlyList<NotificationMessage> GetPending(Guid userId);
        void MarkAsDelivered(Guid userId, int count);
    }

    /// <summary>Простая in-memory реализация <see cref="INotificationService"/>.</summary>
    public sealed class InMemoryNotificationService : INotificationService
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentQueue<NotificationMessage>> _queues = new();

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queue = _queues.GetOrAdd(message.RecipientUserId, _ => new ConcurrentQueue<NotificationMessage>());
            queue.Enqueue(message);
            return Task.CompletedTask;
        }

        public IReadOnlyList<NotificationMessage> GetPending(Guid userId)
        {
            if (_queues.TryGetValue(userId, out var queue))
                return queue.ToArray();
            return Array.Empty<NotificationMessage>();
        }

        public void MarkAsDelivered(Guid userId, int count)
        {
            if (_queues.TryGetValue(userId, out var queue))
            {
                for (int i = 0; i < count && queue.TryDequeue(out _); i++) { }
            }
        }
    }
}