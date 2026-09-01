#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События магии и заклинаний. Включают изучение, использование ячеек, концентрацию,
    // ритуалы, магические предметы, контрзаклинания, а также урон и лечение от заклинаний.
    // --------------------------------------------------------------------------------------------

    // ---------- Базовые события ----------

    /// <summary>Заклинание произнесено. Указывает заклинателя, заклинание и цель (если есть).</summary>
    public record SpellCast(
        Guid CasterId,
        Guid SpellId,
        Guid? TargetId,
        DateTime OccurredOn) : IDomainEvent;

    /// <summary>Магический эффект применён к цели с указанием длительности.</summary>
    public record MagicEffectApplied(
        Guid TargetId,
        string Effect,
        int Duration,
        DateTime OccurredOn) : IDomainEvent;

    // ---------- Подготовка и известные заклинания ----------

    /// <summary>Заклинание изучено персонажем.</summary>
    public record SpellLearned(
        Guid CasterId,
        string SpellId) : IDomainEvent;

    /// <summary>Заклинание забыто персонажем.</summary>
    public record SpellForgotten(
        Guid CasterId,
        string SpellId) : IDomainEvent;

    // ---------- Ячейки заклинаний ----------

    /// <summary>Израсходована ячейка заклинания указанного уровня.</summary>
    public record SpellSlotConsumed(
        Guid CasterId,
        int SlotLevel) : IDomainEvent;

    // ---------- Концентрация ----------

    /// <summary>Выполнена проверка концентрации на заклинании.</summary>
    public record ConcentrationCheckMade(
        Guid CasterId,
        string SpellId,
        int DC,
        int RollResult,
        bool Success) : IDomainEvent;

    // ---------- Ритуалы ----------

    /// <summary>Начато ритуальное сотворение заклинания.</summary>
    public record RitualCastStarted(
        Guid CasterId,
        string SpellId,
        int CastingTimeMinutes) : IDomainEvent;

    /// <summary>Ритуал завершён, заклинание сотворено.</summary>
    public record RitualCastCompleted(
        Guid CasterId,
        string SpellId) : IDomainEvent;

    // ---------- Свитки и магические предметы ----------

    /// <summary>Использован свиток заклинания.</summary>
    public record ScrollUsed(
        Guid UserId,
        string ScrollItemId,
        string SpellId) : IDomainEvent;

    /// <summary>Активирован магический предмет.</summary>
    public record MagicItemActivated(
        Guid UserId,
        string ItemId,
        string EffectDescription) : IDomainEvent;

    /// <summary>Израсходован заряд волшебной палочки, осталось указанное количество зарядов.</summary>
    public record WandChargeUsed(
        Guid UserId,
        string ItemId,
        int RemainingCharges) : IDomainEvent;

    // ---------- Диспелл и контрзаклинания ----------

    /// <summary>Заклинание развеяно (диспелл).</summary>
    public record SpellDispelled(
        Guid CasterId,           // персонаж, чьё заклинание развеяно
        Guid TargetSpellId,
        string DispellerId) : IDomainEvent;

    /// <summary>Попытка контрзаклинания (Counterspell).</summary>
    public record CounterSpellAttempted(
        Guid CasterId,           // тот, кто пытается контрзаклинание
        Guid OriginalCasterId,   // исходный заклинатель
        string OriginalSpellId,
        int SlotLevelUsed) : IDomainEvent;

    /// <summary>Контрзаклинание разрешено: успех или провал.</summary>
    public record CounterSpellResolved(
        Guid CasterId,
        string OriginalSpellId,
        bool Successful) : IDomainEvent;

    // ---------- Урон и исцеление от заклинаний ----------

    /// <summary>Нанесён урон заклинанием.</summary>
    public record SpellDamageDealt(
        Guid CasterId,
        string SpellId,
        Guid TargetId,
        int DamageAmount,
        string DamageType) : IDomainEvent;

    /// <summary>Произведено исцеление заклинанием.</summary>
    public record SpellHealingDealt(
        Guid CasterId,
        string SpellId,
        Guid TargetId,
        int HealingAmount) : IDomainEvent;

    // ---------- Спасброски от заклинаний ----------

    /// <summary>Цель совершила спасбросок против заклинания.</summary>
    public record SpellSavingThrowAttempted(
        Guid TargetId,
        string SpellId,
        string Ability,
        int DC,
        int RollResult,
        bool Success) : IDomainEvent;

    // ---------- Области воздействия и множественные цели ----------

    /// <summary>Заклинание с областью воздействия наложено на несколько целей.</summary>
    public record AreaOfEffectSpellCast(
        Guid CasterId,
        string SpellId,
        List<Guid> AffectedTargets) : IDomainEvent;
}