using dnd_game.domain.events;
using dnd_game.infrastructure.caching;
using dnd_game.infrastructure.event_store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.projections
{
    /// <summary>Детальная информация о квесте.</summary>
    public class QuestInfo
    {
        public Guid QuestId { get; set; }
        public Guid CampaignId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public QuestStatus Status { get; set; } = QuestStatus.Active;
        public List<QuestObjective> Objectives { get; set; } = [];
        public List<QuestReward> Rewards { get; set; } = [];
        public DateTime IssuedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public enum QuestStatus
    {
        Available,
        Active,
        Completed,
        Failed,
        OnHold
    }

    public class QuestObjective
    {
        public string Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public int CurrentProgress { get; set; }
        public int RequiredProgress { get; set; }
    }

    public class QuestReward
    {
        public string Description { get; set; } = string.Empty;
        public int ExperiencePoints { get; set; }
        public List<string> ItemIds { get; set; } = [];
        public int Gold { get; set; }
        public string? FactionReputationChange { get; set; }
    }

    /// <summary>Состояние фракции и отношение к партии.</summary>
    public class FactionState
    {
        public string FactionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Reputation { get; set; }
        public string Attitude => Reputation switch
        {
            <= -75 => "Враждебное",
            <= -25 => "Недружелюбное",
            < 25 => "Нейтральное",
            < 75 => "Дружелюбное",
            _ => "Союзное"
        };
    }

    /// <summary>Полное состояние кампании.</summary>
    public class CampaignState
    {
        public Guid CampaignId { get; set; }
        public string CampaignName { get; set; } = string.Empty;
        public int CurrentAct { get; set; } = 1;
        public int Day { get; set; } = 1;
        public int Hour { get; set; } = 8;
        public int Minute { get; set; }
        public string Weather { get; set; } = "Ясно";
        public List<string> DiscoveredRegions { get; set; } = [];
        public Dictionary<string, string> GlobalFlags { get; set; } = [];
    }

    public class CampaignProjection
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<Guid, List<QuestInfo>> _campaignQuests = [];
        private readonly Dictionary<Guid, CampaignState> _campaignStates = [];
        private readonly Dictionary<string, FactionState> _factions = [];
        private readonly Dictionary<Guid, List<string>> _activeWorldEvents = [];

        private readonly ICacheProvider _cache;
        private readonly TimeSpan _cacheTtl;
        private readonly ILogger<CampaignProjection> _logger;

        public CampaignProjection(ICacheProvider cache, TimeSpan? cacheTtl = null, ILogger<CampaignProjection>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(cache);
            _cache = cache;
            _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(10);
            _logger = logger ?? NullLogger<CampaignProjection>.Instance;
        }

        private void InvalidateCache(Guid campaignId)
        {
            _cache.RemoveSync($"campaign:{campaignId}");
            _cache.RemoveSync($"campaign:quests:{campaignId}");
            _cache.RemoveSync($"campaign:activeQuests:{campaignId}");
            _cache.RemoveSync($"campaign:worldEvents:{campaignId}");
        }

        private void InvalidateFactionCache(string? factionId = null)
        {
            try
            {
                if (factionId != null)
                    _cache.RemoveAsync($"campaign:faction:{factionId}").GetAwaiter().GetResult();
                _cache.RemoveAsync("campaign:factions:all").GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось инвалидировать кэш фракции(й)");
            }
        }

        public void Apply(QuestCreated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var quests = GetOrCreateQuestList(e.CampaignId);
                if (quests.Any(q => q.QuestId == e.QuestId))
                {
                    _logger.LogWarning("Квест {QuestId} уже существует в кампании {CampaignId}", e.QuestId, e.CampaignId);
                    return;
                }

                quests.Add(new QuestInfo
                {
                    QuestId = e.QuestId,
                    CampaignId = e.CampaignId,
                    Title = e.Title,
                    Description = e.Description,
                    Objectives = [.. e.Objectives.Select(o => new QuestObjective
                    {
                        Description = o.Description,
                        RequiredProgress = o.RequiredProgress
                    })],
                    Rewards = [.. e.Rewards.Select(r => new QuestReward
                    {
                        Description = r.Description,
                        ExperiencePoints = r.ExperiencePoints,
                        ItemIds = r.ItemIds ?? [],
                        Gold = r.Gold,
                        FactionReputationChange = r.FactionReputationChange
                    })],
                    IssuedAt = DateTime.UtcNow
                });
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(QuestAccepted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var quest = FindQuest(e.CampaignId, e.QuestId);
                if (quest != null)
                {
                    quest.Status = QuestStatus.Active;
                }
                else
                {
                    _logger.LogWarning("Квест {QuestId} не найден при принятии", e.QuestId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(QuestDeleted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_campaignQuests.TryGetValue(e.CampaignId, out var quests))
                {
                    quests.RemoveAll(q => q.QuestId == e.QuestId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(QuestCompleted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var quest = FindQuest(e.CampaignId, e.QuestId);
                if (quest != null)
                {
                    quest.Status = QuestStatus.Completed;
                    quest.CompletedAt = DateTime.UtcNow;
                }
                else
                {
                    _logger.LogWarning("Квест {QuestId} не найден при завершении", e.QuestId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(QuestFailed e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var quest = FindQuest(e.CampaignId, e.QuestId);
                if (quest != null)
                {
                    quest.Status = QuestStatus.Failed;
                }
                else
                {
                    _logger.LogWarning("Квест {QuestId} не найден при провале", e.QuestId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(QuestObjectiveUpdated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var quest = FindQuest(e.CampaignId, e.QuestId);
                var objective = quest?.Objectives.ElementAtOrDefault(e.ObjectiveIndex);
                if (objective != null)
                {
                    objective.IsCompleted = e.IsCompleted;
                    objective.CurrentProgress = e.CurrentProgress;
                }
                else
                {
                    _logger.LogWarning("Цель {Index} квеста {QuestId} не найдена", e.ObjectiveIndex, e.QuestId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(QuestRewardClaimed e)
        {
            ArgumentNullException.ThrowIfNull(e);
            _logger.LogDebug("Награда квеста {QuestId} получена", e.QuestId);
        }

        public async Task RebuildAsync(IEventStore eventStore, CancellationToken cancellationToken = default)
        {
            var allEvents = await eventStore.GetAllEvents();
            foreach (var e in allEvents)
            {
                if (e is IDomainEvent domainEvent)
                {
                    Apply(domainEvent);
                }
            }
            await _cache.RemoveAsync("campaign:all", cancellationToken);
        }

        public void Apply(FactionDiscovered e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (!_factions.ContainsKey(e.FactionId))
                {
                    _factions[e.FactionId] = new FactionState
                    {
                        FactionId = e.FactionId,
                        Name = e.FactionName,
                        Reputation = 0
                    };
                }
            }
            InvalidateFactionCache(e.FactionId);
        }

        public void Apply(FactionReputationChanged e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_factions.TryGetValue(e.FactionId, out var faction))
                {
                    faction.Reputation = Math.Clamp(faction.Reputation + e.Change, -100, 100);
                }
                else
                {
                    _logger.LogWarning("Фракция {FactionId} не найдена при изменении репутации", e.FactionId);
                }
            }
            InvalidateFactionCache(e.FactionId);
        }

        public void Apply(CampaignCreated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (!_campaignStates.ContainsKey(e.CampaignId))
                {
                    _campaignStates[e.CampaignId] = new CampaignState
                    {
                        CampaignId = e.CampaignId,
                        CampaignName = e.Name
                    };
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(GameTimeAdvanced e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_campaignStates.TryGetValue(e.CampaignId, out var state))
                {
                    int totalMinutes = state.Minute + e.Minutes;
                    state.Minute = totalMinutes % 60;
                    state.Hour += totalMinutes / 60;
                    while (state.Hour >= 24)
                    {
                        state.Hour -= 24;
                        state.Day++;
                    }
                }
                else
                {
                    _logger.LogWarning("Кампания {CampaignId} не найдена при продвижении времени", e.CampaignId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(WeatherChanged e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_campaignStates.TryGetValue(e.CampaignId, out var state))
                {
                    state.Weather = e.NewWeather;
                }
                else
                {
                    _logger.LogWarning("Кампания {CampaignId} не найдена при изменении погоды", e.CampaignId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(RegionDiscovered e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_campaignStates.TryGetValue(e.CampaignId, out var state))
                {
                    if (!state.DiscoveredRegions.Contains(e.RegionName))
                        state.DiscoveredRegions.Add(e.RegionName);
                }
                else
                {
                    _logger.LogWarning("Кампания {CampaignId} не найдена при открытии региона", e.CampaignId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(GlobalFlagSet e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_campaignStates.TryGetValue(e.CampaignId, out var state))
                {
                    state.GlobalFlags[e.FlagName] = e.FlagValue;
                }
                else
                {
                    _logger.LogWarning("Кампания {CampaignId} не найдена при установке глобального флага", e.CampaignId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(GlobalFlagRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_campaignStates.TryGetValue(e.CampaignId, out var state))
                {
                    state.GlobalFlags.Remove(e.FlagName);
                }
                else
                {
                    _logger.LogWarning("Кампания {CampaignId} не найдена при удалении глобального флага", e.CampaignId);
                }
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(WorldEventTriggered e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var events = GetOrCreateWorldEvents(e.CampaignId);
                if (!events.Contains(e.EventName))
                    events.Add(e.EventName);
            }
            InvalidateCache(e.CampaignId);
        }

        public void Apply(IDomainEvent e)
        {
            ArgumentNullException.ThrowIfNull(e);
            switch (e)
            {
                case QuestCreated ev: Apply(ev); break;
                case QuestAccepted ev: Apply(ev); break;
                case QuestCompleted ev: Apply(ev); break;
                case QuestFailed ev: Apply(ev); break;
                case QuestObjectiveUpdated ev: Apply(ev); break;
                case QuestRewardClaimed ev: Apply(ev); break;
                case FactionDiscovered ev: Apply(ev); break;
                case FactionReputationChanged ev: Apply(ev); break;
                case CampaignCreated ev: Apply(ev); break;
                case GameTimeAdvanced ev: Apply(ev); break;
                case WeatherChanged ev: Apply(ev); break;
                case RegionDiscovered ev: Apply(ev); break;
                case GlobalFlagSet ev: Apply(ev); break;
                case GlobalFlagRemoved ev: Apply(ev); break;
                case WorldEventTriggered ev: Apply(ev); break;
                case QuestDeleted ev: Apply(ev); break;
                default:
                    _logger.LogDebug("Получено неизвестное событие {EventType} в проекции кампании", e.GetType().Name);
                    break;
            }
        }

        public async Task<List<Guid>> GetActiveQuestIds(Guid campaignId, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"campaign:activeQuests:{campaignId}";
            var cached = await _cache.GetAsync<List<Guid>>(cacheKey, cancellationToken);
            if (cached != null)
                return [.. cached];

            lock (_syncRoot)
            {
                if (_campaignQuests.TryGetValue(campaignId, out var quests))
                {
                    var result = quests.Where(q => q.Status == QuestStatus.Active).Select(q => q.QuestId).ToList();
                    _cache.SetAsync(cacheKey, result, _cacheTtl, cancellationToken).GetAwaiter().GetResult();
                    return [.. result];
                }
            }
            return [];
        }

        public async Task<List<QuestInfo>> GetQuests(Guid campaignId, QuestStatus? statusFilter = null, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"campaign:quests:{campaignId}";
            if (!statusFilter.HasValue)
            {
                var cached = await _cache.GetAsync<List<QuestInfo>>(cacheKey, cancellationToken);
                if (cached != null)
                    return cached.Select(ProjectionCloner.CloneQuest).ToList();
            }

            List<QuestInfo> filtered;
            lock (_syncRoot)
            {
                filtered = _campaignQuests.TryGetValue(campaignId, out var quests)
                    ? (statusFilter.HasValue
                        ? quests.Where(q => q.Status == statusFilter.Value).ToList()
                        : quests.ToList())
                    : new List<QuestInfo>();
            }

            if (!statusFilter.HasValue)
                await _cache.SetAsync(cacheKey, filtered, _cacheTtl, cancellationToken);
            return filtered.Select(ProjectionCloner.CloneQuest).ToList();
        }

        public Task<QuestInfo?> GetQuestDetails(Guid campaignId, Guid questId, CancellationToken _ = default)
        {
            lock (_syncRoot)
            {
                var quest = FindQuest(campaignId, questId);
                return Task.FromResult(quest != null ? ProjectionCloner.CloneQuest(quest) : null);
            }
        }

        public async Task<CampaignState?> GetCampaignState(Guid campaignId, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"campaign:{campaignId}";
            var cached = await _cache.GetAsync<CampaignState>(cacheKey, cancellationToken);
            if (cached != null)
                return ProjectionCloner.CloneCampaignState(cached);

            CampaignState? state;
            lock (_syncRoot)
            {
                _campaignStates.TryGetValue(campaignId, out state);
            }

            if (state != null)
            {
                await _cache.SetAsync(cacheKey, state, _cacheTtl, cancellationToken);
                return ProjectionCloner.CloneCampaignState(state);
            }
            return null;
        }

        public async Task<FactionState?> GetFaction(string factionId, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"campaign:faction:{factionId}";
            var cached = await _cache.GetAsync<FactionState>(cacheKey, cancellationToken);
            if (cached != null)
                return ProjectionCloner.CloneFaction(cached);

            FactionState? faction;
            lock (_syncRoot)
            {
                _factions.TryGetValue(factionId, out faction);
            }

            if (faction != null)
            {
                await _cache.SetAsync(cacheKey, faction, _cacheTtl, cancellationToken);
                return ProjectionCloner.CloneFaction(faction);
            }
            return null;
        }

        public async Task<List<FactionState>> GetAllFactions(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "campaign:factions:all";
            var cached = await _cache.GetAsync<List<FactionState>>(cacheKey, cancellationToken);
            if (cached != null)
                return cached.Select(ProjectionCloner.CloneFaction).ToList();

            List<FactionState> list;
            lock (_syncRoot)
            {
                list = [.. _factions.Values];
            }

            await _cache.SetAsync(cacheKey, list, _cacheTtl, cancellationToken);
            return list.Select(ProjectionCloner.CloneFaction).ToList();
        }

        public async Task<List<string>> GetActiveWorldEvents(Guid campaignId, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"campaign:worldEvents:{campaignId}";
            var cached = await _cache.GetAsync<List<string>>(cacheKey, cancellationToken);
            if (cached != null)
                return new List<string>(cached);

            List<string> events;
            lock (_syncRoot)
            {
                events = _activeWorldEvents.TryGetValue(campaignId, out var list) ? [.. list] : [];
            }

            await _cache.SetAsync(cacheKey, events, _cacheTtl, cancellationToken);
            return new List<string>(events);
        }

        private List<QuestInfo> GetOrCreateQuestList(Guid campaignId)
        {
            if (!_campaignQuests.TryGetValue(campaignId, out var list))
            {
                list = [];
                _campaignQuests[campaignId] = list;
            }
            return list;
        }

        private List<string> GetOrCreateWorldEvents(Guid campaignId)
        {
            if (!_activeWorldEvents.TryGetValue(campaignId, out var list))
            {
                list = [];
                _activeWorldEvents[campaignId] = list;
            }
            return list;
        }

        private QuestInfo? FindQuest(Guid campaignId, Guid questId)
        {
            if (_campaignQuests.TryGetValue(campaignId, out var quests))
                return quests.FirstOrDefault(q => q.QuestId == questId);
            return null;
        }
    }
}