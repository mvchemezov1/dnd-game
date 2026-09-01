#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.rules;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.domain.sagas
{
    /// <summary>
    /// Сага повышения уровня персонажа. Обрабатывает событие получения опыта,
    /// вычисляет, сколько уровней персонаж должен получить, и отправляет команды
    /// для применения всех изменений: уровень, максимальные хиты, кости хитов, ячейки заклинаний.
    /// </summary>
    public class LevelUpSaga : ISaga
    {
        private readonly ICommandBus _commandBus;
        private readonly CharacterProjection _characterProjection; // TODO: заменить на интерфейс чтения, чтобы не зависеть от конкретной реализации
        private LevelUpSagaState _state;

        public LevelUpSaga(Guid characterId, ICommandBus commandBus, CharacterProjection characterProjection)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _state = new LevelUpSagaState
            {
                SagaId = characterId,
                CorrelationId = characterId,
                CreatedAt = DateTime.UtcNow,
                Status = SagaStatus.Started
            };
        }

        public Guid SagaId => _state.SagaId;
        public ISagaState State => _state;

        public void LoadState(ISagaState state)
        {
            _state = state as LevelUpSagaState
                     ?? throw new ArgumentException("Неверный тип состояния саги", nameof(state));
        }

        public Task Complete(bool success, string? reason = null, CancellationToken cancellationToken = default)
        {
            _state.Status = success ? SagaStatus.Completed : SagaStatus.Failed;
            _state.CompletionReason = reason;
            _state.UpdatedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        // --------------------------------------------------------------------------------------------
        // Таблица порогов опыта (опыт, необходимый для достижения уровня)
        // --------------------------------------------------------------------------------------------
        private static readonly Dictionary<int, int> ExperienceThresholds = new()
        {
            {1, 0}, {2, 300}, {3, 900}, {4, 2700}, {5, 6500}, {6, 14000}, {7, 23000},
            {8, 34000}, {9, 48000}, {10, 64000}, {11, 85000}, {12, 100000}, {13, 120000},
            {14, 140000}, {15, 165000}, {16, 195000}, {17, 225000}, {18, 265000},
            {19, 305000}, {20, 355000}
        };

        // --------------------------------------------------------------------------------------------
        // Определение кости хитов по классу
        // --------------------------------------------------------------------------------------------
        private static int GetHitDieByClass(string? className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return 8; // значение по умолчанию

            return className.ToLowerInvariant() switch
            {
                "barbarian" or "fighter" or "paladin" or "ranger" => 10,
                "bard" or "cleric" or "druid" or "monk" or "rogue" or "warlock" => 8,
                "sorcerer" or "wizard" => 6,
                _ => 8
            };
        }

        // --------------------------------------------------------------------------------------------
        // Получение ячеек заклинаний в зависимости от класса
        // --------------------------------------------------------------------------------------------
        private static Dictionary<int, int>? GetSpellSlotsByClass(string? className, int level)
        {
            if (string.IsNullOrWhiteSpace(className))
                return null;

            var lower = className.ToLowerInvariant();

            // Классы без заклинаний не получают ячеек
            if (lower is "barbarian" or "fighter" or "monk" or "rogue")
                return null;

            // Полузаклинатели (паладин, следопыт)
            if (lower is "paladin" or "ranger")
                return MagicRules.HalfCasterSpellSlots(level);

            // Полные заклинатели
            return MagicRules.FullCasterSpellSlots(level);
        }

        // --------------------------------------------------------------------------------------------
        // Обработка события получения опыта
        // --------------------------------------------------------------------------------------------
        public async Task Handle(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is ExperienceGained expGained)
            {
                await ProcessExperienceGain(expGained, cancellationToken);
            }
        }

        private async Task ProcessExperienceGain(ExperienceGained e, CancellationToken cancellationToken)
        {
            _state.Status = SagaStatus.InProgress;
            _state.UpdatedAt = DateTime.UtcNow;

            // Получаем актуальные данные о персонаже
            var character = await _characterProjection.GetById(e.CharacterId, cancellationToken);
            if (character == null)
            {
                await Complete(false, "Персонаж не найден", cancellationToken);
                return;
            }

            int currentLevel = character.Level;
            int currentXp = character.ExperiencePoints;

            // Определяем максимальный уровень, которого можно достичь
            int maxPossibleLevel = currentLevel;
            for (int level = currentLevel + 1; level <= 20; level++)
            {
                if (currentXp >= ExperienceThresholds[level])
                    maxPossibleLevel = level;
                else
                    break;
            }

            if (maxPossibleLevel <= currentLevel)
            {
                // Недостаточно опыта для повышения
                await Complete(true, "Недостаточно опыта для повышения уровня", cancellationToken);
                return;
            }

            // Применяем повышение уровня последовательно
            // ВАЖНО: избегаем повторных загрузок проекции в цикле, т.к. она может не обновиться мгновенно.
            // Вместо этого сохраняем текущие параметры и вычисляем все изменения заранее.
            var currentClass = character.Class;
            var currentConstitution = character.AbilityScores.GetValueOrDefault("Constitution", 10);
            int conModifier = (currentConstitution - 10) / 2;

            for (int newLevel = currentLevel + 1; newLevel <= maxPossibleLevel; newLevel++)
            {
                await ApplyLevelUpCore(
                    e.CharacterId,
                    newLevel,
                    currentClass,
                    conModifier,
                    cancellationToken);

                _state.LastAppliedLevel = newLevel;
            }

            await Complete(true, $"Повышение уровня применено до {_state.LastAppliedLevel}", cancellationToken);
        }

        private async Task ApplyLevelUpCore(
            Guid characterId,
            int newLevel,
            string className,
            int constitutionModifier,
            CancellationToken cancellationToken)
        {
            // Повышаем уровень — все бонусы теперь применяются внутри агрегата
            await _commandBus.SendAsync(
                new LevelUpCharacter(characterId, newLevel),
                new CommandContext { CancellationToken = cancellationToken });
        }

        // --------------------------------------------------------------------------------------------
        // Состояние саги
        // --------------------------------------------------------------------------------------------
        private class LevelUpSagaState : ISagaState
        {
            public Guid SagaId { get; set; }
            public Guid CorrelationId { get; set; }
            public SagaStatus Status { get; set; } = SagaStatus.Started;
            public int Version { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? UpdatedAt { get; set; }
            public int LastAppliedLevel { get; set; }
            public string? CompletionReason { get; set; }
        }
    }
}