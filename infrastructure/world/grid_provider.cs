#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.domain.value_objects;

namespace dnd_game.infrastructure.world
{
    /// <summary>Тип сетки игрового поля.</summary>
    public enum GridType
    {
        Square,
        Hex
    }

    /// <summary>Тип местности клетки.</summary>
    public enum CellTerrain
    {
        Normal,
        Difficult,
        Road,
        ShallowWater,
        DeepWater,
        Lava,
        Wall,
        Window,
        Door,
        HiddenDoor,
        Ice,
        Mud,
        Rubble,
        Thorns
    }

    /// <summary>Ячейка сетки.</summary>
    public sealed class Cell
    {
        /// <summary>Тип местности.</summary>
        public CellTerrain Terrain { get; set; } = CellTerrain.Normal;

        /// <summary>Высота (для определения укрытий).</summary>
        public int Height { get; set; }

        /// <summary>Уровень освещения.</summary>
        public LightLevel Light { get; set; } = LightLevel.Bright;

        /// <summary>Блокирует ли зрение.</summary>
        public bool BlocksVision { get; set; }

        /// <summary>Блокирует ли движение.</summary>
        public bool BlocksMovement { get; set; }

        /// <summary>Дополнительный флаг труднопроходимости (может использоваться вместо Terrain).</summary>
        public bool IsDifficult { get; set; }
    }

    /// <summary>Провайдер игровой сетки: управление клетками, поиск пути, линия обзора.</summary>
    public interface IGridProvider
    {
        int Width { get; }
        int Height { get; }
        GridType Type { get; }

        bool InBounds(int x, int y);
        bool IsWalkable(int x, int y);
        bool IsDifficultTerrain(int x, int y);

        Cell GetCell(int x, int y);
        void SetCell(int x, int y, Cell cell);

        /// <summary>Вычисляет расстояние между позициями в клетках (приближённо, для игровых нужд).</summary>
        int GetDistance(Position from, Position to);

        bool LineOfSight(Position from, Position to);
        List<Position> FindPath(Position from, Position to);

        /// <summary>Возвращает тип укрытия цели относительно атакующего ("None", "Half", "ThreeQuarters", "Full").</summary>
        string GetCoverType(Position attacker, Position target);
    }

    /// <summary>
    /// Реализация провайдера сетки. Поддерживает квадратную и гексагональную сетки.
    /// Не является потокобезопасной: одновременное изменение клеток и поиск пути могут дать гонку.
    /// При необходимости использовать в многопоточной среде — добавить блокировки.
    /// </summary>
    public sealed class GridProvider : IGridProvider
    {
        private readonly Cell[,] _grid;
        private readonly object _lock = new();

        public int Width { get; }
        public int Height { get; }
        public GridType Type { get; }

        public GridProvider(int width = 100, int height = 100, GridType type = GridType.Square)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Ширина сетки должна быть положительной.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Высота сетки должна быть положительной.");

            Width = width;
            Height = height;
            Type = type;
            _grid = new Cell[width, height];

            InitializeGrid();
        }

