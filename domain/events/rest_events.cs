#nullable enable
using System;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События отдыха персонажа: начало, прерывание и завершение.
    // Все события реализуют ICharacterEvent и привязаны к идентификатору персонажа.
    // --------------------------------------------------------------------------------------------

    /// <summary>Персонаж начал отдых (короткий или длинный).</summary>
    public record RestStarted(
        Guid CharacterId,
        string RestType,
        DateTime Timestamp) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Отдых персонажа был прерван (например, из-за нападения).</summary>
    public record RestInterrupted(
        Guid CharacterId,
        string InterruptionType,
        DateTime Timestamp) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Отдых персонажа завершён, восстановлено указанное количество хитов.</summary>
    public record RestCompleted(
        Guid CharacterId,
        string RestType,
        int HitPointsRestored,
        DateTime Timestamp) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }
}