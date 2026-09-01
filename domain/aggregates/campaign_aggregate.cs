using dnd_game.application.projections;
using dnd_game.domain.events;
using dnd_game.domain.value_objects; // предположим, что QuestObjectiveData и QuestRewardData здесь
using System;
using System.Collections.Generic;
using System.Linq;

namespace dnd_game.domain.aggregates
{
    /// <summary>
    /// Агрегат кампании. Управляет состоянием кампании: игроками, квестами, фракциями, временем, погодой и флагами.
    /// </summary>
    public class CampaignAggregate : AggregateRoot
    {
        // ---------- Поля состояния ----------
        public string Name { get; private set; } = string.Empty;
        public Guid GameMasterId { get; private set; }
        public List<Guid> PlayerIds { get; private set; } = [];
        public List<Guid> ActiveQuestIds { get; private set; } = [];
        public Dictionary<string, int> FactionReputations { get; private set; } = []; // FactionId -> Reputation (-100..100)
        public Dictionary<string, string> GlobalFlags { get; private set; } = [];      // FlagName -> Value
        public int Day { get; private set; } = 1;
        public int Hour { get; private set; } = 8;
        public int Minute { get; private set; } = 0;
        public string CurrentWeather { get; private set; } = "Ясно";
        public List<string> DiscoveredRegions { get; private set; } = [];
        public List<CampaignQuestInfo> Quests { get; private set; } = [];              // детали квестов

        // ---------- Конструкторы ----------
        public CampaignAggregate(Guid campaignId, string name, Guid gameMasterId)
        {
            ApplyChange(new CampaignCreated(campaignId, name, gameMasterId, DateTime.UtcNow));
        }

        // Параметрless конструктор для event sourcing
        public CampaignAggregate() { }

        // ---------- Применение событий ----------
        protected override void ApplyEvent(IDomainEvent @event)
        {
            switch (@event)
            {
                case CampaignCreated e:
                    Id = e.CampaignId;
                    Name = e.Name;
                    GameMasterId = e.GameMasterId;
                    break;

                // --- Игроки ---
                case PlayerJoinedCampaign e:
                    if (!PlayerIds.Contains(e.PlayerId))
                        PlayerIds.Add(e.PlayerId);
                    break;
                case PlayerLeftCampaign e:
                    PlayerIds.Remove(e.PlayerId);
                    break;

                // --- Квесты ---
                case QuestCreated e:
                    Quests.Add(new CampaignQuestInfo
                    {
                        QuestId = e.QuestId,
                        Title = e.Title,
                        Status = QuestStatus.Available,
                        Objectives = e.Objectives,
                        Rewards = e.Rewards,
                        IssuedAt = e.IssuedAt,
                        ParticipantIds = e.ParticipantIds ?? []
                    });
                    break;

                case QuestAccepted e:
                    if (!ActiveQuestIds.Contains(e.QuestId))
                        ActiveQuestIds.Add(e.QuestId);
                    var questInfo = Quests.FirstOrDefault(q => q.QuestId == e.QuestId);
                    if (questInfo != null)
                    {
                        questInfo.Status = QuestStatus.Active;
                        foreach (var participantId in e.ParticipantIds)
                        {
                            if (!questInfo.ParticipantIds.Contains(participantId))
                                questInfo.ParticipantIds.Add(participantId);
                        }
                    }
                    break;

                case QuestCompleted e:
                    ActiveQuestIds.Remove(e.QuestId);
                    var qComp = Quests.FirstOrDefault(q => q.QuestId == e.QuestId);
                    if (qComp != null)
                    {
                        qComp.Status = QuestStatus.Completed;
                        qComp.CompletedAt = e.Timestamp;
                    }
                    break;

                case QuestFailed e:
                    ActiveQuestIds.Remove(e.QuestId);
                    var qFail = Quests.FirstOrDefault(q => q.QuestId == e.QuestId);
                    if (qFail != null)
                        qFail.Status = QuestStatus.Failed;
                    break;

                case QuestDeleted e:
                    Quests.RemoveAll(q => q.QuestId == e.QuestId);
                    ActiveQuestIds.Remove(e.QuestId);
                    break;

                case QuestObjectiveUpdated e:
                    var quest = Quests.FirstOrDefault(q => q.QuestId == e.QuestId);
                    var obj = quest?.Objectives.ElementAtOrDefault(e.ObjectiveIndex);
                    if (obj != null)
                    {
                        obj.IsCompleted = e.IsCompleted;
                        obj.CurrentProgress = e.CurrentProgress;
                    }
                    break;

                // --- Фракции ---
                case FactionAdded e:
                    if (!FactionReputations.ContainsKey(e.FactionId))
                        FactionReputations[e.FactionId] = e.InitialReputation;
                    break;
                case FactionReputationChanged e:
                    if (FactionReputations.TryGetValue(e.FactionId, out int value))
                    {
                        FactionReputations[e.FactionId] = Math.Clamp(value + e.Change, -100, 100);
                    }
                    break;

                // --- Глобальные флаги ---
                case GlobalFlagSet e:
                    GlobalFlags[e.FlagName] = e.FlagValue;
                    break;
                case GlobalFlagRemoved e:
                    GlobalFlags.Remove(e.FlagName);
                    break;

                // --- Игровое время ---
                case GameTimeAdvanced e:
                    Minute += e.Minutes;
                    while (Minute >= 60) { Minute -= 60; Hour++; }
                    while (Hour >= 24) { Hour -= 24; Day++; }
                    break;
                case WeatherChanged e:
                    CurrentWeather = e.NewWeather;
                    break;

                // --- Регионы ---
                case RegionDiscovered e:
                    if (!DiscoveredRegions.Contains(e.RegionName))
                        DiscoveredRegions.Add(e.RegionName);
                    break;
            }
        }

