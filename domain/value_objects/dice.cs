#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace dnd_game.domain.value_objects
{
    /// <summary>
    /// Представляет бросок набора одинаковых костей (например, 2d6+3, 4d6kh3).
    /// Поддерживает стандартную нотацию DnD: количество костей, грани, модификатор,
    /// удержание наибольших/наименьших результатов (keep highest/lowest)
    /// и однократный переброс низких значений.
    /// </summary>
    public partial record Dice
    {
        /// <summary>Количество бросаемых костей.</summary>
        public int Count { get; }

        /// <summary>Количество граней у одной кости (d4, d6, d8, d10, d12, d20, d100).</summary>
        public int Sides { get; }

        /// <summary>Модификатор, добавляемый к сумме выпавших значений.</summary>
        public int Modifier { get; }

        /// <summary>
        /// Если задано положительное число — оставить только указанное количество наибольших результатов.
        /// Если отрицательное — оставить соответствующее количество наименьших.
        /// Если <c>null</c> — учитываются все броски.
        /// </summary>
        public int? Keep { get; }

        /// <summary>
        /// Если задано, каждый куб, показавший результат меньше или равный этому значению,
        /// перебрасывается один раз, и принимается новый результат.
        /// Используется, например, для стиля боя «Великое оружие» (Great Weapon Fighting).
        /// Должно быть ≥ 1 и меньше Sides.
        /// </summary>
        public int? RerollOnOrLess { get; }

        public Dice(int count, int sides, int modifier = 0, int? keep = null, int? rerollOnOrLess = null)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Количество костей не может быть отрицательным.");
            if (sides < 2)
                throw new ArgumentOutOfRangeException(nameof(sides), "Кость должна иметь минимум 2 грани (d2 или больше).");
            if (keep.HasValue && keep.Value == 0)
                throw new ArgumentException("Значение keep не может быть нулём.", nameof(keep));
            if (keep.HasValue && Math.Abs(keep.Value) > count)
                throw new ArgumentException("Нельзя удержать больше костей, чем брошено.", nameof(keep));
            if (rerollOnOrLess.HasValue && (rerollOnOrLess.Value < 1 || rerollOnOrLess.Value >= sides))
                throw new ArgumentOutOfRangeException(nameof(rerollOnOrLess),
                    $"Порог переброса должен быть от 1 до {sides - 1}.");

            Count = count;
            Sides = sides;
            Modifier = modifier;
            Keep = keep;
            RerollOnOrLess = rerollOnOrLess;
        }

        // ---------- Фабрики для стандартных костей ----------
        public static Dice D4(int count = 1, int modifier = 0) => new(count, 4, modifier);
        public static Dice D6(int count = 1, int modifier = 0) => new(count, 6, modifier);
        public static Dice D8(int count = 1, int modifier = 0) => new(count, 8, modifier);
        public static Dice D10(int count = 1, int modifier = 0) => new(count, 10, modifier);
        public static Dice D12(int count = 1, int modifier = 0) => new(count, 12, modifier);
        public static Dice D20(int modifier = 0) => new(1, 20, modifier);
        public static Dice D100(int modifier = 0) => new(1, 100, modifier);

        /// <summary>Специальный бросок характеристики: 4d6, оставить 3 наибольших.</summary>
        public static Dice AbilityScore() => new(4, 6, keep: 3);

        // ---------- Выполнение броска ----------

        /// <summary>
        /// Выполняет бросок костей и возвращает результат.
        /// Для детерминированных сценариев передавайте конкретный экземпляр <see cref="Random"/> с заданным seed.
        /// </summary>
        /// <param name="random">Генератор случайных чисел (не может быть null).</param>
        /// <returns>Результат броска.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="random"/> равен null.</exception>
        public DiceRollResult Roll(Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            var rolls = new List<int>(Count);
            for (int i = 0; i < Count; i++)
            {
                rolls.Add(random.Next(1, Sides + 1));
            }

            // Применяем однократный переброс значений ≤ порога
            if (RerollOnOrLess.HasValue)
            {
                for (int i = 0; i < rolls.Count; i++)
                {
                    if (rolls[i] <= RerollOnOrLess.Value)
                    {
                        rolls[i] = random.Next(1, Sides + 1);
                    }
                }
            }

            // Применяем удержание наибольших/наименьших
            IEnumerable<int> kept = rolls;
            if (Keep.HasValue)
            {
                int keepCount = Math.Abs(Keep.Value);
                kept = Keep.Value > 0
                    ? rolls.OrderByDescending(x => x).Take(keepCount)
                    : rolls.OrderBy(x => x).Take(keepCount);
            }

            int total = kept.Sum() + Modifier;

            // Критические успех/провал только для d20 (в контексте атаки)
            bool isNatural20 = false;
            bool isNatural1 = false;
            if (Sides == 20 && Count == 1)
            {
                int singleRoll = rolls[0];
                isNatural20 = singleRoll == 20;
                isNatural1 = singleRoll == 1;
            }

            return new DiceRollResult(
                rolls.AsReadOnly(),
                kept.ToList().AsReadOnly(),
                total,
                Modifier,
                isNatural20,
                isNatural1);
        }

        // ---------- Среднее значение ----------
        /// <summary>
        /// Возвращает математическое ожидание суммы костей (без модификатора).
        /// Если <paramref name="applyKeep"/> равен <c>true</c> и задано удержание (<see cref="Keep"/>),
        /// точное среднее не рассчитывается, и метод выбрасывает <see cref="NotSupportedException"/>.
        /// Если <paramref name="applyKeep"/> равен <c>false</c>, удержание игнорируется,
        /// и возвращается среднее для всех брошенных костей.
        /// </summary>
        /// <param name="applyKeep">Учитывать ли правило удержания при расчёте среднего.</param>
        /// <exception cref="NotSupportedException">Если <paramref name="applyKeep"/> равен <c>true</c> и <see cref="Keep"/> задан.</exception>
        public double Average(bool applyKeep = true)
        {
            if (applyKeep && Keep.HasValue)
                throw new NotSupportedException("Точное среднее для keep (удержание костей) не реализовано.");

            double avgOneDie = (Sides + 1) / 2.0;

            if (RerollOnOrLess.HasValue)
            {
                int N = Sides;
                int R = RerollOnOrLess.Value;
                // Корректная формула для однократного переброса значений ≤ R:
                // E = (N(N+1)/2 - R(R+1)/2 + R * (N+1)/2) / N
                avgOneDie = (N * (N + 1) / 2.0 - R * (R + 1) / 2.0 + R * (N + 1) / 2.0) / N;
            }

            return Count * avgOneDie;
        }

        // ---------- Строковое представление ----------
        public override string ToString()
        {
            string notation = $"{Count}d{Sides}";
            if (Keep.HasValue)
            {
                notation += Keep > 0 ? $"kh{Math.Abs(Keep.Value)}" : $"kl{Math.Abs(Keep.Value)}";
            }
            if (RerollOnOrLess.HasValue)
            {
                notation += $"ro{RerollOnOrLess.Value}";
            }
            if (Modifier != 0)
            {
                notation += Modifier > 0 ? $"+{Modifier}" : $"{Modifier}";
            }
            return notation;
        }

        // ---------- Парсинг нотации ----------
        /// <summary>
        /// Разбирает строку в объект Dice. Поддерживает форматы:
        /// "2d6+3", "1d20", "4d6kh3", "4d6kl3", "2d6ro2+1", "8d6ro1kh6".
        /// </summary>
        /// <exception cref="ArgumentException">Если строка пустая или имеет неверный формат.</exception>
        public static Dice Parse(string notation)
        {
            if (string.IsNullOrWhiteSpace(notation))
                throw new ArgumentException("Нотация костей не может быть пустой.", nameof(notation));

            var match = DiceNotationRegex().Match(notation.Trim());
            if (!match.Success)
                throw new FormatException($"Неверный формат нотации костей: '{notation}'.");

            int count = int.Parse(match.Groups["count"].Value);
            int sides = int.Parse(match.Groups["sides"].Value);

            int modifier = 0;
            if (match.Groups["modifier"].Success)
            {
                string sign = match.Groups["sign"].Value;
                int mod = int.Parse(match.Groups["mod"].Value);
                modifier = sign == "-" ? -mod : mod;
            }

            int? keep = null;
            if (match.Groups["keep"].Success)
            {
                string keepDir = match.Groups["keep"].Value; // "kh" или "kl"
                int keepCount = int.Parse(match.Groups["keepCount"].Value);
                keep = keepDir == "kh" ? keepCount : -keepCount;
            }

            int? reroll = null;
            if (match.Groups["reroll"].Success)
            {
                reroll = int.Parse(match.Groups["rerollValue"].Value);
            }

            return new Dice(count, sides, modifier, keep, reroll);
        }

        [GeneratedRegex(
            @"^(?<count>\d+)d(?<sides>\d+)(?:(?<keep>kh|kl)(?<keepCount>\d+))?(?:(?<reroll>ro(?<rerollValue>\d+)))?(?:(?<sign>[+-])(?<mod>\d+))?$",
            RegexOptions.IgnoreCase)]
        private static partial Regex DiceNotationRegex();
    }

    /// <summary>
    /// Результат броска костей.
    /// </summary>
    public record DiceRollResult
    {
        /// <summary>Все брошенные значения (до применения keep и переброса).</summary>
        public IReadOnlyList<int> AllRolls { get; }

        /// <summary>Значения, учтённые в сумме (после keep).</summary>
        public IReadOnlyList<int> KeptRolls { get; }

        /// <summary>Итоговое значение (сумма учтённых + модификатор).</summary>
        public int Total { get; }

        /// <summary>Модификатор, добавленный к сумме.</summary>
        public int Modifier { get; }

        /// <summary>Истина, если это бросок d20 и выпало натуральное 20 (критический успех).</summary>
        public bool IsNatural20 { get; }

        /// <summary>Истина, если это бросок d20 и выпало натуральное 1 (критический провал).</summary>
        public bool IsNatural1 { get; }

        public DiceRollResult(
            IReadOnlyList<int> allRolls,
            IReadOnlyList<int> keptRolls,
            int total,
            int modifier,
            bool isNatural20,
            bool isNatural1)
        {
            AllRolls = allRolls;
            KeptRolls = keptRolls;
            Total = total;
            Modifier = modifier;
            IsNatural20 = isNatural20;
            IsNatural1 = isNatural1;
        }

        public override string ToString() =>
            $"[{string.Join(", ", KeptRolls)}] + {Modifier} = {Total}";
    }

    /// <summary>
    /// Вспомогательные методы для бросков d20 с преимуществом и помехой.
    /// </summary>
    public static class D20RollHelper
    {
        /// <summary>
        /// Бросок d20 с преимуществом (два броска, выбирается наибольший).
        /// </summary>
        /// <param name="modifier">Модификатор, добавляемый к результату.</param>
        /// <param name="random">Генератор случайных чисел.</param>
        public static AdvantageResult RollWithAdvantage(int modifier, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            int roll1 = random.Next(1, 21);
            int roll2 = random.Next(1, 21);
            int chosen = Math.Max(roll1, roll2);
            return new AdvantageResult(roll1, roll2, chosen, modifier, chosen + modifier, true, false);
        }

        /// <summary>
        /// Бросок d20 с помехой (два броска, выбирается наименьший).
        /// </summary>
        /// <param name="modifier">Модификатор, добавляемый к результату.</param>
        /// <param name="random">Генератор случайных чисел.</param>
        public static AdvantageResult RollWithDisadvantage(int modifier, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            int roll1 = random.Next(1, 21);
            int roll2 = random.Next(1, 21);
            int chosen = Math.Min(roll1, roll2);
            return new AdvantageResult(roll1, roll2, chosen, modifier, chosen + modifier, false, true);
        }

        public record AdvantageResult(
            int Roll1,
            int Roll2,
            int Chosen,
            int Modifier,
            int Total,
            bool IsAdvantage,
            bool IsDisadvantage)
        {
            public bool IsCriticalHit => Chosen == 20;
            public bool IsCriticalMiss => Chosen == 1;

            public override string ToString()
            {
                string type = IsAdvantage ? "преимущество" : "помеха";
                return $"{type}: [{Roll1}, {Roll2}] -> {Chosen} + {Modifier} = {Total}";
            }
        }
    }
}