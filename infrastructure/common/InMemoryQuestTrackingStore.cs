#nullable enable
using dnd_game.domain.interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.common
{
    /// <summary>
    /// Реализация <see cref="IQuestTrackingStore"/> в памяти.
    /// Обеспечивает потокобезопасное хранение связей между квестами, участниками, предметами и кампаниями.
    /// Используется для маршрутизации событий к соответствующим сагам.
    /// </summary>
    public class InMemoryQuestTrackingStore(ILogger<InMemoryQuestTrackingStore>? logger = null) : IQuestTrackingStore
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<Guid, HashSet<Guid>> _questParticipants = [];
        private readonly Dictionary<Guid, HashSet<Guid>> _participantQuests = [];
        private readonly Dictionary<Guid, HashSet<string>> _questRequiredItems = [];
        private readonly Dictionary<string, HashSet<Guid>> _itemQuests = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, Guid> _questCampaigns = [];
        private readonly ILogger<InMemoryQuestTrackingStore> _logger = logger ?? NullLogger<InMemoryQuestTrackingStore>.Instance;

        /// <inheritdoc />
        public Task AddParticipantAsync(Guid questId, Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(questId, nameof(questId));
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                if (!_questParticipants.TryGetValue(questId, out var participants))
                {
                    participants = [];
                    _questParticipants[questId] = participants;
                }
                participants.Add(characterId);

                if (!_participantQuests.TryGetValue(characterId, out var quests))
                {
                    quests = [];
                    _participantQuests[characterId] = quests;
                }
                quests.Add(questId);
            }

            _logger.LogDebug("Участник {CharacterId} добавлен к квесту {QuestId}", characterId, questId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<Guid>> GetQuestsForCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                if (_participantQuests.TryGetValue(characterId, out var quests))
                {
                    var snapshot = quests.ToList();
                    return Task.FromResult<IEnumerable<Guid>>(snapshot);
                }
                return Task.FromResult<IEnumerable<Guid>>([]);
            }
        }

        /// <inheritdoc />
        public Task<IEnumerable<Guid>> GetQuestsForItemAsync(string itemId, CancellationToken cancellationToken = default)
        {
            ValidateItemId(itemId, nameof(itemId));
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                if (_itemQuests.TryGetValue(itemId, out var quests))
                {
                    var snapshot = quests.ToList();
                    return Task.FromResult<IEnumerable<Guid>>(snapshot);
                }
                return Task.FromResult<IEnumerable<Guid>>([]);
            }
        }

        /// <inheritdoc />
        public Task RemoveQuestAsync(Guid questId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(questId, nameof(questId));
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                // Удаляем участников
                if (_questParticipants.Remove(questId, out var participants))
                {
                    foreach (var characterId in participants)
                    {
                        if (_participantQuests.TryGetValue(characterId, out var quests))
                        {
                            quests.Remove(questId);
                            if (quests.Count == 0)
                            {
                                _participantQuests.Remove(characterId);
                            }
                        }
                    }
                }

                // Удаляем требуемые предметы
                if (_questRequiredItems.Remove(questId, out var items))
                {
                    foreach (var itemId in items)
                    {
                        if (_itemQuests.TryGetValue(itemId, out var quests))
                        {
                            quests.Remove(questId);
                            if (quests.Count == 0)
                            {
                                _itemQuests.Remove(itemId);
                            }
                        }
                    }
                }

                _questCampaigns.Remove(questId);
            }

            _logger.LogDebug("Квест {QuestId} удалён из отслеживания", questId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AddRequiredItemAsync(Guid questId, string itemId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(questId, nameof(questId));
            ValidateItemId(itemId, nameof(itemId));
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                if (!_questRequiredItems.TryGetValue(questId, out var items))
                {
                    items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _questRequiredItems[questId] = items;
                }
                items.Add(itemId);

                if (!_itemQuests.TryGetValue(itemId, out var quests))
                {
                    quests = [];
                    _itemQuests[itemId] = quests;
                }
                quests.Add(questId);
            }

            _logger.LogDebug("Предмет {ItemId} отмечен как требуемый для квеста {QuestId}", itemId, questId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetCampaignAsync(Guid questId, Guid campaignId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(questId, nameof(questId));
            ValidateGuid(campaignId, nameof(campaignId));
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                _questCampaigns[questId] = campaignId;
            }

            _logger.LogDebug("Квест {QuestId} привязан к кампании {CampaignId}", questId, campaignId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<Guid?> GetCampaignAsync(Guid questId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(questId, nameof(questId));
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                return Task.FromResult(
                    _questCampaigns.TryGetValue(questId, out var campaignId)
                        ? campaignId
                        : (Guid?)null);
            }
        }

        // ---------- Вспомогательные методы ----------

        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не может быть пустым: {paramName}", paramName);
        }

        private static void ValidateItemId(string itemId, string paramName)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException($"Идентификатор предмета не может быть пустым: {paramName}", paramName);
        }
    }
}