        // ---------- Команды (методы, порождающие события) ----------

        /// <summary>Добавляет игрока в кампанию.</summary>
        public void JoinPlayer(Guid playerId)
        {
            if (playerId == Guid.Empty)
                throw new ArgumentException("Идентификатор игрока не может быть пустым.", nameof(playerId));
            if (PlayerIds.Contains(playerId))
                throw new InvalidOperationException("Игрок уже состоит в кампании.");
            ApplyChange(new PlayerJoinedCampaign(Id, playerId, DateTime.UtcNow));
        }

        /// <summary>Удаляет игрока из кампании.</summary>
        public void LeavePlayer(Guid playerId)
        {
            if (playerId == Guid.Empty)
                throw new ArgumentException("Идентификатор игрока не может быть пустым.", nameof(playerId));
            if (!PlayerIds.Contains(playerId))
                throw new InvalidOperationException("Игрок не состоит в кампании.");
            ApplyChange(new PlayerLeftCampaign(Id, playerId, DateTime.UtcNow));
        }

        /// <summary>Принимает квест (делает его активным).</summary>
        public void AcceptQuest(Guid questId)
        {
            if (questId == Guid.Empty)
                throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));

            var quest = Quests.FirstOrDefault(q => q.QuestId == questId)
                        ?? throw new InvalidOperationException("Квест не найден в кампании.");

            if (quest.Status != QuestStatus.Available)
                throw new InvalidOperationException($"Квест не может быть принят в статусе {quest.Status}.");

            if (ActiveQuestIds.Contains(questId))
                throw new InvalidOperationException("Квест уже активен.");

