using System;
using System.Collections.Generic;

namespace dnd_game.application.projections
{
    /// <summary>
    /// DTO персонажа для чтения (проекция). Содержит все основные характеристики,
    /// текущее состояние, инвентарь, экипировку, заклинания и т.д.
    /// </summary>
    public record CharacterDto
    {
        /// <summary>Идентификатор персонажа.</summary>
        public Guid Id { get; init; }

        /// <summary>Имя персонажа.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Максимальное количество хитов.</summary>
        public int MaxHitPoints { get; init; }

        /// <summary>Текущее количество хитов.</summary>
        public int HitPoints { get; init; }

        /// <summary>Временные хиты.</summary>
        public int TemporaryHitPoints { get; init; }

        /// <summary>Класс брони (AC).</summary>
        public int ArmorClass { get; init; } = 10;

        /// <summary>Скорость передвижения в футах.</summary>
        public int Speed { get; init; } = 30;

        /// <summary>Накопленный опыт.</summary>
        public int ExperiencePoints { get; init; }

        /// <summary>Текущий уровень персонажа.</summary>
        public int Level { get; init; } = 1;

        /// <summary>Раса персонажа.</summary>
        public string Race { get; init; } = string.Empty;

        /// <summary>Класс персонажа.</summary>
        public string Class { get; init; } = string.Empty;

        /// <summary>Предыстория (background).</summary>
        public string Background { get; init; } = string.Empty;

        /// <summary>Бонус мастерства.</summary>
        public int ProficiencyBonus { get; init; } = 2;

        /// <summary>Значения характеристик (Сила, Ловкость и т.д.).</summary>
        public Dictionary<string, int> AbilityScores { get; init; } = [];

        /// <summary>Владение навыками (название навыка → есть ли владение).</summary>
        public Dictionary<string, bool> SkillProficiencies { get; init; } = [];

        /// <summary>Владение спасбросками (характеристика → есть ли владение).</summary>
        public Dictionary<string, bool> SavingThrowProficiencies { get; init; } = [];

        /// <summary>Список известных заклинаний.</summary>
        public List<string> KnownSpells { get; init; } = [];
        public Dictionary<string, int> ClassFeatureMaxUses { get; init; } = [];
        public Dictionary<string, int> ClassFeatureUsedCount { get; init; } = [];

        /// <summary>Максимальное количество ячеек заклинаний по уровням (уровень → количество).</summary>
        public Dictionary<int, int> MaxSpellSlots { get; init; } = [];

        /// <summary>Использованные ячейки заклинаний по уровням.</summary>
        public Dictionary<int, int> UsedSpellSlots { get; init; } = [];

        /// <summary>Оставшиеся кости хитов по типам (количество граней → количество костей).</summary>
        public Dictionary<int, int> HitDiceRemaining { get; init; } = [];

        /// <summary>Максимальное количество костей хитов по типам.</summary>
        public Dictionary<int, int> MaxHitDice { get; init; } = [];

        /// <summary>Количество успешных спасбросков от смерти.</summary>
        public int DeathSaveSuccesses { get; init; }

        /// <summary>Количество проваленных спасбросков от смерти.</summary>
        public int DeathSaveFailures { get; init; }

        /// <summary>Стабилизирован ли персонаж.</summary>
        public bool IsStable { get; init; }

        /// <summary>Мёртв ли персонаж.</summary>
        public bool IsDead { get; init; }

        /// <summary>Активные состояния (например, "отравлен", "оглушён").</summary>
        public List<string> Conditions { get; init; } = [];

        /// <summary>Сопротивления урону.</summary>
        public List<string> Resistances { get; init; } = [];

        /// <summary>Уязвимости к урону.</summary>
        public List<string> Vulnerabilities { get; init; } = [];

        /// <summary>Иммунитеты к урону.</summary>
        public List<string> Immunities { get; init; } = [];

        /// <summary>Экипированные предметы.</summary>
        public List<EquippedItemDto> Equipment { get; init; } = [];

        /// <summary>Предметы в инвентаре.</summary>
        public List<InventoryItemDto> Inventory { get; init; } = [];

        /// <summary>Полученные черты (feats).</summary>
        public List<string> Feats { get; init; } = [];

        /// <summary>Поддерживает ли концентрацию на заклинании.</summary>
        public bool Concentrating { get; init; }

        /// <summary>Количество золота.</summary>
        public int Gold { get; init; }

        /// <summary>Координата X на карте.</summary>
        public int PositionX { get; init; }

        /// <summary>Координата Y на карте.</summary>
        public int PositionY { get; init; }

        /// <summary>Находится ли персонаж без сознания (0 хитов, не мёртв и не стабилизирован).</summary>
        public bool IsUnconscious => HitPoints <= 0 && !IsDead && !IsStable;

        /// <summary>Находится ли персонаж при смерти (0 хитов, не мёртв, не стабилизирован, спасброски ещё не решены).</summary>
        public bool IsDying => HitPoints <= 0 && !IsDead && !IsStable && DeathSaveSuccesses < 3 && DeathSaveFailures < 3;

        /// <summary>Строковое представление класса брони для отображения.</summary>
        public string ArmorClassDisplay => ArmorClass.ToString();

        public bool IsDashing { get; init; }

        /// <summary>Персонаж использовал Отход (Disengage) в текущем ходу.</summary>
        public bool IsDisengaged { get; init; }

        /// <summary>Персонаж скрывается (Hide).</summary>
        public bool IsHiding { get; init; }
        public bool IsInCombat { get; init; }

    }

    /// <summary>
    /// DTO для текущего состояния хитов персонажа.
    /// </summary>
    public record CharacterHitPointsDto(int Current, int Max, int Temporary);

    /// <summary>
    /// DTO для боевых характеристик персонажа.
    /// </summary>
    public record CharacterCombatStatsDto(
        int ArmorClass,
        int Speed,
        Dictionary<int, int> HitDiceRemaining,
        int DeathSaveSuccesses,
        int DeathSaveFailures,
        bool IsStable);

    /// <summary>
    /// DTO для информации о заклинаниях персонажа.
    /// </summary>
    public record CharacterSpellsDto(
        List<string> KnownSpells,
        Dictionary<int, int> MaxSpellSlots,
        Dictionary<int, int> UsedSpellSlots);

    /// <summary>
    /// DTO для статуса смерти персонажа.
    /// </summary>
    public record CharacterDeathStatusDto(string Status, int DeathSaveSuccesses, int DeathSaveFailures);

    /// <summary>
    /// DTO для защит персонажа (сопротивления, уязвимости, иммунитеты).
    /// </summary>
    public record CharacterDefensesDto(
        List<string> Resistances,
        List<string> Vulnerabilities,
        List<string> Immunities);

    /// <summary>
    /// Краткая сводка о персонаже для списков.
    /// </summary>
    public record CharacterSummaryDto(
        Guid Id,
        string Name,
        int Level,
        string Class,
        string Race,
        int HitPoints,
        int MaxHitPoints,
        bool IsAlive,
        int ArmorClass);

    /// <summary>
    /// Элемент инвентаря.
    /// </summary>
    public record InventoryItemDto(string ItemId, string Name, int Quantity);

    /// <summary>
    /// Экипированный предмет.
    /// </summary>
    public record EquippedItemDto(
        string ItemId,
        string Slot,
        string Name,
        int ArmorBonus,
        int DamageBonus);
}