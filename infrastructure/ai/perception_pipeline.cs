#nullable enable
using dnd_game.application.projections;
using dnd_game.domain.events;
using dnd_game.domain.value_objects;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.world;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.ai
{
    /// <summary>
    /// Результат восприятия конкретной сущности наблюдателем.
    /// </summary>
    public class PerceptionResult
    {
        public Guid EntityId { get; set; }
        public bool IsDetected { get; set; }
        public string DetectionMethod { get; set; } = string.Empty; // "зрение", "слух", "обоняние", "тёмное зрение" и т.д.
        public int PerceptionCheckResult { get; set; }
        public int StealthCheckResult { get; set; }
    }

    /// <summary>
    /// Конвейер восприятия, моделирующий правила DnD 5e для обнаружения существ.
    /// Использует доску объявлений для хранения результатов и публикует события обнаружения.
    /// </summary>
    public class PerceptionPipeline(
        CharacterProjection characterProjection,
        IBlackboardStore blackboard,
        IGridProvider grid,
        ICommandBus? commandBus = null,
        IEventBus? eventBus = null,
        ILogger<PerceptionPipeline>? logger = null)
    {
        private readonly CharacterProjection _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
        private readonly IBlackboardStore _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        private readonly ICommandBus? _commandBus = commandBus;
        private readonly IEventBus? _eventBus = eventBus;
        private readonly IGridProvider _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        private readonly ILogger<PerceptionPipeline> _logger = logger ?? NullLogger<PerceptionPipeline>.Instance;

        // Константы дальности (в футах)
        private const int NormalVisionRangeFeet = 1200;
        private const int DimLightVisionRangeFeet = 60;
        private const int DarkvisionRangeFeet = 60;
        private const int BlindsightRangeFeet = 30;
        private const int TremorsenseRangeFeet = 60;
        private const int TruesightRangeFeet = 120;
        private const int HearingRangeFeet = 60;
        private const int SmellRangeFeet = 30;

        /// <summary>
        /// Возвращает список идентификаторов сущностей, обнаруженных наблюдателем.
        /// </summary>
        public async Task<List<Guid>> GetVisibleEntities(Guid observerId, CancellationToken ct = default)
        {
            var results = await PerceiveAllEntities(observerId, ct);
            return [.. results.Where(r => r.IsDetected).Select(r => r.EntityId)];
        }

        /// <summary>
        /// Выполняет полное восприятие всех потенциальных целей вокруг наблюдателя.
        /// </summary>
        public async Task<List<PerceptionResult>> PerceiveAllEntities(Guid observerId, CancellationToken ct = default)
        {
            ValidateEntityId(observerId);
            ct.ThrowIfCancellationRequested();

            var observer = await _characterProjection.GetById(observerId, ct);
            if (observer == null)
            {
                _logger.LogWarning("Наблюдатель {ObserverId} не найден в проекции", observerId);
                return [];
            }

            // Все персонажи — в реальной системе следует ограничить область поиска (регион, расстояние)
            var allCharacters = await _characterProjection.GetAll(ct);
            var results = new List<PerceptionResult>();

            var senses = GetSenses(observer);
            int passivePerception = CalculatePassivePerception(observer);

            foreach (var target in allCharacters)
            {
                ct.ThrowIfCancellationRequested();
                if (target.Id == observerId) continue;

                var observerPos = new Position(observer.PositionX, observer.PositionY);
                var targetPos = new Position(target.PositionX, target.PositionY);

                int distanceFeet = await EstimateDistanceAsync(observerId, target.Id, ct);
                if (distanceFeet == int.MaxValue) continue;

                int targetStealth = await GetTargetStealth(target.Id, ct);
                bool isInvisible = target.Conditions?.Contains("Invisible", StringComparer.OrdinalIgnoreCase) ?? false;
                bool isHidden = await IsActivelyHiding(target.Id);

                var result = new PerceptionResult
                {
                    EntityId = target.Id,
                    StealthCheckResult = targetStealth
                };

                bool detected = DetectBySenses(
                    senses,
                    passivePerception,
                    targetStealth,
                    isInvisible,
                    isHidden,
                    distanceFeet,
                    observerPos,
                    targetPos,
                    out string method);

                result.IsDetected = detected;
                result.DetectionMethod = method;
                result.PerceptionCheckResult = passivePerception;

                if (detected)
                {
                    await _blackboard.SetFact(observerId, $"Detected_{target.Id}", true, FactType.EntityState, expiration: TimeSpan.FromSeconds(30));
                    await _blackboard.SetFact(observerId, $"Target_{target.Id}_Distance", distanceFeet, FactType.Location, expiration: TimeSpan.FromSeconds(10));

                    bool alreadyDetected = await IsAlreadyDetected(observerId, target.Id);
                    if (!alreadyDetected && _eventBus != null)
                    {
                        await _eventBus.PublishAsync(new EntityDetectedEvent(observerId, target.Id, method), ct);
                        _logger.LogDebug("Наблюдатель {ObserverId} обнаружил {TargetId} с помощью {Method}", observerId, target.Id, method);
                    }
                }
                else
                {
                    await _blackboard.RemoveFact(observerId, $"Detected_{target.Id}");
                }

                results.Add(result);
            }

            return results;
        }

        // --------------------------------------------------------------------------------
        // Вспомогательные методы
        // --------------------------------------------------------------------------------

        private bool DetectBySenses(
    IReadOnlyCollection<SenseType> senses,
    int passivePerception,
    int targetStealth,
    bool isInvisible,
    bool isHidden,
    int distanceFeet,
    Position observerPos,   // ← новая позиция наблюдателя
    Position targetPos,
    out string method)
        {
            method = string.Empty;

            // 1. Обычное зрение (зависит от освещения)
            if (senses.Contains(SenseType.NormalVision) && !isInvisible)
            {
                LightLevel light = GetLightLevelAt(targetPos);
                if (light == LightLevel.Bright && distanceFeet <= NormalVisionRangeFeet)
                {
                    method = "зрение (яркий свет)";
                    return true;
                }
                if (light == LightLevel.Dim && distanceFeet <= DimLightVisionRangeFeet)
                {
                    if ((passivePerception - 5) >= targetStealth && !isHidden)
                    {
                        method = "зрение (тусклый свет)";
                        return true;
                    }
                }
            }

            // 2. Тёмное зрение (Darkvision) – видит в темноте как в тусклом свете
            if (senses.Contains(SenseType.Darkvision) && distanceFeet <= DarkvisionRangeFeet && !isInvisible)
            {
                LightLevel light = GetLightLevelAt(targetPos);
                if (light == LightLevel.Darkness)
                {
                    if ((passivePerception - 5) >= targetStealth && !isHidden)
                    {
                        method = "тёмное зрение";
                        return true;
                    }
                }
            }

            // 3. Истинное зрение (Truesight) – видит невидимое
            if (senses.Contains(SenseType.Truesight) && distanceFeet <= TruesightRangeFeet)
            {
                method = "истинное зрение";
                return true;
            }

            // 4. Слепое зрение (Blindsight) – не зависит от зрения
            if (senses.Contains(SenseType.Blindsight) && distanceFeet <= BlindsightRangeFeet)
            {
                method = "слепое зрение";
                return true;
            }

            // 5. Чувство вибрации (Tremorsense) – работает только на одной поверхности
            if (senses.Contains(SenseType.Tremorsense) && distanceFeet <= TremorsenseRangeFeet)
            {
                // Проверяем, находятся ли наблюдатель и цель на одной высоте (поверхности)
                if (IsOnSameSurface(observerPos, targetPos))
                {
                    method = "чувство вибрации";
                    return true;
                }
            }

            // 6. Слух
            if (senses.Contains(SenseType.Hearing) && distanceFeet <= HearingRangeFeet)
            {
                int hearingDC = isHidden ? targetStealth : 10;
                if (passivePerception >= hearingDC)
                {
                    method = "слух";
                    return true;
                }
            }

            // 7. Обоняние
            if (senses.Contains(SenseType.Smell) && distanceFeet <= SmellRangeFeet)
            {
                method = "обоняние";
                return true;
            }

            return false;
        }

        private static List<SenseType> GetSenses(CharacterDto character)
        {
            var senses = new List<SenseType> { SenseType.NormalVision, SenseType.Hearing };

            // Упрощённо определяем тёмное зрение по расе
            if (character.Race?.Contains("Эльф", StringComparison.OrdinalIgnoreCase) == true ||
                character.Race?.Contains("Дварф", StringComparison.OrdinalIgnoreCase) == true ||
                character.Race?.Contains("Гном", StringComparison.OrdinalIgnoreCase) == true ||
                character.Race?.Contains("Полуорк", StringComparison.OrdinalIgnoreCase) == true ||
                character.Race?.Contains("Тифлинг", StringComparison.OrdinalIgnoreCase) == true)
            {
                senses.Add(SenseType.Darkvision);
            }

            // TODO: добавить Blindsight, Truesight, Tremorsense по классовым/расовым особенностям или заклинаниям.
            return senses;
        }

        private static int CalculatePassivePerception(CharacterDto character)
        {
            int wisMod = ModifierCalculator.Calculate(character.AbilityScores.GetValueOrDefault("Wisdom", 10));
            bool proficient = character.SkillProficiencies?.ContainsKey("Perception") ?? false;
            int profBonus = proficient ? character.ProficiencyBonus : 0;

            // Преимущество на Внимательность добавляет +5 (можно учесть через факты, но пока опускаем)
            return 10 + wisMod + profBonus;
        }

        private bool IsOnSameSurface(Position observerPos, Position targetPos)
        {
            var cell1 = _grid.GetCell(observerPos.X, observerPos.Y);
            var cell2 = _grid.GetCell(targetPos.X, targetPos.Y);
            return cell1?.Height == cell2?.Height;
        }

        private async Task<int> GetTargetStealth(Guid targetId, CancellationToken ct)
        {
            var target = await _characterProjection.GetById(targetId, ct);
            if (target == null) return 10;

            int dexMod = ModifierCalculator.Calculate(target.AbilityScores.GetValueOrDefault("Dexterity", 10));
            bool proficient = target.SkillProficiencies?.ContainsKey("Stealth") ?? false;
            int profBonus = proficient ? target.ProficiencyBonus : 0;

            return 10 + dexMod + profBonus; // пассивная Скрытность
        }

        private async Task<bool> IsActivelyHiding(Guid targetId)
        {
            var fact = await _blackboard.GetFact(targetId, "IsHiding");
            return fact?.Value is bool hiding && hiding;
        }

        private async Task<int> EstimateDistanceAsync(Guid observerId, Guid targetId, CancellationToken ct)
        {
            var observer = await _characterProjection.GetById(observerId, ct);
            var target = await _characterProjection.GetById(targetId, ct);
            if (observer == null || target == null) return int.MaxValue;

            var observerPos = new Position(observer.PositionX, observer.PositionY);
            var targetPos = new Position(target.PositionX, target.PositionY);
            return _grid.GetDistance(observerPos, targetPos);
        }

        private LightLevel GetLightLevelAt(Position pos)
        {
            if (!_grid.InBounds(pos.X, pos.Y)) return LightLevel.Darkness;
            var cell = _grid.GetCell(pos.X, pos.Y);
            // Предполагаем, что ячейка имеет свойство Light (если нет, нужно адаптировать)
            return cell?.Light ?? LightLevel.Darkness;
        }

        private async Task<bool> IsAlreadyDetected(Guid observerId, Guid targetId)
        {
            var fact = await _blackboard.GetFact(observerId, $"Detected_{targetId}");
            return fact != null;
        }

        private static void ValidateEntityId(Guid entityId)
        {
            if (entityId == Guid.Empty)
                throw new ArgumentException("Идентификатор сущности не может быть пустым.", nameof(entityId));
        }
    }

    /// <summary>
    /// Типы чувств, которыми может обладать существо.
    /// </summary>
    public enum SenseType
    {
        NormalVision,
        Darkvision,
        Blindsight,
        Truesight,
        Tremorsense,
        Hearing,
        Smell
    }

    /// <summary>
    /// Событие: сущность обнаружена наблюдателем.
    /// </summary>
    public record EntityDetectedEvent(Guid ObserverId, Guid DetectedId, string Method) : IDomainEvent;
}