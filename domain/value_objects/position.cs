#nullable enable
using System;

namespace dnd_game.domain.value_objects
{
    /// <summary>
    /// Представляет позицию на двумерной тактической карте (в футах или квадратах).
    /// Поддерживает квадратную (5-футовые квадраты) и гексагональную сетку.
    /// </summary>
    public record Position(int X, int Y)
    {
        // ---------- Константы сетки ----------

        /// <summary>Стандартный размер квадрата в футах (5 ft).</summary>
        public const int DefaultGridSizeFeet = 5;

        // ---------- Арифметика точек ----------

        public static Position operator +(Position a, Position b) => new(a.X + b.X, a.Y + b.Y);
        public static Position operator -(Position a, Position b) => new(a.X - b.X, a.Y - b.Y);
        public static Position operator *(Position a, int factor) => new(a.X * factor, a.Y * factor);

        public static Position operator /(Position a, int divisor)
        {
            if (divisor == 0)
                throw new DivideByZeroException("Деление на ноль при работе с координатами.");
            return new Position(a.X / divisor, a.Y / divisor);
        }

        // ---------- Измерения расстояния ----------

        /// <summary>
        /// Евклидово расстояние в футах (при условии, что единицы координат — футы).
        /// </summary>
        public double DistanceTo(Position other)
        {
            int dx = other.X - X;
            int dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Расстояние в квадратной сетке (метрика Чебышёва) — стандарт DnD для квадратной сетки.
        /// Каждый шаг по горизонтали, вертикали или диагонали считается за 1 квадрат.
        /// Возвращает расстояние в количестве квадратов.
        /// </summary>
        public int ChebyshevDistanceInSquares(Position other) =>
            Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

        /// <summary>
        /// Расстояние по квадратной сетке в футах (с учётом размера квадрата).
        /// </summary>
        public int ChebyshevDistanceInFeet(Position other, int gridSizeFeet = DefaultGridSizeFeet) =>
            ChebyshevDistanceInSquares(other) * gridSizeFeet;

        /// <summary>
        /// Манхэттенское расстояние (для альтернативных правил или гексагональной сетки).
        /// </summary>
        public int ManhattanDistanceInSquares(Position other) =>
            Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        // ---------- Направления и ориентация ----------

        /// <summary>
        /// Восемь основных направлений на квадратной сетке.
        /// Ориентация осей: X увеличивается вправо, Y увеличивается вверх.
        /// </summary>
        public enum Direction
        {
            North,
            NorthEast,
            East,
            SouthEast,
            South,
            SouthWest,
            West,
            NorthWest
        }

        /// <summary>
        /// Возвращает соседнюю позицию в указанном направлении.
        /// </summary>
        public Position Neighbor(Direction direction)
        {
            return direction switch
            {
                Direction.North => new(X, Y + 1),
                Direction.NorthEast => new(X + 1, Y + 1),
                Direction.East => new(X + 1, Y),
                Direction.SouthEast => new(X + 1, Y - 1),
                Direction.South => new(X, Y - 1),
                Direction.SouthWest => new(X - 1, Y - 1),
                Direction.West => new(X - 1, Y),
                Direction.NorthWest => new(X - 1, Y + 1),
                _ => this
            };
        }

        /// <summary>
        /// Возвращает все восемь соседних позиций.
        /// </summary>
        public IEnumerable<Position> GetNeighbors()
        {
            foreach (Direction dir in Enum.GetValues<Direction>())
                yield return Neighbor(dir);
        }

        // ---------- Проверка соседства (для атак ближнего боя) ----------

        /// <summary>
        /// Находятся ли две позиции в соседних квадратах (8-связное соседство, стандарт DnD 5e).
        /// Позиция не считается соседней самой себе.
        /// </summary>
        public bool IsAdjacent(Position other) =>
            Math.Abs(X - other.X) <= 1 && Math.Abs(Y - other.Y) <= 1 && this != other;

        /// <summary>
        /// Находится ли цель в пределах досягаемости оружия (reach).
        /// По умолчанию reach = 1 (5 ft). Для оружия с reach = 2 (10 ft).
        /// </summary>
        public bool IsWithinReach(Position other, int reachInSquares = 1) =>
            ChebyshevDistanceInSquares(other) <= reachInSquares;

        // ---------- Дистанция для дальнобойных атак ----------

        /// <summary>
        /// Проверяет, находится ли цель в пределах нормальной дальности оружия/заклинания.
        /// </summary>
        public bool IsWithinRange(Position other, int normalRangeFeet) =>
            ChebyshevDistanceInFeet(other) <= normalRangeFeet;

        /// <summary>
        /// Проверяет, находится ли цель в пределах максимальной (длинной) дальности.
        /// </summary>
        public bool IsWithinLongRange(Position other, int longRangeFeet) =>
            ChebyshevDistanceInFeet(other) <= longRangeFeet;

        // ---------- Преобразования ----------

        public override string ToString() => $"({X}, {Y})";
    }
}