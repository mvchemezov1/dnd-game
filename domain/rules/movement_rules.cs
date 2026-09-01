#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.world;

namespace dnd_game.domain.rules
{
    /// <summary>
    /// Правила перемещения по тактической карте в DnD 5e.
    /// Содержит расчёты скорости, стоимости клеток, проверки проходимости и урона от падения.
    /// </summary>
    public static class MovementRules
    {
        // --------------------------------------------------------------------------------
        // Базовая скорость
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Возвращает эффективную скорость персонажа с учётом нагрузки.
        /// </summary>
        /// <param name="baseSpeed">Базовая скорость в футах.</param>
        /// <param name="isEncumbered">Персонаж несёт значительный груз (штраф -10 фт.).</param>
        /// <param name="isHeavilyEncumbered">Персонаж перегружен (штраф -20 фт.).</param>
        /// <returns>Эффективная скорость (не может быть отрицательной).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если базовая скорость отрицательна.</exception>
        public static int GetEffectiveSpeed(int baseSpeed, bool isEncumbered = false, bool isHeavilyEncumbered = false)
        {
            if (baseSpeed < 0)
                throw new ArgumentOutOfRangeException(nameof(baseSpeed), "Базовая скорость не может быть отрицательной.");

            int speed = baseSpeed;
            if (isHeavilyEncumbered)
                speed -= 20;
            else if (isEncumbered)
                speed -= 10;

            return Math.Max(0, speed);
        }

        // --------------------------------------------------------------------------------
        // Стоимость перемещения по местности
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Возвращает стоимость перемещения на одну клетку в футах для заданного типа местности.
        /// </summary>
        /// <param name="terrain">Тип местности клетки.</param>
        /// <returns>Стоимость в футах или -1, если клетка непроходима.</returns>
        public static int GetMovementCostPerCell(CellTerrain terrain)
        {
            return terrain switch
            {
                CellTerrain.Normal => 5,
                CellTerrain.Road => 5,
                CellTerrain.Difficult => 10,
                CellTerrain.ShallowWater => 10,
                CellTerrain.Ice => 10,
                CellTerrain.Mud => 10,
                CellTerrain.Rubble => 10,
                CellTerrain.Thorns => 10,
                CellTerrain.DeepWater => -1,
                CellTerrain.Lava => -1,
                CellTerrain.Wall => -1,
                CellTerrain.Door => 5,
                CellTerrain.Window => 5,
                CellTerrain.HiddenDoor => 5,
                _ => 5 // неизвестная местность считается обычной
            };
        }

        /// <summary>
        /// Проверяет, может ли персонаж войти в клетку с учётом оставшейся скорости.
        /// </summary>
        /// <param name="terrain">Тип местности клетки.</param>
        /// <param name="remainingSpeed">Оставшаяся скорость в футах.</param>
        /// <returns><c>true</c>, если клетка проходима и у персонажа достаточно скорости.</returns>
        public static bool CanEnterCell(CellTerrain terrain, int remainingSpeed)
        {
            if (remainingSpeed < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingSpeed), "Оставшаяся скорость не может быть отрицательной.");

            int cost = GetMovementCostPerCell(terrain);
            return cost >= 0 && remainingSpeed >= cost;
        }