        private void InitializeGrid()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _grid[x, y] = new Cell();
        }

        /// <inheritdoc />
        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <inheritdoc />
        public Cell GetCell(int x, int y)
        {
            if (!InBounds(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), "Координаты вне границ сетки.");
            return _grid[x, y];
        }

        /// <inheritdoc />
        public void SetCell(int x, int y, Cell cell)
        {
            if (!InBounds(x, y))
                throw new ArgumentOutOfRangeException(nameof(x), "Координаты вне границ сетки.");
            ArgumentNullException.ThrowIfNull(cell, nameof(cell));

            lock (_lock)
            {
                _grid[x, y] = cell;
            }
        }

        /// <inheritdoc />
        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y))
                return false;

            var cell = _grid[x, y];
            if (cell.BlocksMovement)
                return false;

            return cell.Terrain switch
            {
                CellTerrain.Wall or CellTerrain.DeepWater or CellTerrain.Lava => false,
                _ => true,
            };
        }

        /// <inheritdoc />
        public bool IsDifficultTerrain(int x, int y)
        {
            if (!InBounds(x, y))
                return false;

            var cell = _grid[x, y];
            return cell.IsDifficult ||
                   cell.Terrain is CellTerrain.Difficult
                       or CellTerrain.ShallowWater
                       or CellTerrain.Ice
                       or CellTerrain.Mud
                       or CellTerrain.Rubble
                       or CellTerrain.Thorns;
        }

        /// <inheritdoc />
        public int GetDistance(Position from, Position to)
        {
            if (!InBounds(from.X, from.Y))
                throw new ArgumentOutOfRangeException(nameof(from), "Координаты вне границ сетки.");
            if (!InBounds(to.X, to.Y))
                throw new ArgumentOutOfRangeException(nameof(to), "Координаты вне границ сетки.");

            return Type switch
            {
                GridType.Square => from.ChebyshevDistanceInSquares(to),
                GridType.Hex => CalculateHexDistance(from, to),
                _ => from.ChebyshevDistanceInSquares(to)
            };
        }

        /// <summary>Вычисляет расстояние на гексагональной сетке (для axial coordinates).</summary>
        private static int CalculateHexDistance(Position a, Position b)
        {
            // Предполагаем, что координаты X,Y соответствуют axial coords (q,r)
            int dq = a.X - b.X;
            int dr = a.Y - b.Y;
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq - dr)) / 2;
        }

        /// <inheritdoc />
        public bool LineOfSight(Position from, Position to)
        {
            if (!InBounds(from.X, from.Y) || !InBounds(to.X, to.Y))
                return false;

            int x0 = from.X, y0 = from.Y;
            int x1 = to.X, y1 = to.Y;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            int currentX = x0, currentY = y0;
            while (!(currentX == x1 && currentY == y1))
            {
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; currentX += sx; }
                if (e2 < dx) { err += dx; currentY += sy; }

                if (!InBounds(currentX, currentY)) return false;
                if (_grid[currentX, currentY].BlocksVision) return false;
            }
            return true;
        }

        /// <inheritdoc />
        public List<Position> FindPath(Position from, Position to)
        {
            if (!InBounds(from.X, from.Y) || !InBounds(to.X, to.Y))
                return [];
            if (!IsWalkable(to.X, to.Y))
                return [];

            // A* с приоритетной очередью (PriorityQueue доступна в .NET 6+)
            var open = new PriorityQueue<Position, int>();
            var cameFrom = new Dictionary<Position, Position>();
            var gScore = new Dictionary<Position, int> { [from] = 0 };
            var fScore = new Dictionary<Position, int> { [from] = Heuristic(from, to) };

            open.Enqueue(from, fScore[from]);

            // 8 направлений: 4 прямых (стоимость 1) и 4 диагональных (стоимость 2 для простоты)
            // Можно усложнить: диагональ стоит 3 (1.5 * 2), но для D&D обычно диагональ = 1 квадрат (5 фт)
            // Здесь мы используем стоимость 2, что приблизительно соответствует 5e (диагональ = 2 клетки)
            (int dx, int dy, int cost)[] directions =
            [
                (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
                (1, 1, 2), (1, -1, 2), (-1, 1, 2), (-1, -1, 2)
            ];

            while (open.Count > 0)
            {
                var current = open.Dequeue();
                if (current == to)
                    return ReconstructPath(cameFrom, current);

                foreach (var (dx, dy, moveCost) in directions)
                {
                    var next = new Position(current.X + dx, current.Y + dy);
                    if (!InBounds(next.X, next.Y) || !IsWalkable(next.X, next.Y))
                        continue;

                    int terrainCost = IsDifficultTerrain(next.X, next.Y) ? 2 : 1;
                    int totalMoveCost = moveCost * terrainCost; // множитель для диагонали и труднопроходимости
                    int tentativeG = gScore[current] + totalMoveCost;

                    if (!gScore.TryGetValue(next, out var currentG) || tentativeG < currentG)
                    {
                        cameFrom[next] = current;
                        gScore[next] = tentativeG;
                        int f = tentativeG + Heuristic(next, to);
                        fScore[next] = f;
                        open.Enqueue(next, f);
                    }
                }
            }

            return []; // путь не найден
        }

        /// <inheritdoc />
        public string GetCoverType(Position attacker, Position target)
        {
            if (!InBounds(attacker.X, attacker.Y) || !InBounds(target.X, target.Y))
                return "Full";

            // Упрощённая оценка укрытия:
            // - если нет линии обзора, полное укрытие
            // - если линия есть, проверяем высоту и препятствия между точками
            if (!LineOfSight(attacker, target))
                return "Full";

            // Пока считаем, что укрытия нет, если линия видимости чистая.
            // В будущем можно сканировать луч и определять тип укрытия.
            return "None";
        }

        private static int Heuristic(Position a, Position b)
        {
            // Для 8-направленного движения с диагональной стоимостью 2,
            // допустимая эвристика = max(dx,dy) + (sqrt(2)-1)*min(dx,dy) ≈ max + 0.414*min.
            // Округляем вверх для целочисленной стоимости.
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            return Math.Max(dx, dy) + Math.Min(dx, dy) / 2; // немного занижено, но допустимо
        }

        private static List<Position> ReconstructPath(Dictionary<Position, Position> cameFrom, Position current)
        {
            var path = new List<Position> { current };
            while (cameFrom.TryGetValue(current, out var previous))
            {
                current = previous;
                path.Add(current);
            }
            path.Reverse();
            return path;
        }
    }
}