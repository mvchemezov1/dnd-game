#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События состояний персонажей (Conditions).
    // Все события реализуют ICharacterEvent, что позволяет обрабатывать их в общих проекциях
    // и использовать идентификатор персонажа (CharacterId) как идентификатор агрегата.
    // --------------------------------------------------------------------------------------------

    /// <summary>Наложено состояние с указанием длительности в раундах.</summary>
    public record ConditionAppliedWithDuration(
        Guid CharacterId,
        string Condition,
        int DurationRounds,
        DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Оставшаяся длительность состояния уменьшена (например, в конце раунда).</summary>
    public record ConditionDurationDecreased(
        Guid CharacterId,
        string Condition,
        int RemainingRounds) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Состояние истекло и было автоматически снято.</summary>
    public record ConditionExpired(
        Guid CharacterId,
        string Condition) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- События, связанные с источником состояния ----------

    /// <summary>Наложено состояние с указанием источника (кто/что наложил).</summary>
    public record ConditionAppliedBySource(
        Guid CharacterId,
        string Condition,
        Guid SourceCharacterId,
        string SourceDescription,
        DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Состояние снято конкретным персонажем.</summary>
    public record ConditionRemovedBySource(
        Guid CharacterId,
        string Condition,
        Guid RemovedByCharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Спасброски против состояний ----------

    /// <summary>Выполнен спасбросок против действия состояния.</summary>
    public record ConditionSavingThrowAttempted(
        Guid CharacterId,
        string Condition,
        string Ability,
        int DifficultyClass,
        int RollResult,
        bool Success) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Состояния, связанные с концентрацией ----------

    /// <summary>Для поддержания состояния требуется концентрация на заклинании.</summary>
    public record ConditionConcentrationRequired(
        Guid CharacterId,
        string Condition,
        string SpellId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Концентрация, поддерживающая состояние, была прервана.</summary>
    public record ConditionConcentrationBroken(
        Guid CharacterId,
        string Condition) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Усталость (Exhaustion) ----------

    /// <summary>Уровень истощения повышен.</summary>
    public record ExhaustionLevelIncreased(
        Guid CharacterId,
        int NewExhaustionLevel) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Уровень истощения понижен.</summary>
    public record ExhaustionLevelDecreased(
        Guid CharacterId,
        int NewExhaustionLevel) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Истощение полностью снято.</summary>
    public record ExhaustionRemoved(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Отравление и болезни ----------

    /// <summary>Персонаж отравлен, задан тип яда, длительность и сложность спасброска.</summary>
    public record PoisonApplied(
        Guid CharacterId,
        string PoisonType,
        int DurationRounds,
        int SaveDC) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Совершён спасбросок против яда.</summary>
    public record PoisonSaveAttempted(
        Guid CharacterId,
        string PoisonType,
        int DC,
        int Roll,
        bool Success) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж заболел, заданы название болезни, инкубационный период и сложность спасброска.</summary>
    public record DiseaseApplied(
        Guid CharacterId,
        string DiseaseName,
        int IncubationDays,
        int SaveDC) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Болезнь прогрессировала до новой стадии.</summary>
    public record DiseaseProgressed(
        Guid CharacterId,
        string DiseaseName,
        int NewStage) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Болезнь излечена.</summary>
    public record DiseaseCured(
        Guid CharacterId,
        string DiseaseName) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Паралич, оцепенение, бессознательность ----------

    /// <summary>Персонаж парализован на указанное количество раундов.</summary>
    public record ParalyzedConditionApplied(
        Guid CharacterId,
        int DurationRounds) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж оглушён на указанное количество раундов.</summary>
    public record StunnedConditionApplied(
        Guid CharacterId,
        int DurationRounds) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж потерял сознание по указанной причине.</summary>
    public record UnconsciousConditionApplied(
        Guid CharacterId,
        string Reason) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж окаменел.</summary>
    public record PetrifiedConditionApplied(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Магические эффекты (очарование, страх и т.д.) ----------

    /// <summary>Персонаж очарован указанным источником.</summary>
    public record CharmedConditionApplied(
        Guid CharacterId,
        Guid SourceCharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж напуган указанным источником.</summary>
    public record FrightenedConditionApplied(
        Guid CharacterId,
        Guid SourceCharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж ослеплён на указанное количество раундов.</summary>
    public record BlindedConditionApplied(
        Guid CharacterId,
        int DurationRounds) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж оглох на указанное количество раундов.</summary>
    public record DeafenedConditionApplied(
        Guid CharacterId,
        int DurationRounds) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж стал невидимым.</summary>
    public record InvisibleConditionApplied(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Групповые состояния ----------

    /// <summary>Состояние наложено на несколько персонажей одновременно.</summary>
    public record ConditionAppliedToMultiple(
        IEnumerable<Guid> CharacterIds,
        string Condition,
        int DurationRounds) : IDomainEvent;

    /// <summary>Состояние снято с нескольких персонажей одновременно.</summary>
    public record ConditionRemovedFromMultiple(
        IEnumerable<Guid> CharacterIds,
        string Condition) : IDomainEvent;

    // ---------- Снятие состояний при исцелении/отдыхе ----------

    /// <summary>Несколько состояний сняты в результате отдыха.</summary>
    public record ConditionsClearedByRest(
        Guid CharacterId,
        IEnumerable<string> ConditionsRemoved) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Состояние снято в результате исцеления.</summary>
    public record ConditionsClearedByHealing(
        Guid CharacterId,
        string Condition) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Сопротивления и иммунитеты (связанные с состояниями) ----------

    /// <summary>Персонаж успешно сопротивляется состоянию благодаря источнику сопротивления.</summary>
    public record ConditionResisted(
        Guid CharacterId,
        string Condition,
        string ResistanceSource) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж невосприимчив к состоянию.</summary>
    public record ConditionImmune(
        Guid CharacterId,
        string Condition) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }
}