            // Передаём участников квеста, которые были сохранены при создании
            ApplyChange(new QuestAccepted(Id, questId, quest.ParticipantIds, DateTime.UtcNow));
        }

        /// <summary>Удаляет квест из кампании.</summary>
        public void DeleteQuest(Guid questId)
        {
            if (questId == Guid.Empty)
                throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));

            var quest = Quests.FirstOrDefault(q => q.QuestId == questId)
                        ?? throw new InvalidOperationException("Квест не найден в кампании.");

            if (quest.Status == QuestStatus.Active)
                throw new InvalidOperationException("Нельзя удалить активный квест.");

            ApplyChange(new QuestDeleted(Id, questId, DateTime.UtcNow));
        }

        /// <summary>Завершает активный квест.</summary>
        public void CompleteQuest(Guid questId)
        {
            if (questId == Guid.Empty)
                throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));
            if (!ActiveQuestIds.Contains(questId))
                throw new InvalidOperationException("Квест не активен.");
            ApplyChange(new QuestCompleted(Id, questId, DateTime.UtcNow));
        }

        /// <summary>Проваливает активный квест.</summary>
        public void FailQuest(Guid questId)
        {
            if (questId == Guid.Empty)
                throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));
            if (!ActiveQuestIds.Contains(questId))
                throw new InvalidOperationException("Квест не активен.");
            ApplyChange(new QuestFailed(Id, questId, DateTime.UtcNow));
        }

        /// <summary>Создаёт новый квест в кампании.</summary>
        public void CreateQuest(
            Guid questId,
            string title,
            string description,
            List<QuestObjectiveData> objectives,
            List<QuestRewardData> rewards,
            List<Guid> participantIds)
        {
            if (questId == Guid.Empty)
                throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Название квеста не может быть пустым.", nameof(title));
            if (objectives == null || objectives.Count == 0)
                throw new ArgumentException("Квест должен содержать хотя бы одну цель.", nameof(objectives));
            if (Quests.Any(q => q.QuestId == questId))
                throw new InvalidOperationException("Квест с таким идентификатором уже существует.");

            ApplyChange(new QuestCreated(
                Id,
                questId,
                title,
                description,
                objectives,
                rewards ?? [],
                participantIds ?? [],
                DateTime.UtcNow));
        }

        /// <summary>Обновляет прогресс цели квеста.</summary>
        public void UpdateQuestObjective(Guid questId, int objectiveIndex, bool isCompleted, int currentProgress)
        {
            if (questId == Guid.Empty)
                throw new ArgumentException("Идентификатор квеста не может быть пустым.", nameof(questId));

            var quest = Quests.FirstOrDefault(q => q.QuestId == questId)
                        ?? throw new InvalidOperationException("Квест не найден.");
            if (objectiveIndex < 0 || objectiveIndex >= quest.Objectives.Count)
                throw new InvalidOperationException("Недопустимый индекс цели.");
            if (currentProgress < 0)
                throw new ArgumentOutOfRangeException(nameof(currentProgress), "Прогресс не может быть отрицательным.");

            ApplyChange(new QuestObjectiveUpdated(Id, questId, objectiveIndex, isCompleted, currentProgress));
        }

        /// <summary>Добавляет фракцию в кампанию.</summary>
        public void AddFaction(string factionId, int initialReputation = 0)
        {
            if (string.IsNullOrWhiteSpace(factionId))
                throw new ArgumentException("Идентификатор фракции не может быть пустым.", nameof(factionId));
            if (initialReputation < -100 || initialReputation > 100)
                throw new ArgumentOutOfRangeException(nameof(initialReputation), "Репутация должна быть в диапазоне [-100, 100].");
            if (FactionReputations.ContainsKey(factionId))
                throw new InvalidOperationException("Фракция уже существует в кампании.");

            ApplyChange(new FactionAdded(Id, factionId, initialReputation));
        }

        /// <summary>Изменяет репутацию фракции.</summary>
        public void ChangeFactionReputation(string factionId, int change)
        {
            if (string.IsNullOrWhiteSpace(factionId))
                throw new ArgumentException("Идентификатор фракции не может быть пустым.", nameof(factionId));
            if (!FactionReputations.ContainsKey(factionId))
                throw new InvalidOperationException("Фракция не найдена.");

            ApplyChange(new FactionReputationChanged(Id, factionId, change));
        }

        /// <summary>Устанавливает глобальный флаг кампании.</summary>
        public void SetGlobalFlag(string flagName, string value)
        {
            if (string.IsNullOrWhiteSpace(flagName))
                throw new ArgumentException("Имя флага не может быть пустым.", nameof(flagName));
            ArgumentNullException.ThrowIfNull(value);

            ApplyChange(new GlobalFlagSet(Id, flagName, value));
        }

        /// <summary>Удаляет глобальный флаг.</summary>
        public void RemoveGlobalFlag(string flagName)
        {
            if (string.IsNullOrWhiteSpace(flagName))
                throw new ArgumentException("Имя флага не может быть пустым.", nameof(flagName));
            if (!GlobalFlags.ContainsKey(flagName))
                throw new InvalidOperationException("Флаг не найден.");

            ApplyChange(new GlobalFlagRemoved(Id, flagName));
        }

        /// <summary>Продвигает игровое время на указанное количество минут.</summary>
        public void AdvanceTime(int minutes)
        {
            if (minutes <= 0)
                throw new ArgumentException("Количество минут должно быть положительным.", nameof(minutes));

            ApplyChange(new GameTimeAdvanced(Id, minutes));
        }

        /// <summary>Изменяет текущую погоду.</summary>
        public void ChangeWeather(string newWeather)
        {
            if (string.IsNullOrWhiteSpace(newWeather))
                throw new ArgumentException("Погода не может быть пустой.", nameof(newWeather));

            ApplyChange(new WeatherChanged(Id, newWeather));
        }

        /// <summary>Отмечает регион как открытый.</summary>
        public void DiscoverRegion(string regionName)
        {
            if (string.IsNullOrWhiteSpace(regionName))
                throw new ArgumentException("Название региона не может быть пустым.", nameof(regionName));
            if (DiscoveredRegions.Contains(regionName))
                return; // уже открыто, не дублируем событие

            ApplyChange(new RegionDiscovered(Id, regionName));
        }
    }

    /// <summary>
    /// Информация о квесте внутри кампании.
    /// </summary>
    public class CampaignQuestInfo
    {
        public Guid QuestId { get; set; }
        public string Title { get; set; } = string.Empty;
        public QuestStatus Status { get; set; } = QuestStatus.Available;
        public List<QuestObjectiveData> Objectives { get; set; } = [];
        public List<QuestRewardData> Rewards { get; set; } = [];
        public DateTime IssuedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<Guid> ParticipantIds { get; set; } = [];
    }
}