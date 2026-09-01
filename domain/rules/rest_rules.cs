#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.rules
{
    /// <summary>
    /// Правила отдыха в DnD 5e: восстановление хитов, костей хитов, ячеек заклинаний,
    /// ограничения по времени, прерывание отдыха и влияние доспехов.
    /// </summary>
    public static class RestRules
    {
        // --------------------------------------------------------------------------------------------
        // Константы типов отдыха
        // --------------------------------------------------------------------------------------------

        /// <summary>Короткий отдых (1 час).</summary>
        public const string ShortRest = "Short";

        /// <summary>Длинный отдых (8 часов).</summary>
        public const string LongRest = "Long";

        // --------------------------------------------------------------------------------------------
        // Восстановление хитов
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Хиты, восстанавливаемые при использовании одной кости хитов во время короткого отдыха.
        /// Результат не может быть меньше 0.
        /// </summary>
        /// <param name="roll">Результат броска кости хитов.</param>
        /// <param name="constitutionModifier">Модификатор телосложения.</param>
        /// <returns>Количество восстанавливаемых хитов (≥ 0).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если бросок меньше 1 или модификатор меньше -5.</exception>
        public static int HitPointsPerHitDie(int roll, int constitutionModifier)
        {
            if (roll < 1)
                throw new ArgumentOutOfRangeException(nameof(roll), "Результат броска кости хитов должен быть не меньше 1.");
            // Модификатор телосложения может быть отрицательным, но в рамках правил от -5 до +10.
            // Проверяем только на экстремально малые значения.
            if (constitutionModifier < -5)
                throw new ArgumentOutOfRangeException(nameof(constitutionModifier), "Модификатор телосложения не может быть меньше -5.");

            return Math.Max(0, roll + constitutionModifier);
        }

        /// <summary>
        /// Восстановление хитов после длинного отдыха — полное восстановление.
        /// </summary>
        /// <param name="maxHitPoints">Максимальное количество хитов персонажа.</param>
        /// <returns>Максимальное количество хитов (полное восстановление).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если максимальные хиты меньше 1.</exception>
        public static int HitPointsAfterLongRest(int maxHitPoints)
        {
            if (maxHitPoints < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHitPoints), "Максимальные хиты должны быть положительными.");

            return maxHitPoints;
        }

        // --------------------------------------------------------------------------------------------
        // Кости хитов
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Максимальное количество костей хитов персонажа (обычно равно уровню).
        /// </summary>
        /// <param name="level">Уровень персонажа (1–20).</param>
        /// <returns>Количество костей хитов.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если уровень вне диапазона 1–20.</exception>
        public static int TotalHitDice(int level)
        {
            if (level < 1 || level > 20)
                throw new ArgumentOutOfRangeException(nameof(level), "Уровень персонажа должен быть от 1 до 20.");

            return level;
        }

        /// <summary>
        /// Количество костей хитов, восстанавливаемых после длинного отдыха.
        /// Персонаж восстанавливает половину от максимума (минимум 1).
        /// </summary>
        /// <param name="maxHitDice">Максимальное количество костей хитов.</param>
        /// <returns>Количество восстанавливаемых костей хитов.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="maxHitDice"/> меньше 1.</exception>
        public static int HitDiceRecoveredOnLongRest(int maxHitDice)
        {
            if (maxHitDice < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHitDice), "Максимальное количество костей хитов должно быть положительным.");

            return Math.Max(1, maxHitDice / 2);
        }

        /// <summary>
        /// Можно ли тратить кости хитов во время короткого отдыха.
        /// </summary>
        /// <param name="remainingHitDice">Оставшиеся кости хитов.</param>
        /// <param name="currentHitPoints">Текущие хиты.</param>
        /// <param name="maxHitPoints">Максимальные хиты.</param>
        /// <returns><c>true</c>, если есть кости и текущие хиты меньше максимальных.</returns>
        public static bool CanSpendHitDice(int remainingHitDice, int currentHitPoints, int maxHitPoints)
        {
            if (remainingHitDice < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingHitDice), "Количество оставшихся костей хитов не может быть отрицательным.");
            if (currentHitPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(currentHitPoints), "Текущие хиты не могут быть отрицательными.");
            if (maxHitPoints < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHitPoints), "Максимальные хиты должны быть положительными.");
            if (currentHitPoints > maxHitPoints)
                throw new ArgumentException("Текущие хиты не могут превышать максимальные.", nameof(currentHitPoints));

            return remainingHitDice > 0 && currentHitPoints < maxHitPoints;
        }

        // --------------------------------------------------------------------------------------------
        // Ячейки заклинаний
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Длинный отдых полностью восстанавливает все ячейки заклинаний.
        /// </summary>
        public static bool LongRestRestoresAllSpellSlots => true;

        /// <summary>
        /// Короткий отдых восстанавливает ячейки pact magic (например, у колдуна).
        /// </summary>
        public static bool ShortRestRestoresPactMagicSlots => true;

        // --------------------------------------------------------------------------------------------
        // Ограничения по времени
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Проверяет, можно ли получить пользу от длинного отдыха.
        /// Нельзя получать пользу более одного раза за 24 часа.
        /// </summary>
        /// <param name="lastLongRestEndUtc">Время окончания последнего длинного отдыха (UTC).</param>
        /// <param name="currentTimeUtc">Текущее время (UTC).</param>
        /// <returns><c>true</c>, если прошло не менее 24 часов.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="lastLongRestEndUtc"/> больше текущего времени.</exception>
        public static bool CanBenefitFromLongRest(DateTime lastLongRestEndUtc, DateTime currentTimeUtc)
        {
            if (lastLongRestEndUtc > currentTimeUtc)
                throw new ArgumentOutOfRangeException(nameof(lastLongRestEndUtc), "Время окончания отдыха не может быть в будущем.");

            return (currentTimeUtc - lastLongRestEndUtc).TotalHours >= 24;
        }

        /// <summary>
        /// Минимальная продолжительность короткого отдыха в часах.
        /// </summary>
        public static int ShortRestMinimumDurationHours => 1;

        /// <summary>
        /// Минимальная продолжительность длинного отдыха в часах (включая не менее 6 часов сна).
        /// </summary>
        public static int LongRestMinimumDurationHours => 8;

        /// <summary>
        /// Длительность транса эльфов (может заменять сон, но не весь отдых).
        /// </summary>
        public static int ElfTranceSleepHours => 4;

        // --------------------------------------------------------------------------------------------
        // Прерывание отдыха
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Короткий отдых прерывается боем или другой напряжённой активностью; преимущества теряются.
        /// </summary>
        public static bool ShortRestInterruptedByCombat => true;

        /// <summary>
        /// Длинный отдых прерывается, если напряжённая активность (бой, колдовство, ходьба) длится более 1 часа.
        /// При меньшей длительности отдых можно продолжить.
        /// </summary>
        /// <param name="strenuousActivityHours">Количество часов напряжённой активности.</param>
        /// <returns><c>true</c>, если отдых прерван.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если количество часов отрицательное.</exception>
        public static bool LongRestInterruptedByStrenuousActivity(int strenuousActivityHours)
        {
            if (strenuousActivityHours < 0)
                throw new ArgumentOutOfRangeException(nameof(strenuousActivityHours), "Количество часов не может быть отрицательным.");

            return strenuousActivityHours > 1;
        }

        // --------------------------------------------------------------------------------------------
        // Сон и усталость
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Длинный отдых снимает 1 уровень истощения, если персонаж поел и выпил.
        /// </summary>
        /// <param name="hasEatenAndDrunk">Поел ли персонаж и попил ли достаточно.</param>
        /// <returns>1, если условия выполнены; иначе 0.</returns>
        public static int ExhaustionReductionOnLongRest(bool hasEatenAndDrunk)
            => hasEatenAndDrunk ? 1 : 0;

        // --------------------------------------------------------------------------------------------
        // Сон в доспехах (опциональное правило Xanathar's Guide)
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Сон в среднем или тяжёлом доспехе снижает восстановление костей хитов и не снимает усталость.
        /// </summary>
        public static bool SleepingInMediumOrHeavyArmorReducesRecovery => true;

        /// <summary>
        /// Количество костей хитов, восстанавливаемых после длинного отдыха, если персонаж спал в доспехе.
        /// Восстанавливается только четверть от максимума (минимум 1).
        /// </summary>
        /// <param name="maxHitDice">Максимальное количество костей хитов.</param>
        /// <returns>Количество восстанавливаемых костей.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="maxHitDice"/> меньше 1.</exception>
        public static int HitDiceRecoveredOnLongRestWhileArmored(int maxHitDice)
        {
            if (maxHitDice < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHitDice), "Максимальное количество костей хитов должно быть положительным.");

            return Math.Max(1, maxHitDice / 4);
        }

        // --------------------------------------------------------------------------------------------
        // Проверка возможности начать отдых
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Нельзя начинать отдых, если персонаж в бою, умирает или без сознания.
        /// </summary>
        /// <param name="isInCombat">Находится ли персонаж в бою.</param>
        /// <param name="isDying">Находится ли персонаж при смерти (0 хитов и не стабилизирован).</param>
        /// <param name="isUnconscious">Без сознания ли персонаж (по другой причине).</param>
        /// <returns><c>true</c>, если отдых возможен.</returns>
        public static bool CanStartRest(bool isInCombat, bool isDying, bool isUnconscious)
            => !isInCombat && !isDying && !isUnconscious;

        // --------------------------------------------------------------------------------------------
        // Перезарядка умений
        // --------------------------------------------------------------------------------------------

        // Словари умений, перезаряжающихся на коротком отдыхе.
        // В реальном проекте заполняются данными из базы или справочника.
        private static readonly Dictionary<string, bool> ShortRestFeatures = new(StringComparer.OrdinalIgnoreCase)
        {
            // Примеры:
            // ["ActionSurge"] = true,
            // ["SecondWind"] = true,
            // ["ChannelDivinity"] = true (для некоторых доменов)
        };

        /// <summary>
        /// Проверяет, перезаряжается ли умение на коротком отдыхе.
        /// </summary>
        /// <param name="featureId">Идентификатор умения.</param>
        /// <returns><c>true</c>, если умение перезаряжается на коротком отдыхе.</returns>
        public static bool RechargesOnShortRest(string featureId)
        {
            if (string.IsNullOrWhiteSpace(featureId))
                return false;

            return ShortRestFeatures.TryGetValue(featureId, out bool recharges) && recharges;
        }

        /// <summary>
        /// Проверяет, перезаряжается ли умение на длинном отдыхе.
        /// По умолчанию умение перезаряжается на длинном отдыхе, если оно явно не отмечено
        /// как перезаряжаемое на коротком.
        /// </summary>
        /// <param name="featureId">Идентификатор умения.</param>
        /// <returns><c>true</c>, если умение перезаряжается на длинном отдыхе.</returns>
        public static bool RechargesOnLongRest(string featureId)
        {
            if (string.IsNullOrWhiteSpace(featureId))
                return false; // неизвестное умение не перезаряжается

            // Если умение есть в списке короткого отдыха, то оно перезаряжается и на длинном? 
            // Обычно умения короткого отдыха также перезаряжаются на длинном, 
            // но чтобы избежать двойного учёта, считаем, что если умение короткого отдыха,
            // то оно перезаряжается на коротком, и этот метод вернёт false,
            // т.к. оно уже покрыто коротким отдыхом.
            // Для простоты возвращаем true для всех непустых идентификаторов,
            // не входящих в ShortRestFeatures.
            return !ShortRestFeatures.ContainsKey(featureId);
        }
    }
}