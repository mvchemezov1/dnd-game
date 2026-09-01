#nullable enable
using dnd_game.application.event_handlers;
using dnd_game.application.security;
using dnd_game.application.services;
using dnd_game.domain.events;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.common
{
    /// <summary>
    /// Репозиторий владельцев персонажей. Хранит связи «персонаж → игрок»,
    /// «персонаж → кампания» и признак NPC.
    ///
    /// УСТАРЕЛО: с миграции 010_AddCharacterOwnership.sql в DI зарегистрирован
    /// <see cref="dnd_game.infrastructure.security.PostgresCharacterOwnershipRepository"/> —
    /// он же реализует <see cref="ICharacterOwnershipRepository"/>, но хранит
    /// данные в PostgreSQL, а не только в памяти процесса. Этот класс хранит
    /// связи только в памяти и теряет их при каждом перезапуске сервера — из-за
    /// этого у обычных игроков (не GM) список персонажей после рестарта
    /// становился пустым, хотя сами персонажи (event-sourced) никуда не
    /// девались. Оставлен для тестов/локальной разработки без БД.
    /// </summary>
    public class CharacterOwnershipRepository(ILogger<CharacterOwnershipRepository>? logger = null) : ICharacterOwnershipRepository
    {
        private readonly ConcurrentDictionary<Guid, Guid> _ownership = new();
        private readonly ConcurrentDictionary<Guid, Guid> _characterCampaigns = new();
        private readonly ConcurrentDictionary<Guid, bool> _npcCharacters = new();
        private readonly ILogger<CharacterOwnershipRepository> _logger = logger ?? NullLogger<CharacterOwnershipRepository>.Instance;

        public Task<Guid?> GetOwnerIdAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            _ownership.TryGetValue(characterId, out var ownerId);
            return Task.FromResult<Guid?>(ownerId == Guid.Empty ? null : ownerId);
        }

        public Task<Guid?> GetCampaignIdAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Guid?>(_characterCampaigns.TryGetValue(characterId, out var campaignId) ? campaignId : null);
        }

        public Task<bool> IsNonPlayerCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_npcCharacters.ContainsKey(characterId));
        }

        public Task<List<Guid>> GetOwnedCharacterIdsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(userId, nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();
            var result = _ownership
                .Where(kvp => kvp.Value == userId)
                .Select(kvp => kvp.Key)
                .ToList();
            return Task.FromResult(result);
        }

        public Task SetCampaignAsync(Guid characterId, Guid campaignId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateGuid(campaignId, nameof(campaignId));
            cancellationToken.ThrowIfCancellationRequested();
            _characterCampaigns[characterId] = campaignId;
            return Task.CompletedTask;
        }

        public Task MarkAsNpcAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            _npcCharacters[characterId] = true;
            return Task.CompletedTask;
        }

        // Вспомогательные методы управления (не входят в интерфейс)
        public Task AssignOwnerAsync(Guid characterId, Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateGuid(userId, nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();
            _ownership[characterId] = userId;
            _logger.LogDebug("Персонаж {CharacterId} привязан к игроку {UserId}", characterId, userId);
            return Task.CompletedTask;
        }

        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не может быть пустым: {paramName}", paramName);
        }
    }

    /// <summary>
    /// Хранилище событий для воспроизведения (replay) в памяти.
    /// </summary>
    public class InMemoryReplayEventStore(ILogger<InMemoryReplayEventStore>? logger = null) : IReplayEventStore
    {
        private readonly ConcurrentDictionary<Guid, List<IDomainEvent>> _byAggregate = new();
        private readonly ConcurrentDictionary<Guid, List<IDomainEvent>> _bySession = new();
        private readonly object _lock = new();
        private readonly ILogger<InMemoryReplayEventStore> _logger = logger ?? NullLogger<InMemoryReplayEventStore>.Instance;

        public Task AppendAsync(IDomainEvent @event, ReplayMetadata metadata, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(@event);
            ArgumentNullException.ThrowIfNull(metadata);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (@event is IAggregateEvent aggregateEvent)
                {
                    var list = _byAggregate.GetOrAdd(aggregateEvent.AggregateId, _ => []);
                    list.Add(@event);
                }

                var sessionList = _bySession.GetOrAdd(metadata.SessionId, _ => []);
                sessionList.Add(@event);
            }
            _logger.LogTrace("Событие {EventType} добавлено в replay-хранилище", @event.GetType().Name);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<IDomainEvent>> GetEventsAsync(Guid aggregateId, DateTime? toTimestamp = null, CancellationToken cancellationToken = default)
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));
            cancellationToken.ThrowIfCancellationRequested();

            if (!_byAggregate.TryGetValue(aggregateId, out var list))
                return Task.FromResult(Enumerable.Empty<IDomainEvent>());

            IEnumerable<IDomainEvent> result = list;
            if (toTimestamp.HasValue)
            {
                result = result.Where(e => e is not ITimestampedEvent timestamped || timestamped.OccurredOn <= toTimestamp.Value);
            }

            return Task.FromResult(result.ToList().AsEnumerable());
        }

        public Task<IEnumerable<IDomainEvent>> GetEventsBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Идентификатор сессии не может быть пустым.", nameof(sessionId));
            cancellationToken.ThrowIfCancellationRequested();

            if (!_bySession.TryGetValue(sessionId, out var list))
                return Task.FromResult(Enumerable.Empty<IDomainEvent>());
            return Task.FromResult(list.ToList().AsEnumerable());
        }

        public Task<long> GetEventCountAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));
            cancellationToken.ThrowIfCancellationRequested();

            var count = _byAggregate.TryGetValue(aggregateId, out var list) ? list.Count : 0;
            return Task.FromResult((long)count);
        }

        public Task<IDomainEvent?> GetLastEventAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));
            cancellationToken.ThrowIfCancellationRequested();

            if (!_byAggregate.TryGetValue(aggregateId, out var list) || list.Count == 0)
                return Task.FromResult<IDomainEvent?>(null);
            return Task.FromResult<IDomainEvent?>(list[^1]);
        }
    }

    /// <summary>
    /// Поставщик текущей игровой сессии по умолчанию. В реальном приложении должен
    /// извлекать сессию из контекста HTTP или другого источника.
    /// </summary>
    public class DefaultCurrentSessionProvider : ICurrentSessionProvider
    {
        public Guid GetCurrentSessionId() => Guid.Empty;
    }

    /// <summary>
    /// Построитель текстовых описаний событий по умолчанию. Возвращает имя типа события.
    /// </summary>
    public class DefaultNarrativeLogBuilder : INarrativeLogBuilder
    {
        public string BuildEntry(IDomainEvent @event) => @event.GetType().Name;
    }

    /// <summary>
    /// Репозиторий определений триггеров в памяти.
    /// </summary>
    public class InMemoryTriggerDefinitionRepository(ILogger<InMemoryTriggerDefinitionRepository>? logger = null) : ITriggerDefinitionRepository
    {
        private readonly ConcurrentDictionary<string, List<TriggerDefinition>> _byEvent = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<InMemoryTriggerDefinitionRepository> _logger = logger ?? NullLogger<InMemoryTriggerDefinitionRepository>.Instance;

        public Task<IEnumerable<TriggerDefinition>> GetByEventAsync(string eventName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentException("Имя события не может быть пустым.", nameof(eventName));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _byEvent.TryGetValue(eventName, out var list)
                    ? list.AsEnumerable()
                    : []);
        }

        public Task AddAsync(TriggerDefinition trigger, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(trigger);
            cancellationToken.ThrowIfCancellationRequested();

            var list = _byEvent.GetOrAdd(trigger.EventName, _ => []);
            list.Add(trigger);
            _logger.LogDebug("Триггер {TriggerId} добавлен для события {EventName}", trigger.TriggerId, trigger.EventName);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Репозиторий подписок на webhook'и в памяти.
    /// </summary>
    public class InMemoryWebhookSubscriptionRepository(ILogger<InMemoryWebhookSubscriptionRepository>? logger = null) : IWebhookSubscriptionRepository
    {
        private readonly ConcurrentDictionary<Guid, WebhookSubscription> _subscriptions = new();
        private readonly ILogger<InMemoryWebhookSubscriptionRepository> _logger = logger ?? NullLogger<InMemoryWebhookSubscriptionRepository>.Instance;

        public Task<IEnumerable<WebhookSubscription>> GetSubscriptionsForEventAsync(string eventType, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Тип события не может быть пустым.", nameof(eventType));
            cancellationToken.ThrowIfCancellationRequested();

            var result = _subscriptions.Values
                .Where(s => s.IsActive && (s.EventType == eventType || s.EventType == "*"))
                .ToList();
            return Task.FromResult<IEnumerable<WebhookSubscription>>(result);
        }

        public Task AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            if (subscription.Id == Guid.Empty)
                throw new ArgumentException("Идентификатор подписки не может быть пустым.", nameof(subscription));
            cancellationToken.ThrowIfCancellationRequested();

            _subscriptions[subscription.Id] = subscription;
            _logger.LogDebug("Подписка {SubscriptionId} добавлена", subscription.Id);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<WebhookSubscription>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_subscriptions.Values.AsEnumerable());
        }
    }

    /// <summary>
    /// Репозиторий состояний саг в памяти.
    /// </summary>
    public class InMemorySagaStateRepository(ILogger<InMemorySagaStateRepository>? logger = null) : ISagaStateRepository
    {
        private readonly ConcurrentDictionary<Guid, ISagaState> _states = new();
        private readonly ILogger<InMemorySagaStateRepository> _logger = logger ?? NullLogger<InMemorySagaStateRepository>.Instance;
        private readonly object _lock = new();

        public Task<ISagaState?> LoadAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Идентификатор саги не может быть пустым.", nameof(id));
            cancellationToken.ThrowIfCancellationRequested();

            _states.TryGetValue(id, out var state);
            return Task.FromResult(state);
        }

        public Task SaveAsync(ISagaState state, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(state);
            cancellationToken.ThrowIfCancellationRequested();

            _states[state.SagaId] = state;
            _logger.LogDebug("Состояние саги {SagaId} сохранено", state.SagaId);
            return Task.CompletedTask;
        }

        public Task<bool> TrySaveAsync(ISagaState state, int expectedVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_states.TryGetValue(state.SagaId, out var existing))
                {
                    if (existing.Version != expectedVersion)
                        return Task.FromResult(false);
                }
                else
                {
                    // Если состояния нет, а expectedVersion != 0, то конфликт
                    if (expectedVersion != 0)
                        return Task.FromResult(false);
                }

                _states[state.SagaId] = state;
                return Task.FromResult(true);
            }
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Идентификатор саги не может быть пустым.", nameof(id));
            cancellationToken.ThrowIfCancellationRequested();

            _states.TryRemove(id, out _);
            _logger.LogDebug("Состояние саги {SagaId} удалено", id);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Репозиторий рецептов крафта в памяти.
    /// </summary>
    public class InMemoryRecipeRepository(ILogger<InMemoryRecipeRepository>? logger = null) : IRecipeRepository
    {
        private readonly ConcurrentDictionary<Guid, CraftingRecipe> _recipes = new();
        private readonly ILogger<InMemoryRecipeRepository> _logger = logger ?? NullLogger<InMemoryRecipeRepository>.Instance;

        public Task<CraftingRecipe?> GetByIdAsync(Guid recipeId, CancellationToken cancellationToken = default)
        {
            if (recipeId == Guid.Empty)
                throw new ArgumentException("Идентификатор рецепта не может быть пустым.", nameof(recipeId));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_recipes.TryGetValue(recipeId, out var r) ? r : null);
        }

        public Task<List<CraftingRecipe>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_recipes.Values.ToList());
        }

        public Task<List<CraftingRecipe>> GetByToolAsync(string toolName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Название инструмента не может быть пустым.", nameof(toolName));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_recipes.Values.Where(r => r.RequiredTool == toolName).ToList());
        }

        public Task<List<CraftingRecipe>> GetBySpellAsync(string spellId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(spellId))
                throw new ArgumentException("Идентификатор заклинания не может быть пустым.", nameof(spellId));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_recipes.Values.Where(r => r.RequiredSpellId == spellId).ToList());
        }

        // Дополнительный метод для добавления рецепта (не входит в интерфейс)
        public Task AddAsync(CraftingRecipe recipe, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            if (recipe.RecipeId == Guid.Empty)
                throw new ArgumentException("Идентификатор рецепта не может быть пустым.", nameof(recipe));
            cancellationToken.ThrowIfCancellationRequested();

            _recipes[recipe.RecipeId] = recipe;
            _logger.LogDebug("Рецепт {RecipeId} добавлен", recipe.RecipeId);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Репозиторий диалогов в памяти.
    /// </summary>
    public class InMemoryDialogueRepository : IDialogueRepository
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, DialogueNode>> _dialogues = new();
        private readonly ConcurrentDictionary<Guid, Guid> _rootNodeIds = new();
        private readonly ILogger<InMemoryDialogueRepository> _logger;

        public InMemoryDialogueRepository(ILogger<InMemoryDialogueRepository>? logger = null)
        {
            _logger = logger ?? NullLogger<InMemoryDialogueRepository>.Instance;
        }

        public Task<DialogueNode?> GetRootNodeAsync(Guid dialogueId, CancellationToken cancellationToken = default)
        {
            ValidateDialogueId(dialogueId);
            cancellationToken.ThrowIfCancellationRequested();

            if (!_dialogues.TryGetValue(dialogueId, out var nodes))
                return Task.FromResult<DialogueNode?>(null);
            if (!_rootNodeIds.TryGetValue(dialogueId, out var rootId))
                return Task.FromResult<DialogueNode?>(null);
            return Task.FromResult(nodes.TryGetValue(rootId, out var node) ? node : null);
        }

        public Task<DialogueNode?> GetNodeAsync(Guid dialogueId, Guid nodeId, CancellationToken cancellationToken = default)
        {
            ValidateDialogueId(dialogueId);
            if (nodeId == Guid.Empty)
                throw new ArgumentException("Идентификатор узла не может быть пустым.", nameof(nodeId));
            cancellationToken.ThrowIfCancellationRequested();

            if (!_dialogues.TryGetValue(dialogueId, out var nodes))
                return Task.FromResult<DialogueNode?>(null);
            return Task.FromResult(nodes.TryGetValue(nodeId, out var node) ? node : null);
        }

        public Task AddNodeAsync(Guid dialogueId, DialogueNode node, bool isRoot = false, CancellationToken cancellationToken = default)
        {
            ValidateDialogueId(dialogueId);
            ArgumentNullException.ThrowIfNull(node, nameof(node));
            if (node.NodeId == Guid.Empty)
                throw new ArgumentException("Идентификатор узла не может быть пустым.", nameof(node));
            cancellationToken.ThrowIfCancellationRequested();

            var nodes = _dialogues.GetOrAdd(dialogueId, _ => new ConcurrentDictionary<Guid, DialogueNode>());
            nodes[node.NodeId] = node;

            if (isRoot || !_rootNodeIds.ContainsKey(dialogueId))
            {
                _rootNodeIds[dialogueId] = node.NodeId;
            }

            _logger.LogDebug("Узел {NodeId} добавлен в диалог {DialogueId}", node.NodeId, dialogueId);
            return Task.CompletedTask;
        }

        public Task SetRootNodeAsync(Guid dialogueId, Guid nodeId, CancellationToken cancellationToken = default)
        {
            ValidateDialogueId(dialogueId);
            if (nodeId == Guid.Empty)
                throw new ArgumentException("Идентификатор узла не может быть пустым.", nameof(nodeId));
            cancellationToken.ThrowIfCancellationRequested();

            if (!_dialogues.TryGetValue(dialogueId, out var nodes) || !nodes.ContainsKey(nodeId))
                throw new InvalidOperationException($"Узел {nodeId} не найден в диалоге {dialogueId}.");

            _rootNodeIds[dialogueId] = nodeId;
            _logger.LogDebug("Корневой узел диалога {DialogueId} установлен: {NodeId}", dialogueId, nodeId);
            return Task.CompletedTask;
        }

        private static void ValidateDialogueId(Guid dialogueId)
        {
            if (dialogueId == Guid.Empty)
                throw new ArgumentException("Идентификатор диалога не может быть пустым.", nameof(dialogueId));
        }
    }

    /// <summary>
    /// Репозиторий скриптов ИИ в памяти.
    /// </summary>
    public class InMemoryScriptRepository(ILogger<InMemoryScriptRepository>? logger = null) : IScriptRepository
    {
        private readonly ConcurrentDictionary<string, ScriptDefinition> _scripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<InMemoryScriptRepository> _logger = logger ?? NullLogger<InMemoryScriptRepository>.Instance;

        public Task<ScriptDefinition?> GetByNameAsync(string scriptName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
                throw new ArgumentException("Имя скрипта не может быть пустым.", nameof(scriptName));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_scripts.TryGetValue(scriptName, out var s) ? s : null);
        }

        public Task AddOrUpdateAsync(ScriptDefinition script, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (string.IsNullOrWhiteSpace(script.ScriptName))
                throw new ArgumentException("Имя скрипта не может быть пустым.", nameof(script));
            cancellationToken.ThrowIfCancellationRequested();

            _scripts[script.ScriptName] = script;
            _logger.LogDebug("Скрипт {ScriptName} сохранён", script.ScriptName);
            return Task.CompletedTask;
        }

        public Task<List<string>> GetAllScriptNamesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_scripts.Keys.ToList());
        }
    }

    /// <summary>
    /// Репозиторий активных процессов крафта в памяти.
    /// </summary>
    public class InMemoryCraftingProcessRepository(ILogger<InMemoryCraftingProcessRepository>? logger = null) : ICraftingProcessRepository
    {
        private readonly ConcurrentDictionary<Guid, ActiveCraftingProcess> _processes = new();
        private readonly ILogger<InMemoryCraftingProcessRepository> _logger = logger ?? NullLogger<InMemoryCraftingProcessRepository>.Instance;

        public Task<List<ActiveCraftingProcess>> GetActiveForCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateCharacterId(characterId);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_processes.Values.Where(p => p.CharacterId == characterId).ToList());
        }

        public Task<ActiveCraftingProcess?> GetByIdAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            if (processId == Guid.Empty)
                throw new ArgumentException("Идентификатор процесса не может быть пустым.", nameof(processId));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_processes.TryGetValue(processId, out var p) ? p : null);
        }

        public Task AddAsync(ActiveCraftingProcess process, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(process);
            if (process.ProcessId == Guid.Empty)
                throw new ArgumentException("Идентификатор процесса не может быть пустым.", nameof(process));
            cancellationToken.ThrowIfCancellationRequested();

            _processes[process.ProcessId] = process;
            _logger.LogDebug("Процесс крафта {ProcessId} добавлен", process.ProcessId);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            if (processId == Guid.Empty)
                throw new ArgumentException("Идентификатор процесса не может быть пустым.", nameof(processId));
            cancellationToken.ThrowIfCancellationRequested();

            _processes.TryRemove(processId, out _);
            _logger.LogDebug("Процесс крафта {ProcessId} удалён", processId);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ActiveCraftingProcess process, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(process);
            if (process.ProcessId == Guid.Empty)
                throw new ArgumentException("Идентификатор процесса не может быть пустым.", nameof(process));
            cancellationToken.ThrowIfCancellationRequested();

            _processes[process.ProcessId] = process;
            _logger.LogDebug("Процесс крафта {ProcessId} обновлён", process.ProcessId);
            return Task.CompletedTask;
        }

        private static void ValidateCharacterId(Guid characterId)
        {
            if (characterId == Guid.Empty)
                throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(characterId));
        }
    }

    /// <summary>
    /// Репозиторий торговых предложений в памяти.
    /// </summary>
    public class InMemoryTradeOfferRepository(ILogger<InMemoryTradeOfferRepository>? logger = null) : ITradeOfferRepository
    {
        private readonly ConcurrentDictionary<Guid, TradeOffer> _offers = new();
        private readonly ILogger<InMemoryTradeOfferRepository> _logger = logger ?? NullLogger<InMemoryTradeOfferRepository>.Instance;

        public Task AddAsync(TradeOffer offer, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(offer);
            if (offer.OfferId == Guid.Empty)
                throw new ArgumentException("Идентификатор предложения не может быть пустым.", nameof(offer));
            cancellationToken.ThrowIfCancellationRequested();

            _offers[offer.OfferId] = offer;
            _logger.LogDebug("Торговое предложение {OfferId} добавлено", offer.OfferId);
            return Task.CompletedTask;
        }

        public Task<TradeOffer?> GetByIdAsync(Guid offerId, CancellationToken cancellationToken = default)
        {
            if (offerId == Guid.Empty)
                throw new ArgumentException("Идентификатор предложения не может быть пустым.", nameof(offerId));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_offers.TryGetValue(offerId, out var o) ? o : null);
        }

        public Task UpdateAsync(TradeOffer offer, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(offer);
            if (offer.OfferId == Guid.Empty)
                throw new ArgumentException("Идентификатор предложения не может быть пустым.", nameof(offer));
            cancellationToken.ThrowIfCancellationRequested();

            _offers[offer.OfferId] = offer;
            _logger.LogDebug("Торговое предложение {OfferId} обновлено", offer.OfferId);
            return Task.CompletedTask;
        }

        public Task<List<TradeOffer>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_offers.Values.ToList());
        }

        public Task RemoveAsync(Guid offerId, CancellationToken cancellationToken = default)
        {
            if (offerId == Guid.Empty)
                throw new ArgumentException("Идентификатор предложения не может быть пустым.", nameof(offerId));
            cancellationToken.ThrowIfCancellationRequested();

            _offers.TryRemove(offerId, out _);
            _logger.LogDebug("Торговое предложение {OfferId} удалено", offerId);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Репозиторий торговых данных NPC в памяти.
    /// </summary>
    public class InMemoryTradeRepository(ILogger<InMemoryTradeRepository>? logger = null) : ITradeRepository
    {
        private readonly ConcurrentDictionary<string, TradeItem> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<InMemoryTradeRepository> _logger = logger ?? NullLogger<InMemoryTradeRepository>.Instance;

        public Task<TradeItem?> GetItemInfoAsync(string itemId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            cancellationToken.ThrowIfCancellationRequested();

            if (_items.TryGetValue(itemId, out var item))
                return Task.FromResult<TradeItem?>(item);

            // Заглушка: возвращаем предмет по умолчанию
            var defaultItem = new TradeItem();
            return Task.FromResult<TradeItem?>(defaultItem);
        }

        public Task<float> GetBuyMultiplierAsync(Guid npcId, Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(npcId, nameof(npcId));
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(1.0f);
        }

        public Task<float> GetSellMultiplierAsync(Guid npcId, Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(npcId, nameof(npcId));
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(0.5f);
        }

        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не может быть пустым: {paramName}", paramName);
        }
    }
}