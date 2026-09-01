#nullable enable
using System;

namespace dnd_game.domain.value_objects
{
    /// <summary>
    /// Значения шести характеристик персонажа.
    /// </summary>
    public record AbilityScores(
        int Strength,
        int Dexterity,
        int Constitution,
        int Intelligence,
        int Wisdom,
        int Charisma)
    {
        public static readonly AbilityScores Default = new(10, 10, 10, 10, 10, 10);

        /// <summary>
        /// Возвращает модификатор характеристики (бонус/штраф) для указанной способности.
        /// </summary>
        /// <param name="ability">Идентификатор характеристики.</param>
        /// <returns>Модификатор (может быть отрицательным).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если характеристика неизвестна.</exception>
        public int GetModifier(AbilityId ability) => ability.Value switch
        {
            nameof(Strength) => ModifierCalculator.Calculate(Strength),
            nameof(Dexterity) => ModifierCalculator.Calculate(Dexterity),
            nameof(Constitution) => ModifierCalculator.Calculate(Constitution),
            nameof(Intelligence) => ModifierCalculator.Calculate(Intelligence),
            nameof(Wisdom) => ModifierCalculator.Calculate(Wisdom),
            nameof(Charisma) => ModifierCalculator.Calculate(Charisma),
            _ => throw new ArgumentOutOfRangeException(nameof(ability), $"Неизвестная характеристика: {ability.Value}")
        };

        /// <summary>
        /// Устанавливает новое значение одной характеристики и возвращает обновлённый объект.
        /// </summary>
        /// <param name="ability">Идентификатор характеристики.</param>
        /// <param name="score">Новое значение (1–30).</param>
        /// <exception cref="ArgumentOutOfRangeException">Если значение вне диапазона или характеристика неизвестна.</exception>
        public AbilityScores With(AbilityId ability, int score)
        {
            if (score < 1 || score > 30)
                throw new ArgumentOutOfRangeException(nameof(score), "Значение характеристики должно быть от 1 до 30.");

            return ability.Value switch
            {
                nameof(Strength) => this with { Strength = score },
                nameof(Dexterity) => this with { Dexterity = score },
                nameof(Constitution) => this with { Constitution = score },
                nameof(Intelligence) => this with { Intelligence = score },
                nameof(Wisdom) => this with { Wisdom = score },
                nameof(Charisma) => this with { Charisma = score },
                _ => throw new ArgumentOutOfRangeException(nameof(ability), $"Неизвестная характеристика: {ability.Value}")
            };
        }
    }

    /// <summary>
    /// Модификаторы характеристик (уже вычисленные значения).
    /// </summary>
    public record AbilityModifiers(
        int Strength,
        int Dexterity,
        int Constitution,
        int Intelligence,
        int Wisdom,
        int Charisma)
    {
        public static AbilityModifiers FromScores(AbilityScores scores) => new(
            ModifierCalculator.Calculate(scores.Strength),
            ModifierCalculator.Calculate(scores.Dexterity),
            ModifierCalculator.Calculate(scores.Constitution),
            ModifierCalculator.Calculate(scores.Intelligence),
            ModifierCalculator.Calculate(scores.Wisdom),
            ModifierCalculator.Calculate(scores.Charisma)
        );

        /// <summary>
        /// Возвращает модификатор для конкретной характеристики.
        /// </summary>
        /// <param name="ability">Идентификатор характеристики.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если характеристика неизвестна.</exception>
        public int Get(AbilityId ability) => ability.Value switch
        {
            nameof(Strength) => Strength,
            nameof(Dexterity) => Dexterity,
            nameof(Constitution) => Constitution,
            nameof(Intelligence) => Intelligence,
            nameof(Wisdom) => Wisdom,
            nameof(Charisma) => Charisma,
            _ => throw new ArgumentOutOfRangeException(nameof(ability), $"Неизвестная характеристика: {ability.Value}")
        };
    }

