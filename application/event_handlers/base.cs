using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.events;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Базовый интерфейс обработчика доменных событий.
    /// </summary>
    public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
    {
        Task Handle(TEvent @event, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Интерфейс публикации событий. Позволяет обработчикам инициировать новые события,
    /// моделируя цепные реакции (например, атака заклинанием вызывает спасброски у всех целей).
    /// </summary>
    public interface IEventPublisher
    {
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
            where TEvent : IDomainEvent;
    }

    /// <summary>
    /// Интерфейс саги (процесс-менеджера). Отслеживает длительные взаимодействия,
    /// коррелирует события и выдаёт команды.
    /// </summary>
    public interface ISaga<TState> where TState : class
    {
        TState State { get; }
        Task TransitionAsync(IDomainEvent @event, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Базовый класс для обработчиков событий. Предоставляет доступ к публикатору событий
    /// и общие проверки игрового состояния.
    /// </summary>
    public abstract class EventHandlerBase(IEventPublisher publisher)
    {
        protected readonly IEventPublisher _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));

        /// <summary>
        /// Проверяет, жив ли персонаж с указанным идентификатором.
        /// Реальная реализация должна обращаться к модели чтения или хранилищу событий.
        /// </summary>
        protected virtual Task<bool> IsCharacterAliveAsync(Guid characterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Заглушка: считаем, что персонаж жив.
            return Task.FromResult(true);
        }

        /// <summary>
        /// Проверяет, активно ли игровое состояние (например, не в меню, не на паузе).
        /// </summary>
        protected virtual Task<bool> IsGameActiveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Базовый класс для обработчиков событий, связанных с конкретным персонажем.
    /// Автоматически игнорирует событие, если персонаж мёртв (или не существует).
    /// </summary>
    public abstract class CharacterEventHandlerBase<TEvent>(IEventPublisher publisher) : EventHandlerBase(publisher),
                                                              IEventHandler<TEvent>
        where TEvent : IDomainEvent
    {
        public async Task Handle(TEvent @event, CancellationToken cancellationToken)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            cancellationToken.ThrowIfCancellationRequested();

            // Если событие содержит информацию о персонаже, проверяем его жизнеспособность.
            if (@event is ICharacterEvent characterEvent)
            {
                if (!await IsCharacterAliveAsync(characterEvent.CharacterId, cancellationToken))
                    return; // Мёртвые персонажи не обрабатывают события.
            }

            await HandleCoreAsync(@event, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Основная логика обработчика. Вызывается после всех предварительных проверок.
        /// </summary>
        protected abstract Task HandleCoreAsync(TEvent @event, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Интерфейс события, связанного с персонажем. Предоставляет идентификатор персонажа
    /// для автоматических проверок жизнеспособности.
    /// </summary>
    public interface ICharacterEvent
    {
        Guid CharacterId { get; }
    }

    /// <summary>
    /// Интерфейс для событий-реакций (например, атака при возможности, заклинание Shield).
    /// Содержит описание триггера, позволяющее обработчикам понять, когда реакция применима.
    /// </summary>
    public interface IReactionEvent : IDomainEvent
    {
        string ReactionTriggerDescription { get; }
    }

    /// <summary>
    /// Атрибут для указания приоритета обработчика событий. Чем меньше число, тем раньше выполняется обработчик.
    /// Используется диспетчером событий для соблюдения порядка (например, снижение урона до применения хитов).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class EventHandlerPriorityAttribute(int priority) : Attribute
    {
        public int Priority { get; } = priority;
    }
}