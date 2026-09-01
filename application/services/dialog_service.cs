#nullable enable
using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.services
{
    /// <summary>Узел диалогового дерева.</summary>
    public class DialogueNode
    {
        public Guid NodeId { get; set; }
        public string NpcText { get; set; } = string.Empty;
        public List<DialogueOption> Options { get; set; } = new();
        public bool IsExitNode { get; set; }
    }

    /// <summary>Вариант ответа игрока.</summary>
    public class DialogueOption
    {
        public Guid OptionId { get; set; }
        public string PlayerText { get; set; } = string.Empty;
        public Guid? NextNodeId { get; set; }
        public List<DialogueCondition>? Conditions { get; set; }
        public DialogueCheck? SkillCheck { get; set; }
        public List<DialogueEffect>? SuccessEffects { get; set; }
        public List<DialogueEffect>? FailureEffects { get; set; }
    }

    /// <summary>Условие отображения варианта ответа.</summary>
    public class DialogueCondition
    {
        public string Type { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>Проверка навыка или характеристики во время диалога.</summary>
    public class DialogueCheck
    {
        public string SkillOrAbility { get; set; } = string.Empty;
        public int DifficultyClass { get; set; }
    }

    /// <summary>Эффект, выполняемый при выборе варианта.</summary>
    public class DialogueEffect
    {
        public string EffectType { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    /// <summary>Состояние диалога для сохранения.</summary>
    public class DialogueState
    {
        public Guid DialogueId { get; set; }
        public Guid NpcId { get; set; }
        public Guid CharacterId { get; set; }
        public Guid CurrentNodeId { get; set; }
        public bool IsActive { get; set; } = true;
        public List<Guid> VisitedNodeIds { get; set; } = new();
        public Guid? PendingOptionId { get; set; }
    }

    /// <summary>Репозиторий диалоговых деревьев.</summary>
    public interface IDialogueRepository
    {
        Task<DialogueNode?> GetRootNodeAsync(Guid dialogueId, CancellationToken cancellationToken = default);
        Task<DialogueNode?> GetNodeAsync(Guid dialogueId, Guid nodeId, CancellationToken cancellationToken = default);
        /// <summary>Добавляет узел диалога. Если isRoot=true или корневой узел ещё не назначен, делает его корневым.</summary>
        Task AddNodeAsync(Guid dialogueId, DialogueNode node, bool isRoot = false, CancellationToken cancellationToken = default);

        /// <summary>Устанавливает существующий узел корневым для диалога.</summary>
        Task SetRootNodeAsync(Guid dialogueId, Guid nodeId, CancellationToken cancellationToken = default);
    }

    /// <summary>Репозиторий состояний диалогов.</summary>
    public interface IDialogueStateRepository
    {
        Task<DialogueState?> GetAsync(Guid dialogueId, CancellationToken cancellationToken = default);
        Task SaveAsync(DialogueState state, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid dialogueId, CancellationToken cancellationToken = default);
    }

    /// <summary>InMemory-реализация репозитория состояний диалогов.</summary>
    public class InMemoryDialogueStateRepository : IDialogueStateRepository
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DialogueState> _states = new();

        public Task<DialogueState?> GetAsync(Guid dialogueId, CancellationToken cancellationToken)
        {
            _states.TryGetValue(dialogueId, out var state);
            return Task.FromResult(state);
        }

        public Task SaveAsync(DialogueState state, CancellationToken cancellationToken)
        {
            _states[state.DialogueId] = state;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid dialogueId, CancellationToken cancellationToken)
        {
            _states.TryRemove(dialogueId, out _);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Сервис управления диалогами. Содержит бизнес-логику проведения диалогов,
    /// проверки условий, применения эффектов и переходов между узлами.
    /// </summary>
    public class DialogService
    {
        private readonly ICommandBus _commandBus;
        private readonly IDialogueRepository _dialogueRepo;
        private readonly CharacterProjection _characterProjection;
        private readonly PermissionChecker _permissionChecker;
        private readonly CampaignProjection _campaignProjection;
        private readonly ICharacterOwnershipRepository _ownershipRepository;
        private readonly IDialogueStateRepository _stateRepository;
        private readonly ILogger<DialogService> _logger;

        public DialogService(
            ICommandBus commandBus,
            IDialogueRepository dialogueRepo,
            CharacterProjection characterProjection,
            PermissionChecker permissionChecker,
            CampaignProjection campaignProjection,
            ICharacterOwnershipRepository ownershipRepository,
            IDialogueStateRepository stateRepository,
            ILogger<DialogService>? logger = null)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _dialogueRepo = dialogueRepo ?? throw new ArgumentNullException(nameof(dialogueRepo));
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
            _campaignProjection = campaignProjection ?? throw new ArgumentNullException(nameof(campaignProjection));
            _ownershipRepository = ownershipRepository ?? throw new ArgumentNullException(nameof(ownershipRepository));
            _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
            _logger = logger ?? NullLogger<DialogService>.Instance;
        }

        // ==================== Публичные методы ====================

        /// <summary>Начинает диалог между персонажем и NPC.</summary>
        public async Task<DialogueState> StartDialogueAsync(
            Guid dialogueId,
            Guid npcId,
            Guid characterId,
            CancellationToken cancellationToken = default)
        {
            ValidateGuids(dialogueId, npcId, characterId);
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _permissionChecker.CanControlCharacterAsync(characterId, cancellationToken))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим персонажем.");

            var rootNode = await _dialogueRepo.GetRootNodeAsync(dialogueId, cancellationToken)
                           ?? throw new InvalidOperationException("Диалог не найден или не имеет корневого узла.");

            // Проверяем, не ведёт ли уже персонаж диалог с этим NPC
            var existing = await _stateRepository.GetAsync(dialogueId, cancellationToken);
            if (existing != null && existing.IsActive && existing.CharacterId == characterId && existing.NpcId == npcId)
                throw new InvalidOperationException("Персонаж уже участвует в диалоге с этим NPC.");

            var state = new DialogueState
            {
                DialogueId = dialogueId,
                NpcId = npcId,
                CharacterId = characterId,
                CurrentNodeId = rootNode.NodeId,
                IsActive = true
            };
            state.VisitedNodeIds.Add(rootNode.NodeId);

            await _stateRepository.SaveAsync(state, cancellationToken);
            _logger.LogInformation("Диалог {DialogueId} начат между персонажем {CharacterId} и NPC {NpcId}",
                dialogueId, characterId, npcId);

            await _commandBus.SendAsync(new StartDialogueCommand(dialogueId, npcId, characterId), cancellationToken);
            return state;
        }

        /// <summary>Выбирает вариант ответа в активном диалоге.</summary>
        public async Task<DialogueState> SelectOptionAsync(
            Guid dialogueId,
            Guid optionId,
            CancellationToken cancellationToken = default)
        {
            ValidateGuids(dialogueId, optionId);
            cancellationToken.ThrowIfCancellationRequested();

            var state = await GetActiveStateAsync(dialogueId, cancellationToken);
            if (!await _permissionChecker.CanControlCharacterAsync(state.CharacterId, cancellationToken))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим персонажем.");

            if (state.PendingOptionId.HasValue)
                throw new InvalidOperationException("Ожидается разрешение проверки навыка.");

            var currentNode = await _dialogueRepo.GetNodeAsync(state.DialogueId, state.CurrentNodeId, cancellationToken)
                              ?? throw new InvalidOperationException("Текущий узел диалога не найден.");

            var selectedOption = currentNode.Options.FirstOrDefault(o => o.OptionId == optionId)
                                 ?? throw new InvalidOperationException("Вариант ответа недоступен.");

            if (selectedOption.Conditions != null)
            {
                foreach (var condition in selectedOption.Conditions)
                {
                    if (!await EvaluateConditionAsync(state.CharacterId, condition, cancellationToken))
                        throw new InvalidOperationException("Условия для выбора этого варианта не выполнены.");
                }
            }

            if (selectedOption.SkillCheck != null)
            {
                state.PendingOptionId = optionId;
                await _stateRepository.SaveAsync(state, cancellationToken);
                _logger.LogDebug("Диалог {DialogueId}: ожидание проверки навыка для опции {OptionId}",
                    dialogueId, optionId);
                return state;
            }

            if (selectedOption.SuccessEffects != null)
                await ApplyEffectsAsync(state, selectedOption.SuccessEffects, cancellationToken);

            await TransitionAfterOptionAsync(state, selectedOption, cancellationToken);
            await _stateRepository.SaveAsync(state, cancellationToken);
            return state;
        }

        /// <summary>Разрешает проверку навыка в диалоге.</summary>
        public async Task<DialogueState> ResolveSkillCheckAsync(
            Guid dialogueId,
            int rollResult,
            int proficiencyBonus,
            int abilityModifier,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(dialogueId, nameof(dialogueId));
            cancellationToken.ThrowIfCancellationRequested();

            var state = await GetActiveStateAsync(dialogueId, cancellationToken);
            if (!state.PendingOptionId.HasValue)
                throw new InvalidOperationException("Нет ожидающей проверки навыка.");

            if (!await _permissionChecker.CanControlCharacterAsync(state.CharacterId, cancellationToken))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим персонажем.");

            var currentNode = await _dialogueRepo.GetNodeAsync(state.DialogueId, state.CurrentNodeId, cancellationToken)
                              ?? throw new InvalidOperationException("Текущий узел диалога не найден.");

            var option = currentNode.Options.FirstOrDefault(o => o.OptionId == state.PendingOptionId.Value)
                         ?? throw new InvalidOperationException("Вариант с ожидающей проверкой не найден.");

            if (option.SkillCheck == null)
                throw new InvalidOperationException("Этот вариант не требует проверки навыка.");

            int total = rollResult + proficiencyBonus + abilityModifier;
            bool success = total >= option.SkillCheck.DifficultyClass;

            var effects = success ? option.SuccessEffects : option.FailureEffects;
            if (effects != null)
                await ApplyEffectsAsync(state, effects, cancellationToken);

            state.PendingOptionId = null;
            await TransitionAfterOptionAsync(state, option, cancellationToken);
            await _stateRepository.SaveAsync(state, cancellationToken);
            return state;
        }

        /// <summary>Возвращает текущий узел диалога.</summary>
        public async Task<DialogueNode?> GetCurrentDialogueNodeAsync(
            Guid dialogueId,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(dialogueId, nameof(dialogueId));
            cancellationToken.ThrowIfCancellationRequested();

            var state = await GetActiveStateAsync(dialogueId, cancellationToken);
            if (!await _permissionChecker.CanViewCharacterAsync(state.CharacterId, cancellationToken))
                throw new UnauthorizedAccessException("У вас нет прав для просмотра этого диалога.");

            return await _dialogueRepo.GetNodeAsync(state.DialogueId, state.CurrentNodeId, cancellationToken);
        }

        /// <summary>Принудительно завершает диалог.</summary>
        public async Task EndDialogueAsync(
            Guid dialogueId,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(dialogueId, nameof(dialogueId));
            cancellationToken.ThrowIfCancellationRequested();

            var state = await GetActiveStateAsync(dialogueId, cancellationToken);
            if (!await _permissionChecker.CanControlCharacterAsync(state.CharacterId, cancellationToken))
                throw new UnauthorizedAccessException("У вас нет прав для управления этим персонажем.");

            await EndDialogueInternalAsync(state, cancellationToken);
        }

        // ==================== Приватные методы ====================

        private async Task<DialogueState> GetActiveStateAsync(Guid dialogueId, CancellationToken ct)
        {
            var state = await _stateRepository.GetAsync(dialogueId, ct)
                        ?? throw new InvalidOperationException("Активный диалог с указанным идентификатором не найден.");
            if (!state.IsActive)
                throw new InvalidOperationException("Диалог уже завершён.");
            return state;
        }

        private async Task TransitionAfterOptionAsync(
            DialogueState state,
            DialogueOption option,
            CancellationToken ct)
        {
            if (option.NextNodeId.HasValue)
            {
                state.CurrentNodeId = option.NextNodeId.Value;
                state.VisitedNodeIds.Add(option.NextNodeId.Value);

                var nextNode = await _dialogueRepo.GetNodeAsync(state.DialogueId, option.NextNodeId.Value, ct);
                if (nextNode != null && nextNode.IsExitNode)
                {
                    await EndDialogueInternalAsync(state, ct);
                }
            }
            else
            {
                await EndDialogueInternalAsync(state, ct);
            }
        }

        private async Task EndDialogueInternalAsync(DialogueState state, CancellationToken ct)
        {
            state.IsActive = false;
            await _stateRepository.DeleteAsync(state.DialogueId, ct);
            _logger.LogInformation("Диалог {DialogueId} завершён", state.DialogueId);
            await _commandBus.SendAsync(new EndDialogueCommand(state.DialogueId), ct);
        }

        private async Task ApplyEffectsAsync(
            DialogueState state,
            IEnumerable<DialogueEffect> effects,
            CancellationToken ct)
        {
            foreach (var effect in effects)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyEffectAsync(state, effect, ct);
            }
        }

        private async Task ApplyEffectAsync(DialogueState state, DialogueEffect effect, CancellationToken ct)
        {
            switch (effect.EffectType)
            {
                case "ChangeReputation":
                    {
                        var factionId = GetRequiredParameter(effect, "FactionId");
                        int delta = ParseIntParameter(effect, "Amount");
                        var campaignId = await _ownershipRepository.GetCampaignIdAsync(state.CharacterId, ct)
                                         ?? throw new InvalidOperationException("Не удалось определить кампанию персонажа.");
                        await _commandBus.SendAsync(new ChangeFactionReputationCommand(campaignId, factionId, delta), ct);
                        break;
                    }
                case "GiveItem":
                    {
                        string itemId = GetRequiredParameter(effect, "ItemId");
                        string itemName = effect.Parameters.TryGetValue("ItemName", out var name) ? name : itemId;
                        int quantity = ParseIntParameterOrDefault(effect, "Quantity", 1);
                        await _commandBus.SendAsync(new AddInventoryItem(state.CharacterId, itemId, itemName, quantity), ct);
                        break;
                    }
                case "RemoveItem":
                    {
                        string removeItemId = GetRequiredParameter(effect, "ItemId");
                        await _commandBus.SendAsync(new RemoveInventoryItem(state.CharacterId, removeItemId), ct);
                        break;
                    }
                case "StartQuest":
                    {
                        var questId = ParseGuidParameter(effect, "QuestId");
                        await _commandBus.SendAsync(new StartQuestCommand(state.CharacterId, questId), ct);
                        break;
                    }
                case "CompleteQuest":
                    {
                        var completeQuestId = ParseGuidParameter(effect, "QuestId");
                        await _commandBus.SendAsync(new CompleteQuestCommand(state.CharacterId, completeQuestId), ct);
                        break;
                    }
                case "SetFlag":
                    {
                        string flagName = GetRequiredParameter(effect, "Flag");
                        string flagValue = GetRequiredParameter(effect, "Value");
                        Guid campaignId = effect.Parameters.TryGetValue("CampaignId", out var cidStr) && Guid.TryParse(cidStr, out var cid)
                            ? cid
                            : (await _ownershipRepository.GetCampaignIdAsync(state.CharacterId, ct) ?? Guid.Empty);
                        if (campaignId == Guid.Empty)
                            throw new InvalidOperationException("Не удалось определить кампанию для установки флага.");
                        await _commandBus.SendAsync(new SetGlobalFlagCommand(campaignId, flagName, flagValue), ct);
                        break;
                    }
                case "StartCombat":
                    {
                        await _commandBus.SendAsync(new StartCombat(Guid.NewGuid(), new List<Guid> { state.CharacterId, state.NpcId }), ct);
                        break;
                    }
                case "Heal":
                    {
                        int healAmount = ParseIntParameter(effect, "Amount");
                        await _commandBus.SendAsync(new HealCharacter(state.CharacterId, healAmount), ct);
                        break;
                    }
                default:
                    _logger.LogWarning("Неизвестный тип эффекта диалога: {EffectType}", effect.EffectType);
                    break;
            }
        }

        private async Task<bool> EvaluateConditionAsync(Guid characterId, DialogueCondition condition, CancellationToken ct)
        {
            var character = await _characterProjection.GetById(characterId, ct);
            if (character == null) return false;

            switch (condition.Type)
            {
                case "HasItem":
                    return character.Inventory.Any(i => i.ItemId == condition.Parameter);
                case "MinLevel":
                    return int.TryParse(condition.Value, out var minLevel) && character.Level >= minLevel;
                case "QuestCompleted":
                    return await IsQuestCompletedAsync(characterId, condition, ct);
                case "ReputationAbove":
                    return await IsReputationAboveAsync(condition, ct);
                case "FlagSet":
                    return await IsFlagSetAsync(characterId, condition, ct);
                default:
                    _logger.LogWarning("Неизвестный тип условия диалога: {ConditionType}", condition.Type);
                    return false;
            }
        }

        private async Task<bool> IsQuestCompletedAsync(Guid characterId, DialogueCondition condition, CancellationToken ct)
        {
            if (!Guid.TryParse(condition.Parameter, out var questId)) return false;
            var campaignId = await _ownershipRepository.GetCampaignIdAsync(characterId, ct);
            if (!campaignId.HasValue) return false;

            var quests = await _campaignProjection.GetQuests(campaignId.Value, null, ct);
            var quest = quests.FirstOrDefault(q => q.QuestId == questId);
            return quest != null && quest.Status == QuestStatus.Completed;
        }

        private async Task<bool> IsReputationAboveAsync(DialogueCondition condition, CancellationToken ct)
        {
            var faction = await _campaignProjection.GetFaction(condition.Parameter, ct);
            if (faction == null) return false;
            if (!int.TryParse(condition.Value, out var threshold)) return false;
            return faction.Reputation >= threshold;
        }

        private async Task<bool> IsFlagSetAsync(Guid characterId, DialogueCondition condition, CancellationToken ct)
        {
            var campaignId = await _ownershipRepository.GetCampaignIdAsync(characterId, ct);
            if (!campaignId.HasValue) return false;

            var state = await _campaignProjection.GetCampaignState(campaignId.Value, ct);
            if (state == null) return false;

            return state.GlobalFlags.TryGetValue(condition.Parameter, out var flagValue)
                   && (string.IsNullOrEmpty(condition.Value) || flagValue == condition.Value);
        }

        private static string GetRequiredParameter(DialogueEffect effect, string key)
        {
            if (!effect.Parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"В эффекте {effect.EffectType} отсутствует обязательный параметр: {key}");
            return value;
        }

        private static int ParseIntParameter(DialogueEffect effect, string key)
        {
            var value = GetRequiredParameter(effect, key);
            if (!int.TryParse(value, out var result))
                throw new InvalidOperationException($"Параметр {key} в эффекте {effect.EffectType} должен быть целым числом.");
            return result;
        }

        private static int ParseIntParameterOrDefault(DialogueEffect effect, string key, int defaultValue)
        {
            return effect.Parameters.TryGetValue(key, out var value) && int.TryParse(value, out var result)
                ? result
                : defaultValue;
        }

        private static Guid ParseGuidParameter(DialogueEffect effect, string key)
        {
            var value = GetRequiredParameter(effect, key);
            if (!Guid.TryParse(value, out var guid))
                throw new InvalidOperationException($"Параметр {key} в эффекте {effect.EffectType} должен быть корректным GUID.");
            return guid;
        }

        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не должен быть пустым: {paramName}", paramName);
        }

        private static void ValidateGuids(params Guid[] ids)
        {
            foreach (var id in ids)
            {
                if (id == Guid.Empty)
                    throw new ArgumentException("Идентификатор не должен быть пустым.");
            }
        }
    }
}