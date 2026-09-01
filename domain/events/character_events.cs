#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События персонажа: обновление параметров, навыков, заклинаний, инвентаря, состояний и т.д.
    // Все события реализуют ICharacterEvent, что позволяет обрабатывать их универсально.
    // --------------------------------------------------------------------------------------------

    /// <summary>Обновлён бонус мастерства персонажа.</summary>
    public record ProficiencyBonusUpdated(Guid CharacterId, int Bonus) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Заклинание подготовлено.</summary>
    public record SpellPrepared(Guid CharacterId, string SpellId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Подготовка заклинания снята.</summary>
    public record SpellUnprepared(Guid CharacterId, string SpellId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Использовано классовое умение.</summary>
    public record ClassFeatureUsed(Guid CharacterId, string FeatureId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Классовое умение перезаряжено.</summary>
    public record ClassFeatureRecharged(Guid CharacterId, string FeatureId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Предмет аттунен (магический предмет связан с персонажем).</summary>
    public record ItemAttuned(Guid CharacterId, string ItemId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Аттунемент с предметом разорван.</summary>
    public record ItemUnattuned(Guid CharacterId, string ItemId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Счётчики спасбросков от смерти сброшены.</summary>
    public record DeathSavingThrowsReset(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Создание и базовое обновление ----------

    /// <summary>Персонаж создан.</summary>
    public record CharacterCreated(Guid CharacterId, string Name, int MaxHitPoints, DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж получил урон.</summary>
    public record CharacterDamageTaken(Guid CharacterId, int Amount, DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж исцелён.</summary>
    public record CharacterHealed(Guid CharacterId, int Amount, DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж погиб.</summary>
    public record CharacterDied(Guid CharacterId, DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Основные данные персонажа обновлены (имя, максимальные хиты).</summary>
    public record CharacterUpdated(Guid CharacterId, string? Name, int? MaxHitPoints, DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Временные хиты ----------

    /// <summary>Временные хиты установлены.</summary>
    public record TemporaryHitPointsSet(Guid CharacterId, int Amount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Опыт и уровень ----------

    /// <summary>Персонаж получил опыт.</summary>
    public record ExperienceGained(Guid CharacterId, int Amount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж достиг нового уровня.</summary>
    public record CharacterLevelUp(Guid CharacterId, int NewLevel, int NewProficiencyBonus) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Характеристики ----------

    /// <summary>Значение характеристики установлено.</summary>
    public record AbilityScoreSet(Guid CharacterId, string Ability, int Score) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Раса, класс, предыстория ----------

    /// <summary>Выбрана раса персонажа.</summary>
    public record RaceChosen(Guid CharacterId, string Race) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Выбран класс персонажа.</summary>
    public record ClassChosen(Guid CharacterId, string ClassName) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Выбрана предыстория персонажа.</summary>
    public record BackgroundChosen(Guid CharacterId, string BackgroundName) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Владения навыками и спасбросками ----------

    /// <summary>Добавлено владение навыком.</summary>
    public record SkillProficiencyAdded(Guid CharacterId, string Skill) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Убрано владение навыком.</summary>
    public record SkillProficiencyRemoved(Guid CharacterId, string Skill) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Добавлено владение спасброском.</summary>
    public record SavingThrowProficiencyAdded(Guid CharacterId, string Ability) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Убрано владение спасброском.</summary>
    public record SavingThrowProficiencyRemoved(Guid CharacterId, string Ability) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Черты ----------

    /// <summary>Персонаж получил черту.</summary>
    public record FeatAdded(Guid CharacterId, string FeatName) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Черта удалена.</summary>
    public record FeatRemoved(Guid CharacterId, string FeatName) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Заклинания ----------

    /// <summary>Заклинание добавлено в список известных.</summary>
    public record SpellAdded(Guid CharacterId, string SpellId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Заклинание удалено из списка известных.</summary>
    public record SpellRemoved(Guid CharacterId, string SpellId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Установлены максимальные ячейки заклинаний (уровень -> количество).</summary>
    public record SpellSlotsSet(Guid CharacterId, Dictionary<int, int> MaxSlots) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Использована ячейка заклинания указанного уровня.</summary>
    public record SpellSlotUsed(Guid CharacterId, int SlotLevel) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Восстановлены ячейки заклинаний указанного уровня.</summary>
    public record SpellSlotsRestored(Guid CharacterId, int SlotLevel, int RestoredCount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Кости хитов ----------

    /// <summary>Установлены кости хитов (тип -> количество).</summary>
    public record HitDiceSet(Guid CharacterId, Dictionary<int, int> Dice) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Потрачена одна кость хита, восстановлено указанное количество хитов.</summary>
    public record HitDieSpent(Guid CharacterId, int HitDieType, int HealedAmount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Восстановлены кости хитов (тип -> сколько восстановлено).</summary>
    public record HitDiceRecovered(Guid CharacterId, Dictionary<int, int> Recovered) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Состояния ----------

    /// <summary>Наложено состояние (например, "отравлен", "оглушён").</summary>
    public record ConditionApplied(Guid CharacterId, string Condition) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Состояние снято.</summary>
    public record ConditionRemoved(Guid CharacterId, string Condition) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Все активные состояния сняты.</summary>
    public record AllConditionsCleared(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Боевые параметры ----------

    /// <summary>Обновлён класс брони.</summary>
    public record ArmorClassUpdated(Guid CharacterId, int NewArmorClass) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Обновлена скорость передвижения.</summary>
    public record SpeedUpdated(Guid CharacterId, int NewSpeed) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Защиты ----------

    /// <summary>Добавлено сопротивление урону.</summary>
    public record ResistanceAdded(Guid CharacterId, string DamageType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Убрано сопротивление урону.</summary>
    public record ResistanceRemoved(Guid CharacterId, string DamageType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Добавлена уязвимость к урону.</summary>
    public record VulnerabilityAdded(Guid CharacterId, string DamageType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Убрана уязвимость к урону.</summary>
    public record VulnerabilityRemoved(Guid CharacterId, string DamageType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Добавлен иммунитет к урону.</summary>
    public record ImmunityAdded(Guid CharacterId, string DamageType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Убран иммунитет к урону.</summary>
    public record ImmunityRemoved(Guid CharacterId, string DamageType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Экипировка и инвентарь ----------

    /// <summary>Предмет экипирован в указанный слот.</summary>
    public record ItemEquipped(
        Guid CharacterId,
        string ItemId,
        string Slot,
        string ItemName,
        int ArmorBonus = 0,
        int DamageBonus = 0) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Предмет снят.</summary>
    public record ItemUnequipped(Guid CharacterId, string ItemId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Предмет добавлен в инвентарь.</summary>
    public record InventoryItemAdded(Guid CharacterId, string ItemId, string ItemName, int Quantity = 1) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Предмет удалён из инвентаря.</summary>
    public record InventoryItemRemoved(Guid CharacterId, string ItemId, int Quantity = 1) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Спасброски от смерти и жизненные состояния ----------

    /// <summary>Успешный спасбросок от смерти.</summary>
    public record DeathSavingThrowSuccess(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Проваленный спасбросок от смерти.</summary>
    public record DeathSavingThrowFailure(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж стабилизирован.</summary>
    public record CharacterStabilized(Guid CharacterId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж воскрешён с указанным количеством хитов.</summary>
    public record CharacterRevived(Guid CharacterId, int NewHitPoints) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Концентрация ----------

    /// <summary>Начата концентрация на заклинании.</summary>
    public record ConcentrationStarted(Guid CharacterId, string SpellId) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Концентрация прекращена (указана причина).</summary>
    public record ConcentrationEnded(Guid CharacterId, string SpellId, string Reason) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    // ---------- Действия с золотом ----------

    /// <summary>Персонажу добавлено золото.</summary>
    public record GoldAdded(Guid CharacterId, int Amount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонаж потратил золото.</summary>
    public record GoldSpent(Guid CharacterId, int Amount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Количество золота установлено принудительно.</summary>
    public record GoldSet(Guid CharacterId, int Amount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Совершена попытка спасброска (общая).</summary>
    public record SavingThrowAttempted(
        Guid CharacterId,
        string Ability,
        int DifficultyClass,
        int RollResult,
        bool Success) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Максимальные хиты персонажа увеличены (например, при повышении уровня).</summary>
    public record MaxHitPointsIncreased(Guid CharacterId, int Amount) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }

    /// <summary>Персонажу добавлена одна кость хитов указанного типа.</summary>
    public record HitDieAdded(Guid CharacterId, int HitDieType) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }
}