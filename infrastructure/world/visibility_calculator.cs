#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.domain.value_objects;

namespace dnd_game.infrastructure.world
{
    /// <summary>
    /// Типы чувств, используемые при расчёте видимости.
    /// </summary>
    public enum SenseType
    {
        NormalVision,
        Darkvision,
        Blindsight,
        Tremorsense,
        Truesight,
        Hearing,
        Smell
    }

    /// <summary>
    /// Профиль зрения существа: какие чувства доступны и их радиусы.
    /// </summary>
    public sealed class VisionProfile
    {
        /// <summary>Список доступных чувств (по умолчанию только обычное зрение).</summary>
        public List<SenseType> Senses { get; set; } = [SenseType.NormalVision];

        /// <summary>Радиус тёмного зрения в футах.</summary>
        public int DarkvisionRange { get; set; } = 60;

        /// <summary>Радиус слепого зрения в футах.</summary>
        public int BlindsightRange { get; set; } = 30;

        /// <summary>Радиус чувства вибрации в футах.</summary>
        public int TremorsenseRange { get; set; } = 60;

        /// <summary>Радиус истинного зрения в футах.</summary>
        public int TruesightRange { get; set; } = 120;

        /// <summary>Полностью блокирует зрение (например, ослепление).</summary>
        public bool IsBlinded { get; set; }
    }

    /// <summary>
    /// Результат вычисления видимости: наборы клеток по типам видимости.
    /// </summary>
    public sealed class VisibilityResult
    {
        /// <summary>Клетки, полностью видимые.</summary>
        public HashSet<(int x, int y)> VisibleCells { get; set; } = [];

        /// <summary>Клетки, видимые при тусклом свете или тёмном зрении.</summary>
        public HashSet<(int x, int y)> DimlyVisibleCells { get; set; } = [];

        /// <summary>Клетки, ощущаемые через вибрацию (Tremorsense).</summary>
        public HashSet<(int x, int y)> TremorSensedCells { get; set; } = [];
    }

    /// <summary>
    /// Вычислитель видимости, соответствующий правилам DnD 5e.
    /// Учитывает освещение, типы чувств и препятствия на сетке.
    /// </summary>
    public sealed class VisibilityCalculator(IGridProvider grid)
    {
        private readonly IGridProvider _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        private const int NormalVisionRangeFeet = 1200; // максимальная дальность обычного зрения в ярком свете

        /// <summary>
        /// Рассчитывает поле зрения (FOV) для наблюдателя на сетке.
        /// </summary>
        /// <param name="originX">X координата наблюдателя.</param>
        /// <param name="originY">Y координата наблюдателя.</param>
        /// <param name="visionProfile">Профиль зрения. Если null, используется базовый профиль.</param>
        /// <returns>Результат с наборами видимых клеток.</returns>
        public VisibilityResult CalculateFieldOfView(int originX, int originY, VisionProfile? visionProfile = null)
        {
            visionProfile ??= new VisionProfile();
            var result = new VisibilityResult();

            // Если наблюдатель ослеплён, обычное зрение отключено,
            // но Blindsight и Tremorsense могут работать.
            if (!visionProfile.IsBlinded)
            {
                if (visionProfile.Senses.Contains(SenseType.Truesight))
                {
                    // Истинное зрение видит всё в радиусе, игнорируя препятствия.
                    AddAreaCells(originX, originY, visionProfile.TruesightRange, result.VisibleCells);
                }
                else
                {
                    // Обычное зрение + тёмное зрение — raycasting с учётом освещения.
                    ProcessVision(originX, originY, visionProfile, result);
                }
            }

            // Слепое зрение работает независимо от освещения, но блокируется стенами.
            if (visionProfile.Senses.Contains(SenseType.Blindsight))
            {
                AddBlindsightCells(originX, originY, visionProfile.BlindsightRange, result.VisibleCells);
            }

            // Чувство вибрации ощущает всё в радиусе, игнорируя препятствия.
            if (visionProfile.Senses.Contains(SenseType.Tremorsense))
            {
                AddAreaCells(originX, originY, visionProfile.TremorsenseRange, result.TremorSensedCells);
            }

            return result;
        }

