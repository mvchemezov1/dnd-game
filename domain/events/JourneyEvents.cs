#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    /// <summary>Базовый интерфейс событий путешествия.</summary>
    public interface IJourneyEvent : IAggregateEvent
    {
        Guid JourneyId { get; }
    }

    /// <summary>Путешествие начато.</summary>
    public record JourneyStarted(
        Guid JourneyId,
        Guid PartyId,
        Guid RouteId,
        string Pace,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Путешествие завершено.</summary>
    public record JourneyEnded(
        Guid JourneyId,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Пройден один день путешествия.</summary>
    public record JourneyDayAdvanced(
        Guid JourneyId,
        string Terrain,
        int HoursTraveled,
        int NavigationCheckResult,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Изменён темп путешествия.</summary>
    public record JourneyPaceChanged(
        Guid JourneyId,
        string NewPace,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Выполнен форсированный марш.</summary>
    public record ForcedMarchPerformed(
        Guid JourneyId,
        int AdditionalHours,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Выполнена проверка навигации.</summary>
    public record NavigationCheckPerformed(
        Guid JourneyId,
        int Roll,
        int WisdomModifier,
        bool IsProficient,
        bool Success,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Группа потерялась.</summary>
    public record PartyLost(
        Guid JourneyId,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Потреблены ресурсы (еда, вода).</summary>
    public record ResourcesConsumed(
        Guid JourneyId,
        int Days,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Проверена случайная встреча.</summary>
    public record RandomEncounterChecked(
        Guid JourneyId,
        string Terrain,
        bool EncounterOccurred,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }

    /// <summary>Применено истощение.</summary>
    public record ExhaustionApplied(
        Guid JourneyId,
        int ExhaustionLevel,
        DateTime OccurredOn) : IJourneyEvent
    {
        public Guid AggregateId => JourneyId;
    }
}