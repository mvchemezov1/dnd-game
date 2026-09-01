#nullable enable
using System;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События перемещения персонажей. Включают базовое перемещение, специальные виды движения,
    // изменение скорости, модификаторы местности, проверки навыков и падение.
    // Все события привязаны к конкретному персонажу и реализуют ICharacterEvent.
    // --------------------------------------------------------------------------------------------

    /// <summary>Персонаж переместился из одной точки в другую.</summary>
    public record CharacterMoved(
        Guid CharacterId,
        int FromX,
        int FromY,
        int ToX,
        int ToY,
        DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж переместился в указанную позицию с заданным типом движения.</summary>
    public record CharacterMovedToPosition(
        Guid CharacterId,
        int TargetX,
        int TargetY,
        string MovementType,
        DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Действия, связанные с перемещением ----------

    /// <summary>Персонаж использовал рывок (Dash).</summary>
    public record CharacterDashed(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж использовал отход (Disengage).</summary>
    public record CharacterDisengaged(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж попытался скрыться (Hide).</summary>
    public record CharacterHid(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Специальные виды движения ----------

    /// <summary>Персонаж взобрался на указанное расстояние.</summary>
    public record CharacterClimbed(
        Guid CharacterId,
        int DistanceFeet,
        int ClimbSpeedUsed) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж проплыл указанное расстояние.</summary>
    public record CharacterSwam(
        Guid CharacterId,
        int DistanceFeet,
        int SwimSpeedUsed) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж пролетел указанное расстояние.</summary>
    public record CharacterFlew(
        Guid CharacterId,
        int DistanceFeet,
        int FlySpeedUsed) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж прокопал указанное расстояние.</summary>
    public record CharacterBurrowed(
        Guid CharacterId,
        int DistanceFeet,
        int BurrowSpeedUsed) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Прыжки ----------

    /// <summary>Персонаж совершил прыжок.</summary>
    public record CharacterJumped(
        Guid CharacterId,
        string JumpType,
        int StrengthScore,
        bool RunningStart,
        int DistanceFeet) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Управление скоростью ----------

    /// <summary>Скорость персонажа временно изменена.</summary>
    public record CharacterSpeedChanged(
        Guid CharacterId,
        int NewSpeed,
        string MovementType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Скорость персонажа сброшена к базовой.</summary>
    public record CharacterSpeedReset(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Модификаторы местности ----------

    /// <summary>Применён модификатор трудной местности.</summary>
    public record DifficultTerrainApplied(
        Guid CharacterId,
        int Multiplier) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Модификатор трудной местности снят.</summary>
    public record DifficultTerrainRemoved(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>На движение персонажа наложено ограничение.</summary>
    public record MovementImpaired(
        Guid CharacterId,
        string ImpairmentType,
        int SpeedReduction) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Ограничение движения снято.</summary>
    public record MovementRestored(
        Guid CharacterId,
        string ImpairmentType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Проверки навыков, связанные с перемещением ----------

    /// <summary>Выполнена проверка Атлетики при перемещении.</summary>
    public record AthleticsCheckForMovementMade(
        Guid CharacterId,
        int DifficultyClass,
        int RollResult,
        int ProficiencyBonus,
        int StrengthModifier,
        bool Success) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Выполнена проверка Акробатики при перемещении.</summary>
    public record AcrobaticsCheckForMovementMade(
        Guid CharacterId,
        int DifficultyClass,
        int RollResult,
        int ProficiencyBonus,
        int DexterityModifier,
        bool Success) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Падение ----------

    /// <summary>Персонаж получил урон от падения.</summary>
    public record FallDamageTaken(
        Guid CharacterId,
        int FallDistanceFeet,
        int DamageAmount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }
}