        // --------------------------------------------------------------------------------
        // Расчёт пути и стоимости
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Вычисляет общую стоимость перемещения по заданному пути в футах.
        /// Первая позиция в списке считается текущей и не учитывается.
        /// </summary>
        /// <param name="grid">Провайдер сетки для получения клеток.</param>
        /// <param name="path">Список позиций, образующих путь.</param>
        /// <returns>Общая стоимость пути или -1, если путь содержит непроходимую клетку.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="grid"/> или <paramref name="path"/> равны null.</exception>
        /// <exception cref="ArgumentException">Если путь содержит менее двух позиций.</exception>
        public static int CalculatePathCost(IGridProvider grid, IReadOnlyList<Position> path)
        {
            ArgumentNullException.ThrowIfNull(grid);
            ArgumentNullException.ThrowIfNull(path);
            if (path.Count < 2)
                return 0; // нет перемещения

            int totalCost = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var pos = path[i];
                var cell = grid.GetCell(pos.X, pos.Y);
                int cost = GetMovementCostPerCell(cell.Terrain);
                if (cost < 0)
                    return -1; // путь содержит непроходимую клетку

                totalCost += cost;
            }
            return totalCost;
        }

        /// <summary>
        /// Проверяет, может ли персонаж пройти весь путь с текущим запасом скорости.
        /// </summary>
        /// <param name="grid">Провайдер сетки.</param>
        /// <param name="path">Путь.</param>
        /// <param name="remainingSpeed">Оставшаяся скорость.</param>
        /// <returns><c>true</c>, если путь проходим и требует не больше скорости, чем есть.</returns>
        public static bool CanTraversePath(IGridProvider grid, IReadOnlyList<Position> path, int remainingSpeed)
        {
            if (remainingSpeed < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingSpeed), "Оставшаяся скорость не может быть отрицательной.");

            int cost = CalculatePathCost(grid, path);
            return cost >= 0 && cost <= remainingSpeed;
        }

        // --------------------------------------------------------------------------------
        // Действия, связанные с движением
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Действие «Рывок» (Dash) удваивает доступное перемещение на текущий ход.
        /// </summary>
        public static int ApplyDash(int baseSpeed)
        {
            if (baseSpeed < 0)
                throw new ArgumentOutOfRangeException(nameof(baseSpeed), "Скорость не может быть отрицательной.");

            return baseSpeed * 2;
        }

        /// <summary>
        /// Действие «Отход» (Disengage) позволяет покинуть угрожаемую зону без провоцирования атак.
        /// </summary>
        public static bool CanDisengage(bool hasAction, bool isInMelee)
            => hasAction && isInMelee;

        /// <summary>
        /// Действие «Засада» (Hide) требует наличия укрытия и доступного действия.
        /// </summary>
        public static bool CanHide(bool hasAction, bool hasCover)
            => hasAction && hasCover;

        // --------------------------------------------------------------------------------
        // Проверки навыков, связанные с движением
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Проверяет успешность проверки Атлетики.
        /// </summary>
        public static bool AthleticsCheckSuccess(int roll, int dc)
        {
            if (roll < 1 || roll > 20)
                throw new ArgumentOutOfRangeException(nameof(roll), "Результат броска d20 должен быть от 1 до 20.");
            if (dc < 1)
                throw new ArgumentOutOfRangeException(nameof(dc), "Сложность проверки должна быть положительной.");

            return roll >= dc;
        }

        /// <summary>
        /// Проверяет успешность проверки Акробатики.
        /// </summary>
        public static bool AcrobaticsCheckSuccess(int roll, int dc)
        {
            if (roll < 1 || roll > 20)
                throw new ArgumentOutOfRangeException(nameof(roll), "Результат броска d20 должен быть от 1 до 20.");
            if (dc < 1)
                throw new ArgumentOutOfRangeException(nameof(dc), "Сложность проверки должна быть положительной.");

            return roll >= dc;
        }

        // --------------------------------------------------------------------------------
        // Падение
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Возвращает количество костей урона (d6) за падение с указанной высоты.
        /// Урон рассчитывается как 1d6 за каждые 10 футов падения, максимум 20d6.
        /// </summary>
        /// <param name="fallDistanceFeet">Высота падения в футах.</param>
        /// <returns>Количество костей d6 для броска урона.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если высота отрицательна.</exception>
        public static int CalculateFallDamageDice(int fallDistanceFeet)
        {
            if (fallDistanceFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(fallDistanceFeet), "Высота падения не может быть отрицательной.");

            int diceCount = fallDistanceFeet / 10; // целочисленное деление
            return Math.Min(diceCount, 20);
        }
    }
}