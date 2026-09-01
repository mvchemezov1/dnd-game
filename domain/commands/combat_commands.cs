#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.commands
{
    // ---------- Управление боем ----------

    /// <summary>
    /// Начать новый бой с указанными участниками и их скоростями.
    /// </summary>
    public record StartCombat(
        Guid CombatId,
        List<Guid> Participants,
        Dictionary<Guid, int>? ParticipantSpeeds = null,
        List<Guid>? PlayerCharacterIds = null) : ICommand;

    /// <summary>
    /// Завершить текущий бой.
    /// </summary>
    public record EndCombat(Guid CombatId) : ICommand;

    // ---------- Инициатива ----------

    /// <summary>
    /// Бросить инициативу для участника боя.
    /// </summary>
    public record RollInitiative(
        Guid CombatId,
        Guid ParticipantId,
        int InitiativeRoll,
        int DexterityModifier) : ICommand;

    // ---------- Раунды и ходы ----------

    /// <summary>Начать новый раунд боя.</summary>
    public record StartRound(Guid CombatId) : ICommand;

    /// <summary>Передать ход следующему участнику.</summary>
    public record NextTurn(Guid CombatId) : ICommand;

    /// <summary>Завершить текущий раунд.</summary>
    public record EndRound(Guid CombatId) : ICommand;

    // ---------- Участники ----------

    /// <summary>Добавить участника в бой с указанной инициативой.</summary>
    public record AddParticipantToCombat(
        Guid CombatId,
        Guid ParticipantId,
        int Initiative) : ICommand;

    /// <summary>Удалить участника из боя.</summary>
    public record RemoveParticipantFromCombat(
        Guid CombatId,
        Guid ParticipantId) : ICommand;

    // ---------- Действия ----------

    /// <summary>Выполнить стандартное действие (атака, заклинание и т.д.).</summary>
    public record TakeStandardAction(
        Guid CombatId,
        Guid ParticipantId,
        string ActionType,
        Guid? TargetId = null,
        object? ActionData = null) : ICommand;

    /// <summary>Выполнить бонусное действие.</summary>
    public record TakeBonusAction(
        Guid CombatId,
        Guid ParticipantId,
        string ActionType,
        Guid? TargetId = null,
        object? ActionData = null) : ICommand;

    /// <summary>Использовать реакцию.</summary>
    public record TakeReaction(
        Guid CombatId,
        Guid ParticipantId,
        string ReactionType,
        string TriggerDescription,
        Guid? TargetId = null) : ICommand;

    // ---------- Перемещение ----------

    /// <summary>Потратить перемещение на указанное расстояние в футах.</summary>
    public record TakeMoveAction(
        Guid CombatId,
        Guid ParticipantId,
        int DistanceFeet) : ICommand;

    // ---------- Готовое действие ----------

    /// <summary>Подготовить действие с условием срабатывания.</summary>
    public record ReadyAction(
        Guid CombatId,
        Guid ParticipantId,
        string ActionToReady,
        string TriggerCondition) : ICommand;

    /// <summary>Активировать подготовленное действие.</summary>
    public record TriggerReadyAction(
        Guid CombatId,
        Guid ParticipantId) : ICommand;

    // ---------- Урон и лечение ----------

    /// <summary>Нанести урон цели в бою.</summary>
    public record DealDamageToTarget(
        Guid CombatId,
        Guid SourceParticipantId,
        Guid TargetParticipantId,
        int DamageAmount,
        string DamageType) : ICommand;

    /// <summary>Исцелить цель в бою.</summary>
    public record HealTarget(
        Guid CombatId,
        Guid SourceParticipantId,
        Guid TargetParticipantId,
        int HealingAmount) : ICommand;

    // ---------- Состояния ----------

    /// <summary>Наложить состояние на цель.</summary>
    public record ApplyConditionToTarget(
        Guid CombatId,
        Guid TargetParticipantId,
        string ConditionType,
        int DurationRounds) : ICommand;

    /// <summary>Снять состояние с цели.</summary>
    public record RemoveConditionFromTarget(
        Guid CombatId,
        Guid TargetParticipantId,
        string ConditionType) : ICommand;

    // ---------- Спасброски ----------

    /// <summary>Совершить спасбросок в бою.</summary>
    public record MakeSavingThrowInCombat(
        Guid CombatId,
        Guid ParticipantId,
        string Ability,
        int DifficultyClass,
        int RollResult,
        int Modifiers) : ICommand;

    /// <summary>Совершить спасбросок от смерти в бою.</summary>
    public record MakeDeathSavingThrowInCombat(
        Guid CombatId,
        Guid ParticipantId,
        int RollResult) : ICommand;

    /// <summary>Стабилизировать участника в бою.</summary>
    public record StabilizeInCombat(
        Guid CombatId,
        Guid ParticipantId,
        Guid StabilizedByParticipantId) : ICommand;

    // ---------- Концентрация ----------

    /// <summary>Совершить проверку концентрации.</summary>
    public record MakeConcentrationCheck(
        Guid CombatId,
        Guid ParticipantId,
        int DifficultyClass,
        int RollResult,
        int ConstitutionModifier) : ICommand;

    // ---------- Особые действия ----------

    /// <summary>Отложить ход участника.</summary>
    public record DelayTurn(
        Guid CombatId,
        Guid ParticipantId) : ICommand;

    /// <summary>Сдаться в бою.</summary>
    public record SurrenderInCombat(
        Guid CombatId,
        Guid ParticipantId) : ICommand;

    // ---------- Вспомогательные действия ----------

    /// <summary>Выполнить действие помощи цели.</summary>
    public record HelpAction(
        Guid CombatId,
        Guid HelperId,
        Guid TargetId) : ICommand;

    /// <summary>Попытаться скрыться.</summary>
    public record HideAction(
        Guid CombatId,
        Guid HiderId) : ICommand;

    /// <summary>Выполнить поиск.</summary>
    public record SearchAction(
        Guid CombatId,
        Guid SearcherId) : ICommand;

    /// <summary>Использовать объект.</summary>
    public record UseObjectAction(
        Guid CombatId,
        Guid UserId,
        Guid ObjectId) : ICommand;

    /// <summary>
    /// Универсальная команда для выполнения любого боевого действия.
    /// Используется, когда нужно передать действие с дополнительными параметрами.
    /// </summary>
    public record PerformAction(
        Guid CombatId,
        Guid ParticipantId,
        string ActionType,          // например, "Attack", "CastSpell", "Dash", "Disengage" и т.д.
        Guid? TargetId = null,
        object? ActionData = null   // дополнительные данные (например, заклинание, оружие)
    ) : ICommand;
}