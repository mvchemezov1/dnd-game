#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.presentation.dm_tools
{
    public class OverrideCommands
    {
        private readonly ICommandBus _commandBus;
        private readonly CharacterProjection _characterProjection;
        private readonly PermissionChecker _permissionChecker;

        public OverrideCommands(
            ICommandBus commandBus,
            CharacterProjection characterProjection,
            PermissionChecker permissionChecker)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        }

        private async Task EnsureGameMasterAccessAsync(CancellationToken ct)
        {
            if (!await _permissionChecker.IsGameMasterAsync(ct))
                throw new UnauthorizedAccessException("Только Мастер или Администратор может выполнить это действие.");
        }

        public async Task ForceKillAsync(Guid characterId, CancellationToken ct = default)
        {
            await EnsureGameMasterAccessAsync(ct);
            ValidateCharacterId(characterId);
            await _commandBus.SendAsync(new MarkCharacterDead(characterId), ct);
        }

        public async Task ReviveCharacterAsync(Guid characterId, int newHitPoints = 1, CancellationToken ct = default)
        {
            await EnsureGameMasterAccessAsync(ct);
            ValidateCharacterId(characterId);
            if (newHitPoints <= 0) throw new ArgumentOutOfRangeException(nameof(newHitPoints));
            await _commandBus.SendAsync(new ReviveCharacter(characterId, newHitPoints), ct);
        }

        public async Task GrantItemAsync(Guid characterId, string itemId, string itemName, int quantity = 1, CancellationToken ct = default)
        {
            await EnsureGameMasterAccessAsync(ct);
            ValidateCharacterId(characterId);
            ValidateItemId(itemId);
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("отсутствует название предмета");
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            await _commandBus.SendAsync(new AddInventoryItem(characterId, itemId, itemName, quantity), ct);
        }

        // --------------------------------------------------------------------------------
        // Жизнь и смерть персонажа
        // --------------------------------------------------------------------------------

        /// <summary>Удаляет предмет у персонажа.</summary>
        public async Task RemoveItemAsync(Guid characterId, string itemId, int quantity = 1, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            ValidateItemId(itemId);
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Количество должно быть положительным.");
            await _commandBus.SendAsync(new RemoveInventoryItem(characterId, itemId, quantity), ct);
        }

        /// <summary>Добавляет золото персонажу.</summary>
        public async Task GrantGoldAsync(Guid characterId, int amount, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Сумма должна быть положительной.");
            await _commandBus.SendAsync(new AddGold(characterId, amount), ct);
        }

        /// <summary>Устанавливает точное количество золота.</summary>
        public async Task SetGoldAsync(Guid characterId, int amount, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Количество золота не может быть отрицательным.");
            await _commandBus.SendAsync(new SetGoldCommand(characterId, amount), ct);
        }

        // --------------------------------------------------------------------------------
        // Характеристики и уровень
        // --------------------------------------------------------------------------------

        /// <summary>Устанавливает значение характеристики.</summary>
        public async Task SetAbilityScoreAsync(Guid characterId, string ability, int score, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            if (string.IsNullOrWhiteSpace(ability)) throw new ArgumentException("Название характеристики не может быть пустым.", nameof(ability));
            if (score < 1 || score > 30) throw new ArgumentOutOfRangeException(nameof(score), "Значение характеристики должно быть от 1 до 30.");
            await _commandBus.SendAsync(new SetAbilityScore(characterId, ability, score), ct);
        }

        /// <summary>
        /// Повышает уровень персонажа. Понижение не поддерживается.
        /// </summary>
        public async Task SetLevelAsync(Guid characterId, int newLevel, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            var character = await _characterProjection.GetById(characterId, ct)
                            ?? throw new InvalidOperationException("Персонаж не найден.");

            if (newLevel < character.Level)
                throw new InvalidOperationException($"Нельзя понизить уровень (текущий: {character.Level}, запрошенный: {newLevel}).");
            if (newLevel == character.Level)
                return; // уже на нужном уровне
            if (newLevel > 20)
                throw new ArgumentOutOfRangeException(nameof(newLevel), "Максимальный уровень — 20.");

            await _commandBus.SendAsync(new LevelUpCharacter(characterId, newLevel), ct);
        }

        /// <summary>Добавляет опыт персонажу.</summary>
        public async Task GrantExperienceAsync(Guid characterId, int amount, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Количество опыта должно быть положительным.");
            await _commandBus.SendAsync(new GainExperience(characterId, amount), ct);
        }

        // --------------------------------------------------------------------------------
        // Состояния
        // --------------------------------------------------------------------------------

        /// <summary>Накладывает состояние.</summary>
        public async Task ApplyConditionAsync(Guid characterId, string condition, int durationRounds = 1, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            if (string.IsNullOrWhiteSpace(condition)) throw new ArgumentException("Название состояния не может быть пустым.", nameof(condition));
            if (durationRounds <= 0) throw new ArgumentOutOfRangeException(nameof(durationRounds), "Длительность должна быть положительной.");
            await _commandBus.SendAsync(new ApplyCondition(characterId, condition, durationRounds), ct);
        }

        /// <summary>Снимает состояние.</summary>
        public async Task RemoveConditionAsync(Guid characterId, string condition, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            if (string.IsNullOrWhiteSpace(condition)) throw new ArgumentException("Название состояния не может быть пустым.", nameof(condition));
            await _commandBus.SendAsync(new RemoveCondition(characterId, condition), ct);
        }

        /// <summary>Снимает все состояния. Если состояний нет, ничего не делает.</summary>
        public async Task ClearAllConditionsAsync(Guid characterId, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            try
            {
                await _commandBus.SendAsync(new ClearAllConditionsCommand(characterId), ct);
            }
            catch (InvalidOperationException)
            {
                // Нет состояний — игнорируем
            }
        }

        // --------------------------------------------------------------------------------
        // Перемещение и телепортация
        // --------------------------------------------------------------------------------

        /// <summary>Телепортирует персонажа в указанные координаты.</summary>
        public async Task TeleportCharacterAsync(Guid characterId, int x, int y, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            await _commandBus.SendAsync(new TeleportCommand(characterId, x, y), ct);
        }

        /// <summary>Перемещает персонажа обычным способом.</summary>
        public async Task MoveCharacterAsync(Guid characterId, int x, int y, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);
            await _commandBus.SendAsync(new MoveCharacter(characterId, x, y), ct);
        }

        // --------------------------------------------------------------------------------
        // Бой
        // --------------------------------------------------------------------------------

        /// <summary>Начинает бой.</summary>
        public async Task StartCombatAsync(Guid combatId, List<Guid> participants, CancellationToken ct = default)
        {
            if (combatId == Guid.Empty) throw new ArgumentException("Идентификатор боя не может быть пустым.", nameof(combatId));
            if (participants == null || participants.Count < 2) throw new ArgumentException("Для боя нужно минимум два участника.", nameof(participants));
            await _commandBus.SendAsync(new StartCombat(combatId, participants), ct);
        }

        /// <summary>Завершает бой.</summary>
        public async Task EndCombatAsync(Guid combatId, CancellationToken ct = default)
        {
            if (combatId == Guid.Empty) throw new ArgumentException("Идентификатор боя не может быть пустым.", nameof(combatId));
            await _commandBus.SendAsync(new EndCombat(combatId), ct);
        }

        /// <summary>Добавляет участника в бой.</summary>
        public async Task AddToCombatAsync(Guid combatId, Guid participantId, int initiative, CancellationToken ct = default)
        {
            if (combatId == Guid.Empty) throw new ArgumentException("Идентификатор боя не может быть пустым.", nameof(combatId));
            if (participantId == Guid.Empty) throw new ArgumentException("Идентификатор участника не может быть пустым.", nameof(participantId));
            await _commandBus.SendAsync(new AddParticipantToCombat(combatId, participantId, initiative), ct);
        }

        /// <summary>Удаляет участника из боя.</summary>
        public async Task RemoveFromCombatAsync(Guid combatId, Guid participantId, CancellationToken ct = default)
        {
            if (combatId == Guid.Empty) throw new ArgumentException("Идентификатор боя не может быть пустым.", nameof(combatId));
            if (participantId == Guid.Empty) throw new ArgumentException("Идентификатор участника не может быть пустым.", nameof(participantId));
            await _commandBus.SendAsync(new RemoveParticipantFromCombat(combatId, participantId), ct);
        }

        // --------------------------------------------------------------------------------
        // Кампания и глобальные флаги
        // --------------------------------------------------------------------------------

        /// <summary>Устанавливает глобальный флаг кампании.</summary>
        public async Task SetGlobalFlagAsync(Guid campaignId, string flagName, string value, CancellationToken ct = default)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (string.IsNullOrWhiteSpace(flagName)) throw new ArgumentException("Имя флага не может быть пустым.", nameof(flagName));
            await _commandBus.SendAsync(new SetGlobalFlagCommand(campaignId, flagName, value), ct);
        }

        /// <summary>Удаляет глобальный флаг.</summary>
        public async Task RemoveGlobalFlagAsync(Guid campaignId, string flagName, CancellationToken ct = default)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (string.IsNullOrWhiteSpace(flagName)) throw new ArgumentException("Имя флага не может быть пустым.", nameof(flagName));
            await _commandBus.SendAsync(new RemoveGlobalFlagCommand(campaignId, flagName), ct);
        }

        /// <summary>Изменяет репутацию фракции.</summary>
        public async Task ChangeFactionReputationAsync(Guid campaignId, string factionId, int change, CancellationToken ct = default)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (string.IsNullOrWhiteSpace(factionId)) throw new ArgumentException("Идентификатор фракции не может быть пустым.", nameof(factionId));
            await _commandBus.SendAsync(new ChangeFactionReputationCommand(campaignId, factionId, change), ct);
        }

        /// <summary>Завершает квест успешно.</summary>
        public async Task CompleteQuestAsync(Guid campaignId, Guid questId, CancellationToken ct = default)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (questId == Guid.Empty) throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));
            await _commandBus.SendAsync(new CompleteQuestCommand(campaignId, questId), ct);
        }

        /// <summary>Проваливает квест.</summary>
        public async Task FailQuestAsync(Guid campaignId, Guid questId, CancellationToken ct = default)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (questId == Guid.Empty) throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));
            await _commandBus.SendAsync(new FailQuestCommand(campaignId, questId), ct);
        }

        // --------------------------------------------------------------------------------
        // Время и погода
        // --------------------------------------------------------------------------------

        /// <summary>Продвигает игровое время.</summary>
        public async Task AdvanceTimeAsync(Guid campaignId, int minutes, CancellationToken ct = default)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (minutes <= 0) throw new ArgumentOutOfRangeException(nameof(minutes), "Количество минут должно быть положительным.");
            await _commandBus.SendAsync(new AdvanceTimeCommand(campaignId, minutes), ct);
        }

        /// <summary>Изменяет погоду.</summary>
        public async Task ChangeWeatherAsync(Guid campaignId, string weather, CancellationToken ct = default)
        {
            if (campaignId == Guid.Empty) throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (string.IsNullOrWhiteSpace(weather)) throw new ArgumentException("Погода не может быть пустой.", nameof(weather));
            await _commandBus.SendAsync(new ChangeWeatherCommand(campaignId, weather), ct);
        }

        // --------------------------------------------------------------------------------
        // Спаун существ
        // --------------------------------------------------------------------------------

        /// <summary>Создаёт монстра/NPC в указанной точке.</summary>
        public async Task SpawnMonsterAsync(string templateId, int x, int y, string name = "", int maxHp = 10, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) throw new ArgumentException("Идентификатор шаблона не может быть пустым.", nameof(templateId));
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp), "Максимальные хиты должны быть положительными.");

            var characterId = Guid.NewGuid();
            await _commandBus.SendAsync(new CreateCharacter(characterId, string.IsNullOrEmpty(name) ? templateId : name, maxHp), ct);
            if (x != 0 || y != 0)
                await _commandBus.SendAsync(new MoveCharacter(characterId, x, y), ct);
        }

        // --------------------------------------------------------------------------------
        // Прочее
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Полностью восстанавливает персонажа: оживляет, снимает состояния, лечит до максимума.
        /// </summary>
        public async Task ResetCharacterAsync(Guid characterId, CancellationToken ct = default)
        {
            ValidateCharacterId(characterId);

            // Сначала оживляем, если мёртв (иначе лечение вызовет ошибку)
            await _commandBus.SendAsync(new ReviveCharacter(characterId, 1), ct);

            // Снимаем все состояния (игнорируем ошибку, если их нет)
            await ClearAllConditionsAsync(characterId, ct);

            // Восстанавливаем полное здоровье
            var character = await _characterProjection.GetById(characterId, ct);
            if (character != null)
            {
                await _commandBus.SendAsync(new HealCharacter(characterId, character.MaxHitPoints), ct);
            }
            else
            {
                // Запасной вариант, если персонаж не найден в проекции
                await _commandBus.SendAsync(new HealCharacter(characterId, 9999), ct);
            }
        }

        // --------------------------------------------------------------------------------
        // Валидация
        // --------------------------------------------------------------------------------

        private static void ValidateCharacterId(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
        }

        private static void ValidateItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
        }
    }
}