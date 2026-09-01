using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{

    // ---------- Управление боем ----------

    /// <summary>Бой начался. Содержит список участников и их скорости.</summary>
    public record CombatStarted(
        Guid CombatId,
        List<Guid> Participants,
        Dictionary<Guid, int> ParticipantSpeeds,
        List<Guid>? PlayerCharacterIds,
        DateTime OccurredOn) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Бой завершён.</summary>
    public record CombatEnded(Guid CombatId, DateTime OccurredOn) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Инициатива и раунды ----------

    /// <summary>Бросок инициативы участника.</summary>
    public record InitiativeRolled(
        Guid CombatId,
        Guid CharacterId,
        int Initiative,
        int DexterityModifier) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Начат новый раунд боя.</summary>
    public record CombatRoundStarted(Guid CombatId, int Round, DateTime OccurredOn) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Ходы ----------

    /// <summary>Начат ход персонажа.</summary>
    public record CombatTurnStarted(Guid CombatId, Guid CharacterId, DateTime OccurredOn) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Ход персонажа завершён.</summary>
    public record CombatTurnEnded(Guid CombatId, Guid CharacterId, DateTime OccurredOn) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Участники ----------

    /// <summary>В бой добавлен новый участник.</summary>
    public record ParticipantAddedToCombat(Guid CombatId, Guid CharacterId, int Initiative) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Участник удалён из боя.</summary>
    public record ParticipantRemovedFromCombat(Guid CombatId, Guid CharacterId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Действия ----------

    /// <summary>Использовано основное действие.</summary>
    public record CombatActionTaken(Guid CombatId, Guid CharacterId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Использовано бонусное действие.</summary>
    public record CombatBonusActionTaken(Guid CombatId, Guid CharacterId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Использована реакция.</summary>
    public record CombatReactionUsed(Guid CombatId, Guid CharacterId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Перемещение ----------

    /// <summary>Персонаж потратил часть перемещения.</summary>
    public record CombatMovementUsed(Guid CombatId, Guid CharacterId, int Feet) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Состояния ----------

    /// <summary>На участника наложено состояние.</summary>
    public record ConditionAppliedToCombatant(Guid CombatId, Guid CharacterId, string Condition) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>С участника снято состояние.</summary>
    public record ConditionRemovedFromCombatant(Guid CombatId, Guid CharacterId, string Condition) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Концентрация ----------

    /// <summary>Участник начал концентрироваться на заклинании.</summary>
    public record CombatConcentrationStarted(Guid CombatId, Guid CharacterId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Концентрация участника прекращена.</summary>
    public record CombatConcentrationEnded(Guid CombatId, Guid CharacterId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    // ---------- Урон, лечение, спасброски и прочее ----------

    /// <summary>Нанесён урон в бою.</summary>
    public record CombatDamageDealt(
        Guid CombatId,
        Guid SourceId,
        Guid TargetId,
        int Amount,
        string DamageType) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Произведено лечение в бою.</summary>
    public record CombatHealingDealt(
        Guid CombatId,
        Guid SourceId,
        Guid TargetId,
        int Amount) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Выполнен спасбросок.</summary>
    public record CombatSavingThrowMade(
        Guid CombatId,
        Guid ParticipantId,
        string Ability,
        int DC,
        int Roll,
        int Modifiers) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Выполнен спасбросок от смерти.</summary>
    public record CombatDeathSavingThrowMade(
        Guid CombatId,
        Guid ParticipantId,
        int Roll) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Участник стабилизирован.</summary>
    public record CombatParticipantStabilized(
        Guid CombatId,
        Guid ParticipantId,
        Guid StabilizedBy) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Выполнена проверка концентрации.</summary>
    public record CombatConcentrationCheckMade(
        Guid CombatId,
        Guid ParticipantId,
        int DC,
        int Roll,
        int ConMod) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Участник отложил свой ход.</summary>
    public record CombatTurnDelayed(Guid CombatId, Guid ParticipantId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Участник сдался.</summary>
    public record CombatSurrender(Guid CombatId, Guid ParticipantId) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Раунд боя завершён.</summary>
    public record CombatRoundEnded(Guid CombatId, int Round, DateTime OccurredOn) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Участник подготовил действие.</summary>
    public record CombatActionReadied(
        Guid CombatId,
        Guid CharacterId,
        string ActionType,
        string TriggerCondition) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }

    /// <summary>Подготовленное действие сработало.</summary>
    public record CombatReadiedActionTriggered(
        Guid CombatId,
        Guid CharacterId,
        string ActionType) : ICombatEvent
    {
        public Guid AggregateId => CombatId;
    }
}