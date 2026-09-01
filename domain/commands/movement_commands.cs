#nullable enable
using System;

namespace dnd_game.domain.commands
{
    // ---------- Базовое перемещение на тактической карте ----------

    /// <summary>Переместить персонажа в указанную точку (тип перемещения по умолчанию "Walk").</summary>
    public record MoveCharacter(Guid CharacterId, int TargetX, int TargetY) : ICommand;

    /// <summary>Переместить персонажа с указанием типа движения (Walk, Climb, Swim и т.д.).</summary>
    public record MoveCharacterToPosition(
        Guid CharacterId,
        int TargetX,
        int TargetY,
        string MovementType) : ICommand;

    // ---------- Действия, связанные с перемещением ----------

    /// <summary>Использовать рывок (Dash) — удвоение скорости на текущий ход.</summary>
    public record MoveCharacterWithDash(Guid CharacterId) : ICommand;

    /// <summary>Использовать отход (Disengage) — избежать атак при перемещении.</summary>
    public record MoveCharacterWithDisengage(Guid CharacterId) : ICommand;

    /// <summary>Попытаться скрыться (Hide).</summary>
    public record MoveCharacterStealthily(Guid CharacterId) : ICommand;

    // ---------- Специальные виды движения ----------

    /// <summary>Взобраться (Climb) на указанное расстояние.</summary>
    public record ClimbCharacter(
        Guid CharacterId,
        int DistanceFeet,
        int ClimbSpeedUsed = 0) : ICommand;

    /// <summary>Плыть (Swim) на указанное расстояние.</summary>
    public record SwimCharacter(
        Guid CharacterId,
        int DistanceFeet,
        int SwimSpeedUsed = 0) : ICommand;

    /// <summary>Лететь (Fly) на указанное расстояние.</summary>
    public record FlyCharacter(
        Guid CharacterId,
        int DistanceFeet,
        int FlySpeedUsed = 0) : ICommand;

    /// <summary>Копать (Burrow) на указанное расстояние.</summary>
    public record BurrowCharacter(
        Guid CharacterId,
        int DistanceFeet,
        int BurrowSpeedUsed = 0) : ICommand;

    // ---------- Прыжки ----------

    /// <summary>Совершить прыжок (Jump) определённого типа.</summary>
    public record JumpCharacter(
        Guid CharacterId,
        string JumpType,
        int StrengthScore,
        bool RunningStart) : ICommand;

    // ---------- Управление скоростью ----------

    /// <summary>Установить временную скорость персонажа.</summary>
    public record SetCharacterSpeed(
        Guid CharacterId,
        int NewSpeed,
        string MovementType = "Walk") : ICommand;

    /// <summary>Сбросить скорость персонажа к базовой.</summary>
    public record ResetCharacterSpeed(Guid CharacterId) : ICommand;

    // ---------- Модификаторы местности и окружения ----------

    /// <summary>Применить штраф трудной местности.</summary>
    public record ApplyDifficultTerrain(Guid CharacterId, int Multiplier) : ICommand;

    /// <summary>Убрать штраф трудной местности.</summary>
    public record RemoveDifficultTerrain(Guid CharacterId) : ICommand;

    /// <summary>Применить ограничение движения (например, захват, опутывание).</summary>
    public record ApplyMovementImpairment(
        Guid CharacterId,
        string ImpairmentType,
        int SpeedReduction) : ICommand;

    /// <summary>Снять ограничение движения.</summary>
    public record RemoveMovementImpairment(
        Guid CharacterId,
        string ImpairmentType) : ICommand;

    // ---------- Проверки навыков, связанные с перемещением ----------

    /// <summary>Совершить проверку Атлетики при перемещении.</summary>
    public record MakeAthleticsCheckForMovement(
        Guid CharacterId,
        int DifficultyClass,
        int RollResult,
        int ProficiencyBonus,
        int StrengthModifier) : ICommand;

    /// <summary>Совершить проверку Акробатики при перемещении.</summary>
    public record MakeAcrobaticsCheckForMovement(
        Guid CharacterId,
        int DifficultyClass,
        int RollResult,
        int ProficiencyBonus,
        int DexterityModifier) : ICommand;

    // ---------- Падение и урон от падения ----------

    /// <summary>Получить урон от падения.</summary>
    public record TakeFallDamage(Guid CharacterId, int FallDistanceFeet) : ICommand;

    // ---------- Путешествия по глобальной карте ----------

    /// <summary>Начать путешествие группы.</summary>
    public record StartJourneyCommand(
        Guid PartyId,
        Guid RouteId,
        string Pace) : ICommand;

    /// <summary>Завершить путешествие.</summary>
    public record EndJourneyCommand(Guid PartyId) : ICommand;

    /// <summary>Пройти один день путешествия.</summary>
    public record TravelDayCommand(
        Guid PartyId,
        string Terrain,
        int HoursTraveled,
        int NavigationCheckResult) : ICommand;

    /// <summary>Установить темп путешествия.</summary>
    public record SetTravelPaceCommand(Guid PartyId, string Pace) : ICommand;

    /// <summary>Совершить форсированный марш (сверх обычного времени).</summary>
    public record ForcedMarchCommand(Guid PartyId, int AdditionalHours) : ICommand;

    /// <summary>Выполнить проверку навигации.</summary>
    public record NavigationCheckCommand(
        Guid PartyId,
        int Roll,
        int WisdomModifier,
        bool IsProficient) : ICommand;

    /// <summary>Группа потерялась.</summary>
    public record PartyLostCommand(Guid PartyId) : ICommand;

    /// <summary>Потребить ресурсы (еду, воду) за указанное количество дней.</summary>
    public record ConsumeResourcesCommand(Guid PartyId, int Days) : ICommand;

    /// <summary>Проверить случайную встречу.</summary>
    public record RandomEncounterCheckCommand(Guid PartyId, string Terrain) : ICommand;

    /// <summary>Применить эффект истощения (Exhaustion) к группе.</summary>
    public record ApplyExhaustionCommand(Guid PartyId, int ExhaustionLevel) : ICommand;
}