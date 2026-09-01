#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.value_objects
{
    /// <summary>
    /// Представляет количество денег в мире DnD.
    /// Хранит общую сумму в медных монетах (мм) и предоставляет нормализованные
    /// количества монет каждого достоинства: платина (пм), золото (зм),
    /// электрум (эм), серебро (см), медь (мм).
    /// Курсы обмена (5e): 1 пм = 10 зм, 1 зм = 10 см, 1 эм = 5 см, 1 см = 10 мм.
    /// </summary>
    public record Currency : IComparable<Currency>
    {
        // ---------- Константы обмена ----------
        public const int CopperPerSilver = 10;       // 1 см = 10 мм
        public const int CopperPerElectrum = 50;     // 1 эм = 5 см = 50 мм
        public const int CopperPerGold = 100;        // 1 зм = 10 см = 100 мм
        public const int CopperPerPlatinum = 1000;   // 1 пм = 10 зм = 1000 мм

        /// <summary>Общее количество медных монет (базовая единица).</summary>
        public int TotalCopper { get; }

        // ---------- Нормализованные компоненты ----------
        public int Platinum => TotalCopper / CopperPerPlatinum;
        public int Gold => TotalCopper % CopperPerPlatinum / CopperPerGold;
        public int Electrum => TotalCopper % CopperPerGold / CopperPerElectrum;
        public int Silver => TotalCopper % CopperPerElectrum / CopperPerSilver;
        public int Copper => TotalCopper % CopperPerSilver;

        // ---------- Конструктор ----------

        /// <summary>
        /// Создаёт валюту из общего количества медных монет.
        /// </summary>
        /// <param name="totalCopper">Неотрицательное количество медных монет.</param>
        /// <exception cref="ArgumentException">Если сумма отрицательна.</exception>
        public Currency(int totalCopper)
        {
            if (totalCopper < 0)
                throw new ArgumentException("Сумма валюты не может быть отрицательной.", nameof(totalCopper));
            TotalCopper = totalCopper;
        }

        // ---------- Статические фабричные методы ----------

        /// <summary>Создаёт валюту только из медных монет.</summary>
        public static Currency FromCopper(int copper) => new(copper);

        /// <summary>Создаёт валюту из серебряных монет.</summary>
        public static Currency FromSilver(int silver)
        {
            CheckNonNegative(silver, nameof(silver));
            return new Currency(checked(silver * CopperPerSilver));
        }

        /// <summary>Создаёт валюту из электрумовых монет.</summary>
        public static Currency FromElectrum(int electrum)
        {
            CheckNonNegative(electrum, nameof(electrum));
            return new Currency(checked(electrum * CopperPerElectrum));
        }

        /// <summary>Создаёт валюту из золотых монет.</summary>
        public static Currency FromGold(int gold)
        {
            CheckNonNegative(gold, nameof(gold));
            return new Currency(checked(gold * CopperPerGold));
        }

        /// <summary>Создаёт валюту из платиновых монет.</summary>
        public static Currency FromPlatinum(int platinum)
        {
            CheckNonNegative(platinum, nameof(platinum));
            return new Currency(checked(platinum * CopperPerPlatinum));
        }

        /// <summary>
        /// Создаёт валюту, задавая точное количество монет каждого типа.
        /// Все параметры должны быть неотрицательными.
        /// </summary>
        public static Currency FromComponents(int platinum, int gold, int electrum, int silver, int copper)
        {
            CheckNonNegative(platinum, nameof(platinum));
            CheckNonNegative(gold, nameof(gold));
            CheckNonNegative(electrum, nameof(electrum));
            CheckNonNegative(silver, nameof(silver));
            CheckNonNegative(copper, nameof(copper));

            int total = checked(
                platinum * CopperPerPlatinum +
                gold * CopperPerGold +
                electrum * CopperPerElectrum +
                silver * CopperPerSilver +
                copper);
            return new Currency(total);
        }

        /// <summary>Пустая валюта (ноль).</summary>
        public static Currency Zero => new(0);

        // ---------- Операторы ----------

        public static Currency operator +(Currency a, Currency b) =>
            new(checked(a.TotalCopper + b.TotalCopper));

        public static Currency operator -(Currency a, Currency b)
        {
            int result = a.TotalCopper - b.TotalCopper;
            if (result < 0)
                throw new InvalidOperationException("Нельзя вычесть большую сумму из меньшей (результат был бы отрицательным).");
            return new Currency(result);
        }

        public static Currency operator *(Currency a, int multiplier)
        {
            if (multiplier < 0)
                throw new ArgumentException("Множитель не может быть отрицательным.", nameof(multiplier));
            return new Currency(checked(a.TotalCopper * multiplier));
        }

        public static bool operator >(Currency a, Currency b) => a.TotalCopper > b.TotalCopper;
        public static bool operator <(Currency a, Currency b) => a.TotalCopper < b.TotalCopper;
        public static bool operator >=(Currency a, Currency b) => a.TotalCopper >= b.TotalCopper;
        public static bool operator <=(Currency a, Currency b) => a.TotalCopper <= b.TotalCopper;

        // ---------- Методы проверки и изменения ----------

        /// <summary>Проверяет, достаточно ли средств для оплаты указанной стоимости.</summary>
        public bool CanAfford(Currency cost) => TotalCopper >= cost.TotalCopper;

        /// <summary>
        /// Вычитает указанную стоимость и возвращает оставшуюся сумму.
        /// Бросает исключение, если средств недостаточно.
        /// </summary>
        public Currency Subtract(Currency cost)
        {
            if (!CanAfford(cost))
                throw new InvalidOperationException("Недостаточно средств.");
            return new Currency(TotalCopper - cost.TotalCopper);
        }

        /// <summary>Добавляет другую валюту и возвращает новую сумму.</summary>
        public Currency Add(Currency other) => new(checked(TotalCopper + other.TotalCopper));

        // ---------- Сравнение ----------
        public int CompareTo(Currency? other) => TotalCopper.CompareTo(other?.TotalCopper ?? 0);

        // ---------- Вспомогательные методы ----------

        /// <summary>
        /// Возвращает нормализованное представление валюты в виде словаря (тип монеты -> количество).
        /// Ключи: "пм", "зм", "эм", "см", "мм".
        /// </summary>
        public Dictionary<string, int> Breakdown() => new()
        {
            ["пм"] = Platinum,
            ["зм"] = Gold,
            ["эм"] = Electrum,
            ["см"] = Silver,
            ["мм"] = Copper
        };

        /// <summary>
        /// Возвращает строку с перечислением ненулевых номиналов.
        /// Например: "1 зм, 3 см" или "5 мм".
        /// </summary>
        public override string ToString()
        {
            var parts = new List<string>();
            if (Platinum > 0) parts.Add($"{Platinum} пм");
            if (Gold > 0) parts.Add($"{Gold} зм");
            if (Electrum > 0) parts.Add($"{Electrum} эм");
            if (Silver > 0) parts.Add($"{Silver} см");
            if (Copper > 0 || parts.Count == 0) parts.Add($"{Copper} мм");
            return string.Join(", ", parts);
        }

        private static void CheckNonNegative(int value, string paramName)
        {
            if (value < 0)
                throw new ArgumentException("Значение не может быть отрицательным.", paramName);
        }
    }
}