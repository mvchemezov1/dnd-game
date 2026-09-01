#nullable enable
using System;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // Базовый интерфейс
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Маркерный интерфейс для всех доменных событий.
    /// </summary>
    public interface IDomainEvent
    {
    }

    // --------------------------------------------------------------------------------------------
    // Событие с метаданными (временная метка, источник)
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Событие, содержащее обязательную временную метку возникновения.
    /// Все события, сохраняемые в EventStore, должны реализовывать этот интерфейс.
    /// </summary>
    public interface ITimestampedEvent : IDomainEvent
    {
        /// <summary>
        /// Дата и время возникновения события (UTC).
        /// </summary>
        DateTime OccurredOn { get; }
    }

    /// <summary>
    /// Событие, инициированное конкретным пользователем (игроком или мастером).
    /// </summary>
    public interface IUserInitiatedEvent : IDomainEvent
    {
        /// <summary>
        /// Идентификатор пользователя, инициировавшего событие.
        /// </summary>
        Guid UserId { get; }
    }

    /// <summary>
    /// Событие, относящееся к определённой игровой сессии (кампании).
    /// </summary>
    public interface ISessionBoundEvent : IDomainEvent
    {
        /// <summary>
        /// Идентификатор игровой сессии.
        /// </summary>
        Guid GameSessionId { get; }
    }

    // --------------------------------------------------------------------------------------------
    // События, связанные с агрегатами
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Событие, принадлежащее конкретному агрегату.
    /// </summary>
    public interface IAggregateEvent : IDomainEvent
    {
        /// <summary>
        /// Идентификатор агрегата, к которому относится событие.
        /// </summary>
        Guid AggregateId { get; }
    }

    /// <summary>
    /// Событие, несущее версию агрегата после применения.
    /// Используется для оптимистической блокировки и воспроизведения.
    /// </summary>
    public interface IVersionedEvent : IAggregateEvent
    {
        /// <summary>
        /// Версия агрегата после применения события.
        /// </summary>
        int Version { get; }
    }

    // --------------------------------------------------------------------------------------------
    // События, связанные с персонажем
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Событие, затрагивающее конкретного персонажа.
    /// </summary>
    public interface ICharacterEvent : IAggregateEvent
    {
        /// <summary>
        /// Идентификатор персонажа.
        /// </summary>
        Guid CharacterId { get; }
    }

    /// <summary>
    /// Событие, связанное с действием одного персонажа по отношению к другому (атака, лечение и т.д.).
    /// </summary>
    public interface ICharacterInteractionEvent : ICharacterEvent
    {
        /// <summary>
        /// Идентификатор персонажа-источника действия.
        /// </summary>
        Guid SourceCharacterId { get; }

        /// <summary>
        /// Идентификатор персонажа-цели действия.
        /// </summary>
        Guid TargetCharacterId { get; }
    }

    // --------------------------------------------------------------------------------------------
    // События боя
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Событие, относящееся к конкретному бою.
    /// </summary>
    public interface ICombatEvent : IAggregateEvent
    {
        /// <summary>
        /// Идентификатор боя.
        /// </summary>
        Guid CombatId { get; }
    }

    /// <summary>
    /// Событие, связанное с действием участника боя.
    /// </summary>
    public interface ICombatActionEvent : ICombatEvent
    {
        /// <summary>
        /// Идентификатор участника боя, совершившего действие.
        /// </summary>
        Guid ParticipantId { get; }
    }

    // --------------------------------------------------------------------------------------------
    // События кампании
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Событие, относящееся к конкретной кампании.
    /// </summary>
    public interface ICampaignEvent : IAggregateEvent
    {
        /// <summary>
        /// Идентификатор кампании.
        /// </summary>
        Guid CampaignId { get; }
    }

    // --------------------------------------------------------------------------------------------
    // Базовый абстрактный класс события
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Удобная базовая реализация доменного события.
    /// Наследники получают стандартные метаданные и могут быть сериализованы.
    /// </summary>
    public abstract record BaseDomainEvent : ITimestampedEvent, IAggregateEvent
    {
        /// <inheritdoc/>
        public Guid AggregateId { get; init; }

        /// <inheritdoc/>
        public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Базовое событие, связанное с персонажем.
    /// </summary>
    public abstract record CharacterDomainEvent : BaseDomainEvent, ICharacterEvent
    {
        /// <inheritdoc/>
        public Guid CharacterId => AggregateId;
    }

    /// <summary>
    /// Базовое событие, связанное с боевой сценой.
    /// </summary>
    public abstract record CombatDomainEvent : BaseDomainEvent, ICombatEvent
    {
        /// <inheritdoc/>
        public Guid CombatId => AggregateId;
    }

    /// <summary>
    /// Базовое событие, связанное с кампанией.
    /// </summary>
    public abstract record CampaignDomainEvent : BaseDomainEvent, ICampaignEvent
    {
        /// <inheritdoc/>
        public Guid CampaignId => AggregateId;
    }
}