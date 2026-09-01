#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace dnd_game.domain.rules
{
    /// <summary>
    /// Набор правил для боевых механик DnD 5e: инициатива, атаки, урон, преимущество/помеха,
    /// проверки концентрации и спасбросков, дальность оружия.
    /// Все методы являются чистыми функциями и не имеют побочных эффектов.
    /// </summary>
    public static class CombatRules
    {
        // --------------------------------------------------------------------------------
        // 1. Инициатива
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Вычисляет значение инициативы: d20 + модификатор ловкости + дополнительные бонусы.
        /// </summary>
        /// <param name="d20Roll">Результат броска d20.</param>
        /// <param name="dexterityModifier">Модификатор ловкости.</param>
        /// <param name="miscBonus">Прочие бонусы (по умолчанию 0).</param>
        /// <returns>Итоговое значение инициативы.</returns>
        public static int CalculateInitiative(int d20Roll, int dexterityModifier, int miscBonus = 0)
        {
            return d20Roll + dexterityModifier + miscBonus;
        }

        // --------------------------------------------------------------------------------
        // 2. Броски атаки
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Вычисляет результат броска атаки: d20 + бонус мастерства + модификатор характеристики + прочие бонусы.
        /// </summary>
        /// <param name="d20Roll">Результат броска d20.</param>
        /// <param name="proficiencyBonus">Бонус мастерства.</param>
        /// <param name="abilityModifier">Модификатор атакующей характеристики.</param>
        /// <param name="miscBonus">Прочие бонусы (по умолчанию 0).</param>
        /// <returns>Итоговый результат броска атаки.</returns>
        public static int CalculateAttackRoll(int d20Roll, int proficiencyBonus, int abilityModifier, int miscBonus = 0)
        {
            return d20Roll + proficiencyBonus + abilityModifier + miscBonus;
        }

        /// <summary>
        /// Проверяет, попала ли атака: результат броска не меньше класса брони цели.
        /// </summary>
        /// <param name="attackRoll">Результат броска атаки.</param>
        /// <param name="targetArmorClass">Класс брони (КД) цели.</param>
        /// <returns><c>true</c>, если атака попала; иначе <c>false</c>.</returns>
        public static bool IsHit(int attackRoll, int targetArmorClass)
        {
            return attackRoll >= targetArmorClass;
        }

        /// <summary>
        /// Проверяет, является ли бросок критическим успехом (натуральное 20).
        /// </summary>
        public static bool IsCriticalHit(int d20Roll) => d20Roll == 20;

        /// <summary>
        /// Проверяет, является ли бросок критическим провалом (натуральное 1).
        /// </summary>
        public static bool IsCriticalMiss(int d20Roll) => d20Roll == 1;

        // --------------------------------------------------------------------------------
        // 3. Урон
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Вычисляет итоговый урон с учётом модификатора (обычно модификатор характеристики для атаки оружием).
        /// </summary>
        /// <param name="baseDamage">Базовый урон (например, сумма костей).</param>
        /// <param name="modifier">Модификатор урона.</param>
        /// <returns>Итоговый урон (не может быть отрицательным).</returns>
        public static int CalculateDamage(int baseDamage, int modifier)
        {
            return Math.Max(0, baseDamage + modifier);
        }

        /// <summary>
        /// Применяет сопротивления, уязвимости и иммунитеты к урону согласно правилам DnD 5e.
        /// Порядок: иммунитет → уязвимость → сопротивление.
        /// </summary>
        /// <param name="incomingDamage">Входящий урон (неотрицательный).</param>
        /// <param name="damageType">Тип урона (например, "огонь", "колющий").</param>
        /// <param name="resistances">Список типов урона, к которым есть сопротивление.</param>
        /// <param name="vulnerabilities">Список типов урона, к которым есть уязвимость.</param>
        /// <param name="immunities">Список типов урона, к которым есть иммунитет.</param>
        /// <returns>Итоговый урон после всех модификаций (всегда ≥ 0).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если входящий урон отрицательный.</exception>
        public static int ApplyDamageModifiers(
            int incomingDamage,
            string damageType,
            IEnumerable<string>? resistances,
            IEnumerable<string>? vulnerabilities,
            IEnumerable<string>? immunities)
        {
            if (incomingDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(incomingDamage), "Урон не может быть отрицательным.");

            // Защита от null-коллекций
            resistances ??= [];
            vulnerabilities ??= [];
            immunities ??= [];

            // Проверка иммунитета
            if (immunities.Contains(damageType, StringComparer.OrdinalIgnoreCase))
                return 0;

            int finalDamage = incomingDamage;

            // Уязвимость удваивает урон
            if (vulnerabilities.Contains(damageType, StringComparer.OrdinalIgnoreCase))
                finalDamage *= 2;

            // Сопротивление уменьшает вдвое (округление вниз)
            if (resistances.Contains(damageType, StringComparer.OrdinalIgnoreCase))
                finalDamage /= 2;

            return Math.Max(0, finalDamage);
        }

        // --------------------------------------------------------------------------------
        // 4. Преимущество и помеха (Advantage / Disadvantage)
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Выполняет бросок с преимуществом: выбирается наибольший из двух d20.
        /// </summary>
        public static int RollWithAdvantage(int roll1, int roll2) => Math.Max(roll1, roll2);

        /// <summary>
        /// Выполняет бросок с помехой: выбирается наименьший из двух d20.
        /// </summary>
        public static int RollWithDisadvantage(int roll1, int roll2) => Math.Min(roll1, roll2);

        // --------------------------------------------------------------------------------
        // 5. Дополнительные проверки (концентрация, спасброски)
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Определяет сложность проверки концентрации: DC = max(10, damage_taken / 2).
        /// </summary>
        /// <param name="damageTaken">Количество полученного урона.</param>
        /// <returns>Сложность проверки концентрации.</returns>
        public static int CalculateConcentrationDC(int damageTaken)
        {
            if (damageTaken < 0)
                throw new ArgumentOutOfRangeException(nameof(damageTaken), "Урон не может быть отрицательным.");
            return Math.Max(10, damageTaken / 2);
        }

        /// <summary>
        /// Проверяет, успешен ли спасбросок.
        /// </summary>
        /// <param name="d20Roll">Результат броска d20.</param>
        /// <param name="abilityModifier">Модификатор характеристики.</param>
        /// <param name="proficiencyBonus">Бонус мастерства.</param>
        /// <param name="isProficient">Есть ли владение спасброском.</param>
        /// <param name="difficultyClass">Сложность спасброска.</param>
        /// <returns><c>true</c>, если спасбросок успешен.</returns>
        public static bool IsSavingThrowSuccess(int d20Roll, int abilityModifier, int proficiencyBonus, bool isProficient, int difficultyClass)
        {
            int total = d20Roll + abilityModifier + (isProficient ? proficiencyBonus : 0);
            return total >= difficultyClass;
        }

        // --------------------------------------------------------------------------------
        // 6. Вспомогательные методы для урона
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Возвращает средний урон для заданного количества костей и их граней.
        /// </summary>
        /// <param name="numberOfDice">Количество костей.</param>
        /// <param name="diceSides">Количество граней у кости (например, 6 для d6).</param>
        /// <returns>Средний урон (целочисленный, округление вниз).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если количество костей или граней меньше 1.</exception>
        public static int AverageDamage(int numberOfDice, int diceSides)
        {
            if (numberOfDice <= 0)
                throw new ArgumentOutOfRangeException(nameof(numberOfDice), "Количество костей должно быть положительным.");
            if (diceSides <= 0)
                throw new ArgumentOutOfRangeException(nameof(diceSides), "Количество граней должно быть положительным.");

            return numberOfDice * (diceSides + 1) / 2;
        }

        /// <summary>
        /// Возвращает средний урон для выражения вида «NdX» (например, «2d6»).
        /// </summary>
        /// <param name="diceNotation">Строка в формате «NdX», где N — количество костей, X — граней.</param>
        /// <returns>Средний урон.</returns>
        /// <exception cref="ArgumentException">Если строка не соответствует формату.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если количество костей или граней меньше 1.</exception>
        public static int AverageDamage(string diceNotation)
        {
            if (string.IsNullOrWhiteSpace(diceNotation))
                throw new ArgumentException("Обозначение костей не может быть пустым.", nameof(diceNotation));

            var parts = diceNotation.ToLower().Split('d');
            if (parts.Length != 2)
                throw new ArgumentException("Неверный формат обозначения костей. Ожидается «NdX» (например, «2d6»).", nameof(diceNotation));

            if (!int.TryParse(parts[0], out int count) || !int.TryParse(parts[1], out int sides))
                throw new ArgumentException("Неверные числовые значения в обозначении костей.", nameof(diceNotation));

            return AverageDamage(count, sides);
        }

        // --------------------------------------------------------------------------------
        // 7. Проверка дистанции для атак
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Проверяет, находится ли цель в пределах обычной дальности оружия.
        /// </summary>
        /// <param name="distanceFeet">Расстояние до цели в футах.</param>
        /// <param name="weaponRangeFeet">Обычная дальность оружия в футах.</param>
        /// <returns><c>true</c>, если цель в пределах обычной дальности.</returns>
        public static bool IsInRange(int distanceFeet, int weaponRangeFeet)
        {
            if (distanceFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceFeet), "Дистанция должна быть неотрицательной.");

            if (weaponRangeFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(weaponRangeFeet), "Дальность оружия должна быть неотрицательной.");

            return distanceFeet <= weaponRangeFeet;
        }

        /// <summary>
        /// Проверяет, находится ли цель в пределах длинной дальности оружия (атака с помехой).
        /// </summary>
        /// <param name="distanceFeet">Расстояние до цели в футах.</param>
        /// <param name="longRangeFeet">Длинная дальность оружия в футах.</param>
        /// <returns><c>true</c>, если цель в пределах длинной дальности.</returns>
        public static bool IsInLongRange(int distanceFeet, int longRangeFeet)
        {
            if (distanceFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceFeet), "Дистанция должна быть неотрицательной.");

            if (longRangeFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(longRangeFeet), "Дальность оружия должна быть неотрицательной.");

            return distanceFeet <= longRangeFeet;
        }
    }
}