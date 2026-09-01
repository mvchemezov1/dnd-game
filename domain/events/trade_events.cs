#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    /// <summary>
    /// Интерфейс событий, связанных с торговыми предложениями.
    /// Все события торговли реализуют этот интерфейс для унификации обработки.
    /// </summary>
    public interface ITradeEvent : IAggregateEvent
    {
        /// <summary>
        /// Идентификатор торгового предложения, к которому относится событие.
        /// </summary>
        Guid OfferId { get; }
    }

    /// <summary>
    /// Редкость предмета в соответствии с общепринятой классификацией DnD.
    /// </summary>
    public enum Rarity
    {
        Common = 1,
        Uncommon = 2,
        Rare = 3,
        VeryRare = 4,
        Legendary = 5,
        Artifact = 6
    }

    /// <summary>
    /// Информация о предмете, участвующем в торговле.
    /// </summary>
    public record TradeItem
    {
        /// <summary>Идентификатор предмета.</summary>
        public string ItemId { get; init; } = string.Empty;

        /// <summary>Название предмета.</summary>
        public string ItemName { get; init; } = string.Empty;

        /// <summary>Количество предметов.</summary>
        public int Quantity { get; init; }

        /// <summary>Базовая цена предмета в золоте.</summary>
        public int BasePriceGold { get; init; }

        /// <summary>Является ли предмет магическим.</summary>
        public bool IsMagical { get; init; }

        /// <summary>Редкость предмета.</summary>
        public Rarity Rarity { get; init; }
    }

    /// <summary>Создано торговое предложение между двумя персонажами.</summary>
    public record TradeOfferCreated(
        Guid OfferId,
        Guid FromCharacterId,
        Guid ToCharacterId,
        List<TradeItem> OfferedItems,
        int OfferedGold,
        List<TradeItem> RequestedItems,
        int RequestedGold,
        DateTime OccurredOn) : ITradeEvent
    {
        public Guid AggregateId => OfferId;
    }

    /// <summary>Торговое предложение принято.</summary>
    public record TradeOfferAccepted(Guid OfferId, DateTime OccurredOn) : ITradeEvent
    {
        public Guid AggregateId => OfferId;
    }

    /// <summary>Торговое предложение отклонено.</summary>
    public record TradeOfferDeclined(Guid OfferId, DateTime OccurredOn) : ITradeEvent
    {
        public Guid AggregateId => OfferId;
    }

    /// <summary>Торговое предложение отменено инициатором.</summary>
    public record TradeOfferCancelled(Guid OfferId, DateTime OccurredOn) : ITradeEvent
    {
        public Guid AggregateId => OfferId;
    }

    /// <summary>Предмет передан в рамках торговли.</summary>
    public record TradeItemTransferred(Guid OfferId, Guid CharacterId, string ItemId, int Quantity) : ITradeEvent
    {
        public Guid AggregateId => OfferId;
    }

    /// <summary>Золото передано в рамках торговли.</summary>
    public record TradeGoldTransferred(Guid OfferId, Guid CharacterId, int Amount) : ITradeEvent
    {
        public Guid AggregateId => OfferId;
    }

    /// <summary>Торговля завершилась неудачей (указана причина).</summary>
    public record TradeFailed(Guid OfferId, string Reason) : ITradeEvent
    {
        public Guid AggregateId => OfferId;
    }
}