        /// <summary>
        /// Проверяет, видит ли наблюдатель цель с учётом освещения и препятствий.
        /// </summary>
        /// <param name="observer">Позиция наблюдателя.</param>
        /// <param name="target">Позиция цели.</param>
        /// <param name="visionProfile">Профиль зрения наблюдателя (не может быть null).</param>
        /// <returns>True, если есть прямая видимость.</returns>
        public bool HasLineOfSight(Position observer, Position target, VisionProfile visionProfile)
        {
            ArgumentNullException.ThrowIfNull(visionProfile);

            if (!_grid.InBounds(observer.X, observer.Y) || !_grid.InBounds(target.X, target.Y))
                return false;

            // Истинное зрение игнорирует все препятствия и освещение.
            if (visionProfile.Senses.Contains(SenseType.Truesight))
            {
                return _grid.GetDistance(observer, target) <= visionProfile.TruesightRange;
            }

            // Ослеплён — обычного зрения нет.
            if (visionProfile.IsBlinded)
                return false;

            int distance = _grid.GetDistance(observer, target);
            LightLevel light = GetEffectiveLightAt(target);

            // Определяем, видит ли в зависимости от освещения и наличия тёмного зрения.
            switch (light)
            {
                case LightLevel.Bright:
                    // Обычное зрение: ограничено только дальностью 1200 футов.
                    if (distance > NormalVisionRangeFeet)
                        return false;
                    break;

                case LightLevel.Dim:
                    // Без тёмного зрения в тусклом свете дистанция ограничена 60 футами.
                    if (!visionProfile.Senses.Contains(SenseType.Darkvision) && distance > 60)
                        return false;
                    // С тёмным зрением тусклый свет воспринимается как яркий.
                    break;

                case LightLevel.Darkness:
                    // В темноте видит только обладатель тёмного зрения.
                    if (!visionProfile.Senses.Contains(SenseType.Darkvision))
                        return false;
                    if (distance > visionProfile.DarkvisionRange)
                        return false;
                    break;
            }

            // Проверяем, не блокируется ли прямая видимость стенами.
            return _grid.LineOfSight(observer, target);
        }

        // ---------- Приватные методы ----------

        /// <summary>
        /// Выполняет raycasting по кругу с шагом 1 градус.
        /// Останавливается на препятствиях, учитывает освещение и радиусы чувств.
        /// </summary>
        private void ProcessVision(int ox, int oy, VisionProfile profile, VisibilityResult result)
        {
            bool hasDarkvision = profile.Senses.Contains(SenseType.Darkvision);
            int maxRadius = NormalVisionRangeFeet; // 1200 футов

            for (int angle = 0; angle < 360; angle += 1)
            {
                double rad = angle * Math.PI / 180.0;
                double dx = Math.Cos(rad);
                double dy = Math.Sin(rad);

                for (int step = 1; step <= maxRadius; step++)
                {
                    int newX = ox + (int)Math.Round(dx * step);
                    int newY = oy + (int)Math.Round(dy * step);
                    if (!_grid.InBounds(newX, newY))
                        break;

                    var cellPos = new Position(newX, newY);
                    LightLevel light = GetEffectiveLightAt(cellPos);
                    bool cellVisible = false;
                    bool cellDim = false;

                    switch (light)
                    {
                        case LightLevel.Bright:
                            cellVisible = true;
                            break;

                        case LightLevel.Dim:
                            if (hasDarkvision)
                                cellVisible = true; // тёмное зрение превращает тусклый свет в яркий
                            else
                                cellDim = true;      // без тёмного зрения видно как тускло
                            break;

                        case LightLevel.Darkness:
                            if (hasDarkvision && step <= profile.DarkvisionRange)
                                cellDim = true;      // тёмное зрение в темноте даёт тусклое зрение
                            else
                                // Дальше этой клетки луч не проходит
                                goto stopRay;
                            break;
                    }

                    if (cellVisible)
                        result.VisibleCells.Add((newX, newY));
                    else if (cellDim)
                        result.DimlyVisibleCells.Add((newX, newY));

                    // Если клетка блокирует зрение, луч останавливается.
                    if (_grid.GetCell(newX, newY).BlocksVision)
                        goto stopRay;

                    continue;

                    stopRay:
                    break;
                }
            }
        }

        /// <summary>
        /// Добавляет все клетки в пределах радиуса, игнорируя препятствия.
        /// Используется для Truesight и Tremorsense.
        /// </summary>
        private void AddAreaCells(int ox, int oy, int range, HashSet<(int x, int y)> cells)
        {
            var origin = new Position(ox, oy);
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    int nx = ox + dx;
                    int ny = oy + dy;
                    if (!_grid.InBounds(nx, ny))
                        continue;
                    var target = new Position(nx, ny);
                    if (_grid.GetDistance(origin, target) <= range)
                        cells.Add((nx, ny));
                }
            }
        }

        /// <summary>
        /// Добавляет клетки в радиусе, но только если есть прямая видимость (LineOfSight).
        /// Используется для Blindsight (не проникает сквозь стены).
        /// </summary>
        private void AddBlindsightCells(int ox, int oy, int range, HashSet<(int x, int y)> cells)
        {
            var origin = new Position(ox, oy);
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    int nx = ox + dx;
                    int ny = oy + dy;
                    if (!_grid.InBounds(nx, ny))
                        continue;
                    var target = new Position(nx, ny);
                    if (_grid.GetDistance(origin, target) <= range && _grid.LineOfSight(origin, target))
                        cells.Add((nx, ny));
                }
            }
        }

        /// <summary>
        /// Возвращает уровень освещения в заданной клетке.
        /// </summary>
        private LightLevel GetEffectiveLightAt(Position pos)
        {
            if (!_grid.InBounds(pos.X, pos.Y))
                return LightLevel.Darkness;
            return _grid.GetCell(pos.X, pos.Y).Light;
        }
    }
}