    /// <summary>
    /// Бонус мастерства (Proficiency Bonus).
    /// </summary>
    public record ProficiencyBonus(int Value)
    {
        /// <summary>
        /// Возвращает бонус мастерства для указанного уровня персонажа (1–20).
        /// </summary>
        /// <param name="level">Уровень персонажа.</param>
        /// <returns>Бонус мастерства.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если уровень вне диапазона 1–20.</exception>
        public static ProficiencyBonus FromLevel(int level)
        {
            if (level < 1 || level > 20)
                throw new ArgumentOutOfRangeException(nameof(level), "Уровень персонажа должен быть от 1 до 20.");

            int bonus = level switch
            {
                <= 4 => 2,
                <= 8 => 3,
                <= 12 => 4,
                <= 16 => 5,
                _ => 6
            };
            return new ProficiencyBonus(bonus);
        }

        public override string ToString() => $"+{Value}";
    }

    /// <summary>
    /// Модификаторы для различных бросков: атаки, урона, спасбросков, навыков, КД.
    /// </summary>
    public record CombatModifiers(
        int AttackBonus = 0,
        int DamageBonus = 0,
        int ArmorClassBonus = 0,
        int SavingThrowBonus = 0,
        int SpellAttackBonus = 0,
        int SpellSaveDCBonus = 0)
    {
        public static readonly CombatModifiers Zero = new();
    }

    /// <summary>
    /// Составной модификатор проверки навыка: учитывает бонус мастерства, модификатор характеристики,
    /// дополнительные бонусы, экспертизу (двойной бонус мастерства) и помеху/преимущество.
    /// </summary>
    public record SkillCheckModifier(
        int AbilityModifier,
        int ProficiencyBonus,
        bool IsProficient,
        bool Expertise = false,
        int MiscBonus = 0)
    {
        /// <summary>
        /// Итоговый бонус проверки навыка.
        /// </summary>
        public int TotalBonus =>
            AbilityModifier +
            (IsProficient ? (Expertise ? 2 * ProficiencyBonus : ProficiencyBonus) : 0) +
            MiscBonus;
    }

    /// <summary>
    /// Модификаторы для спасброска.
    /// </summary>
    public record SavingThrowModifier(
        int AbilityModifier,
        int ProficiencyBonus,
        bool IsProficient,
        int MiscBonus = 0)
    {
        /// <summary>
        /// Итоговый бонус спасброска.
        /// </summary>
        public int TotalBonus =>
            AbilityModifier + (IsProficient ? ProficiencyBonus : 0) + MiscBonus;
    }

    /// <summary>
    /// Вспомогательный класс для вычисления модификаторов DnD 5e.
    /// </summary>
    public static class ModifierCalculator
    {
        /// <summary>
        /// Вычисляет модификатор характеристики: (значение - 10) / 2 с округлением вниз.
        /// </summary>
        public static int Calculate(int abilityScore) => (abilityScore - 10) / 2;

        /// <summary>
        /// Вычисляет пассивную проверку навыка: 10 + бонус + (преимущество +5, помеха -5).
        /// </summary>
        public static int PassiveSkill(int baseValue, bool hasAdvantage = false, bool hasDisadvantage = false)
        {
            int result = baseValue;
            if (hasAdvantage) result += 5;
            if (hasDisadvantage) result -= 5;
            return result;
        }

        /// <summary>
        /// Возвращает модификатор инициативы.
        /// </summary>
        public static int InitiativeModifier(int dexterityModifier, int miscBonus = 0) =>
            dexterityModifier + miscBonus;

        /// <summary>
        /// Класс брони для лёгкого доспеха: база + модификатор Ловкости (с возможным ограничением).
        /// </summary>
        public static int LightArmorAC(int baseArmor, int dexterityModifier, int? maxDexBonus = null) =>
            baseArmor + (maxDexBonus.HasValue ? Math.Min(dexterityModifier, maxDexBonus.Value) : dexterityModifier);

        /// <summary>
        /// Класс брони для среднего доспеха: база + модификатор Ловкости (максимум +2).
        /// </summary>
        public static int MediumArmorAC(int baseArmor, int dexterityModifier) =>
            baseArmor + Math.Min(dexterityModifier, 2);

        /// <summary>
        /// Класс брони для тяжёлого доспеха: база (модификатор Ловкости не применяется).
        /// </summary>
        public static int HeavyArmorAC(int baseArmor) => baseArmor;

        /// <summary>
        /// Класс брони без доспеха (например, защита монаха или варвара).
        /// </summary>
        public static int UnarmoredAC(int dexterityModifier, int? additionalModifier = null) =>
            10 + dexterityModifier + (additionalModifier ?? 0);
    }
}