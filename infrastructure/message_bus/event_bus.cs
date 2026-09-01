#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.event_handlers;
using dnd_game.domain.events;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Универсальная шина событий для игры DnD.
    /// Отвечает за публикацию доменных событий подписчикам и управление подписками.
    /// Реализации: InMemoryBus и RabbitMqBus (см. in_memory_bus.cs / rabbitmq_bus.cs).
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// Публикует одно событие всем подписчикам, зарегистрированным на его тип.
        /// </summary>
        /// <param name="event">Доменное событие (не может быть null).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default);

        /// <summary>
        /// Публикует набор событий (например, все события одного агрегата).
        /// </summary>
        /// <param name="events">Коллекция доменных событий (не может быть null).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);

        /// <summary>
        /// Публикует событие с дополнительным контекстом пользователя и сессии.
        /// </summary>
        /// <param name="event">Доменное событие.</param>
        /// <param name="context">Контекст выполнения (см. CommandContext в command_bus.cs).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task PublishAsync(IDomainEvent @event, CommandContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Подписывает обработчик на события конкретного типа.
        /// </summary>
        /// <typeparam name="TEvent">Тип события, реализующего <see cref="IDomainEvent"/>.</typeparam>
        /// <param name="handler">Обработчик события.</param>
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IDomainEvent;

        /// <summary>
        /// Подписывает делегат-обработчик на события конкретного типа.
        /// </summary>
        /// <typeparam name="TEvent">Тип события.</typeparam>
        /// <param name="handler">Асинхронный обработчик (принимает событие и токен отмены).</param>
        void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent;

        /// <summary>
        /// Отписывает обработчик от событий конкретного типа.
        /// </summary>
        void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IDomainEvent;

        /// <summary>
        /// Отписывает делегат-обработчик от событий конкретного типа.
        /// </summary>
        void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent;
    }

    /// <summary>
    /// Методы расширения для удобной публикации событий с контекстом пользователя/сессии.
    /// </summary>
    public static class EventBusExtensions
    {
        /// <summary>
        /// Публикует событие с указанием пользователя (без игровой сессии).
        /// </summary>
        public static Task PublishAsync(
            this IEventBus eventBus,
            IDomainEvent @event,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventBus);
            ArgumentNullException.ThrowIfNull(@event);

            var context = new CommandContext
            {
                UserId = userId,
                CancellationToken = cancellationToken
            };
            return eventBus.PublishAsync(@event, context, cancellationToken);
        }

        /// <summary>
        /// Публикует событие с указанием пользователя и игровой сессии.
        /// </summary>
        public static Task PublishAsync(
            this IEventBus eventBus,
            IDomainEvent @event,
            Guid userId,
            Guid gameSessionId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventBus);
            ArgumentNullException.ThrowIfNull(@event);

            var context = new CommandContext
            {
                UserId = userId,
                GameSessionId = gameSessionId,
                CancellationToken = cancellationToken
            };
            return eventBus.PublishAsync(@event, context, cancellationToken);
        }

        /// <summary>
        /// Публикует набор событий с указанием пользователя.
        /// </summary>
        public static async Task PublishAsync(
            this IEventBus eventBus,
            IEnumerable<IDomainEvent> events,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventBus);
            ArgumentNullException.ThrowIfNull(events);

            var context = new CommandContext
            {
                UserId = userId,
                CancellationToken = cancellationToken
            };

            foreach (var @event in events)
            {
                await eventBus.PublishAsync(@event, context, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}