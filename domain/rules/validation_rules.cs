#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.rules
{
    /// <summary>
    /// Набор правил валидации входных данных, характерных для DnD 5e.
    /// Содержит проверки для характеристик, навыков, состояний, типов урона,
    /// идентификаторов, параметров отдыха, экипировки и других игровых величин.
    /// </summary>
    public static class ValidationRules
    {
        // --------------------------------------------------------------------------------------------
        // Имена персонажей
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Имя персонажа не должно быть пустым, состоять только из пробелов или превышать 50 символов.
        /// </summary>
        public static bool IsValidCharacterName(string name)
            => !string.IsNullOrWhiteSpace(name) && name.Length <= 50;

        // --------------------------------------------------------------------------------------------
        // Идентификаторы
        // --------------------------------------------------------------------------------------------

        public static bool IsValidGuid(Guid id) => id != Guid.Empty;

        // --------------------------------------------------------------------------------------------
        // Характеристики
        // --------------------------------------------------------------------------------------------

        public const int MinAbilityScore = 1;
        public const int MaxAbilityScore = 30;

        public static bool IsValidAbilityScore(int score)
            => score >= MinAbilityScore && score <= MaxAbilityScore;

        /// <summary>
        /// Проверяет, является ли название характеристики одной из шести стандартных (Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma).
        /// </summary>
        public static bool IsValidAbilityName(string ability)
            => ability is "Strength" or "Dexterity" or "Constitution"
                      or "Intelligence" or "Wisdom" or "Charisma";

        // --------------------------------------------------------------------------------------------
        // Уровень и опыт
        // --------------------------------------------------------------------------------------------

        public const int MinLevel = 1;
        public const int MaxLevel = 20;

        public static bool IsValidLevel(int level)
            => level >= MinLevel && level <= MaxLevel;

        public static bool IsValidExperience(int amount)
            => amount > 0;

        // --------------------------------------------------------------------------------------------
        // Хиты и урон
        // --------------------------------------------------------------------------------------------

        public static bool IsPositiveHitPoints(int hp) => hp >= 0;
        public static bool IsValidMaxHitPoints(int maxHp) => maxHp > 0;
        public static bool IsPositiveDamage(int amount) => amount > 0;
        public static bool IsPositiveHealing(int amount) => amount > 0;

        /// <summary>Временные хиты не могут быть отрицательными.</summary>
        public static bool IsValidTemporaryHitPoints(int amount) => amount >= 0;

        // --------------------------------------------------------------------------------------------
        // Типы урона (стандартные для D&D 5e)
        // --------------------------------------------------------------------------------------------

        private static readonly HashSet<string> ValidDamageTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Bludgeoning", "Piercing", "Slashing",
            "Fire", "Cold", "Lightning", "Thunder", "Acid", "Poison",
            "Radiant", "Necrotic", "Psychic", "Force"
        };

        public static bool IsValidDamageType(string damageType)
            => ValidDamageTypes.Contains(damageType);

        // --------------------------------------------------------------------------------------------
        // Состояния (Conditions) – стандартные для D&D 5e
        // --------------------------------------------------------------------------------------------

        private static readonly HashSet<string> ValidConditions = new(StringComparer.OrdinalIgnoreCase)
        {
            "Blinded", "Charmed", "Deafened", "Frightened",
            "Grappled", "Incapacitated", "Invisible", "Paralyzed",
            "Petrified", "Poisoned", "Prone", "Restrained",
            "Stunned", "Unconscious", "Exhaustion"
        };

        public static bool IsValidCondition(string condition)
            => ValidConditions.Contains(condition);

        // --------------------------------------------------------------------------------------------
        // Навыки (Skills)
        // --------------------------------------------------------------------------------------------

        private static readonly HashSet<string> ValidSkills = new(StringComparer.OrdinalIgnoreCase)
        {
            "Acrobatics", "Animal Handling", "Arcana", "Athletics",
            "Deception", "History", "Insight", "Intimidation",
            "Investigation", "Medicine", "Nature", "Perception",
            "Performance", "Persuasion", "Religion", "Sleight of Hand",
            "Stealth", "Survival"
        };

        public static bool IsValidSkill(string skill)
            => ValidSkills.Contains(skill);

        // --------------------------------------------------------------------------------------------
        // Заклинательные характеристики
        // --------------------------------------------------------------------------------------------

        public static bool IsSpellcastingAbility(string ability)
            => IsValidAbilityName(ability);

        // --------------------------------------------------------------------------------------------
        // Ячейки заклинаний
        // --------------------------------------------------------------------------------------------

        public static bool IsValidSpellSlotLevel(int level)
            => level >= 1 && level <= 9;

        public static bool IsValidSpellSlotCount(int count)
            => count >= 0 && count <= 4; // максимум 4 ячейки одного уровня для полных заклинателей

        // --------------------------------------------------------------------------------------------
        // Кости хитов
        // --------------------------------------------------------------------------------------------

        private static readonly HashSet<int> ValidHitDieTypes = [6, 8, 10, 12];

        public static bool IsValidHitDieType(int dieType)
            => ValidHitDieTypes.Contains(dieType);

        public static bool IsValidHitDiceCount(int count)
            => count >= 0 && count <= 20; // максимум 20 костей (уровень 20)

        // --------------------------------------------------------------------------------------------
        // Класс брони и скорость
        // --------------------------------------------------------------------------------------------

        public static bool IsValidArmorClass(int ac) => ac >= 0;
        public static bool IsValidSpeed(int speed) => speed >= 0;

        // --------------------------------------------------------------------------------------------
        // Бонус мастерства
        // --------------------------------------------------------------------------------------------

        public static bool IsValidProficiencyBonus(int bonus)
            => bonus >= 2 && bonus <= 6;

        // --------------------------------------------------------------------------------------------
        // Экипировка
        // --------------------------------------------------------------------------------------------

        private static readonly HashSet<string> ValidEquipmentSlots = new(StringComparer.OrdinalIgnoreCase)
        {
            "Head", "Neck", "Torso", "Back", "Arms", "Hands",
            "Waist", "Legs", "Feet", "MainHand", "OffHand", "Ring1", "Ring2"
        };

        public static bool IsValidEquipmentSlot(string slot)
            => ValidEquipmentSlots.Contains(slot);

        public static bool IsValidItemId(string itemId)
            => !string.IsNullOrWhiteSpace(itemId);

        // --------------------------------------------------------------------------------------------
        // Инвентарь
        // --------------------------------------------------------------------------------------------

        public static bool IsValidItemQuantity(int quantity)
            => quantity > 0;

        // --------------------------------------------------------------------------------------------
        // Золото и валюта
        // --------------------------------------------------------------------------------------------

        public static bool IsValidGoldAmount(int gold) => gold >= 0;

        // --------------------------------------------------------------------------------------------
        // Раса, класс, предыстория (проверка заполненности)
        // --------------------------------------------------------------------------------------------

        public static bool IsValidRace(string race)
            => !string.IsNullOrWhiteSpace(race);
        public static bool IsValidClass(string className)
            => !string.IsNullOrWhiteSpace(className);
        public static bool IsValidBackground(string background)
            => !string.IsNullOrWhiteSpace(background);

        // --------------------------------------------------------------------------------------------
        // Черты (Feats)
        // --------------------------------------------------------------------------------------------

        public static bool IsValidFeatName(string featName)
            => !string.IsNullOrWhiteSpace(featName);

        // --------------------------------------------------------------------------------------------
        // Заклинания
        // --------------------------------------------------------------------------------------------

        public static bool IsValidSpellId(string spellId)
            => !string.IsNullOrWhiteSpace(spellId);

        // --------------------------------------------------------------------------------------------
        // Концентрация
        // --------------------------------------------------------------------------------------------

        /// <summary>Нельзя концентрироваться на двух заклинаниях одновременно.</summary>
        public static bool CanStartConcentration(bool alreadyConcentrating)
            => !alreadyConcentrating;

        // --------------------------------------------------------------------------------------------
        // Проверки действий (Action Economy)
        // --------------------------------------------------------------------------------------------

        public static bool CanTakeStandardAction(bool hasAction, bool isIncapacitated)
            => hasAction && !isIncapacitated;

        public static bool CanTakeBonusAction(bool hasBonusAction, bool isIncapacitated)
            => hasBonusAction && !isIncapacitated;

        public static bool CanTakeReaction(bool hasReaction, bool isIncapacitated)
            => hasReaction && !isIncapacitated;

        // --------------------------------------------------------------------------------------------
        // Перемещение
        // --------------------------------------------------------------------------------------------

        public static bool CanMove(int remainingMovement, bool isGrappled, bool isRestrained)
            => remainingMovement > 0 && !isGrappled && !isRestrained;

        // --------------------------------------------------------------------------------------------
        // Отдых
        // --------------------------------------------------------------------------------------------

        public static bool IsValidRestType(string restType)
            => restType is "Short" or "Long";

        // --------------------------------------------------------------------------------------------
        // Строковые проверки общего назначения
        // --------------------------------------------------------------------------------------------

        public static bool IsNonEmptyString(string? value)
            => !string.IsNullOrWhiteSpace(value);

        public static bool IsWithinLengthLimit(string? value, int maxLength)
            => value != null && value.Length <= maxLength;

        // --------------------------------------------------------------------------------------------
        // Проверка диапазонов координат (если применимо)
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Проверка координат. По умолчанию всегда true, так как конкретная карта
        /// может накладывать свои ограничения. При необходимости переопределите логику.
        /// </summary>
        public static bool IsValidCoordinate() => true;
    }
}