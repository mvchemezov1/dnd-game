#nullable enable
using dnd_game.application.projections;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.interfaces;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.domain.sagas
{
    /// <summary>
    /// Сага квеста: отслеживает жизненный цикл квеста, обновляет цели,
    /// обрабатывает смерть участников и выдаёт награды.
    /// Один экземпляр саги соответствует одному квесту (SagaId = QuestId).
    /// </summary>
    public class QuestSaga : ISaga
    {
        private readonly ICommandBus _commandBus;
        private readonly CampaignProjection _campaignProjection;
        private readonly CharacterProjection _characterProjection;
        private readonly IQuestTrackingStore _trackingStore;
        private readonly ILogger<QuestSaga> _logger;
        private QuestSagaState _state;

        public QuestSaga(
            Guid questId,
            ICommandBus commandBus,
            CampaignProjection campaignProjection,
            CharacterProjection characterProjection,
            IQuestTrackingStore trackingStore,
            ILogger<QuestSaga>? logger = null)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _campaignProjection = campaignProjection ?? throw new ArgumentNullException(nameof(campaignProjection));
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _trackingStore = trackingStore ?? throw new ArgumentNullException(nameof(trackingStore));
            _logger = logger ?? NullLogger<QuestSaga>.Instance;

            _state = new QuestSagaState
            {
                SagaId = questId,
                CorrelationId = questId,
                QuestId = questId,
                Status = SagaStatus.Started,
                CreatedAt = DateTime.UtcNow
            };
        }

        public Guid SagaId => _state.SagaId;
        public ISagaState State => _state;

        public void LoadState(ISagaState state)
        {
            _state = state as QuestSagaState
                     ?? throw new ArgumentException("Неверный тип состояния саги", nameof(state));
        }

        public async Task Handle(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            switch (@event)
            {
                case QuestAccepted accepted:
                    await OnQuestAccepted(accepted, cancellationToken);
                    break;

                case QuestObjectiveUpdated objectiveUpdated:
                    await OnObjectiveUpdated(objectiveUpdated, cancellationToken);
                    break;

                case QuestCompleted completed:
                    await OnQuestCompleted(completed, cancellationToken);
                    break;

                case QuestFailed failed:
                    await OnQuestFailed(failed, cancellationToken);
                    break;

                case CharacterDied died:
                    await OnCharacterDied(died, cancellationToken);
                    break;

                case ItemAcquired acquired:
                    await OnItemAcquired(acquired, cancellationToken);
                    break;

                default:
                    break;
            }
        }

        public Task Complete(bool success, string? reason = null, CancellationToken cancellationToken = default)
        {
            _state.Status = success ? SagaStatus.Completed : SagaStatus.Failed;
            _state.CompletionReason = reason;
            _state.UpdatedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        // --------------------------------------------------------------------------------------------
        // Реакции на события
        // --------------------------------------------------------------------------------------------

        private async Task OnQuestAccepted(QuestAccepted e, CancellationToken ct)
        {
            var questInfo = await _campaignProjection.GetQuestDetails(e.CampaignId, e.QuestId, ct);
            if (questInfo == null)
            {
                _logger.LogWarning("Квест {QuestId} не найден в проекции при принятии", e.QuestId);
                return;
            }

            _state.CampaignId = e.CampaignId;
            _state.ParticipantIds = e.ParticipantIds ?? [];
            _state.QuestStatus = QuestSagaStatus.InProgress;
            _state.Status = SagaStatus.InProgress;
            _state.UpdatedAt = DateTime.UtcNow;

            _state.Objectives = [.. questInfo.Objectives.Select(o => new TrackedObjective
            {
                Description = o.Description,
                RequiredProgress = o.RequiredProgress,
                CurrentProgress = 0
            })];

            _state.Rewards = [.. questInfo.Rewards.Select(r => new QuestRewardData
            {
                Description = r.Description,
                ExperiencePoints = r.ExperiencePoints,
                ItemIds = r.ItemIds,
                Gold = r.Gold,
                FactionReputationChange = r.FactionReputationChange
            })];

            // Регистрируем участников и кампанию в tracking store
            if (e.ParticipantIds is not null)
            {
                foreach (var participantId in e.ParticipantIds)
                {
                    await _trackingStore.AddParticipantAsync(e.QuestId, participantId, ct);
                }
            }

            await _trackingStore.SetCampaignAsync(e.QuestId, e.CampaignId, ct);
        }

        private async Task OnCharacterDied(CharacterDied e, CancellationToken ct)
        {
            // Этот обработчик может быть вызван на «одноразовом» инстансе саги,
            // поэтому не полагаемся на _state.CampaignId, а ищем через tracking store.
            var questIds = (await _trackingStore.GetQuestsForCharacterAsync(e.CharacterId, ct)).ToList();
            foreach (var questId in questIds)
            {
                var campaignId = await _trackingStore.GetCampaignAsync(questId, ct);
                if (campaignId.HasValue)
                {
                    await _commandBus.SendAsync(
                        new FailQuestCommand(campaignId.Value, questId),
                        new CommandContext { CancellationToken = ct });
                }
            }
        }

        private async Task OnItemAcquired(ItemAcquired e, CancellationToken ct)
        {
            // TODO: реализовать продвижение целей квеста при получении предмета.
            // Требуется маппинг «предмет → цель квеста», который пока не спроектирован.
            // В текущей версии оставляем заглушку с логированием.
            var questIds = (await _trackingStore.GetQuestsForItemAsync(e.ItemId, ct)).ToList();
            _logger.LogDebug("Получен предмет {ItemId}, затронуты квесты: {QuestIds}", e.ItemId, string.Join(", ", questIds));
            await Task.CompletedTask;
        }

        private async Task OnObjectiveUpdated(QuestObjectiveUpdated e, CancellationToken ct)
        {
            if (_state.QuestStatus != QuestSagaStatus.InProgress)
                return;

            if (e.ObjectiveIndex >= 0 && e.ObjectiveIndex < _state.Objectives.Count)
            {
                var obj = _state.Objectives[e.ObjectiveIndex];
                obj.CurrentProgress = e.CurrentProgress;
                obj.IsCompleted = e.IsCompleted;
            }

            if (_state.Objectives.Count > 0 && _state.Objectives.All(o => o.IsCompleted))
            {
                if (_state.CampaignId == Guid.Empty)
                {
                    _logger.LogWarning("Не удалось завершить квест {QuestId}: CampaignId не задан", e.QuestId);
                    return;
                }

                await _commandBus.SendAsync(
                    new CompleteQuestCommand(_state.CampaignId, e.QuestId),
                    new CommandContext { CancellationToken = ct });
            }
        }

        private async Task OnQuestCompleted(QuestCompleted e, CancellationToken ct)
        {
            if (_state.QuestStatus == QuestSagaStatus.Completed)
                return;

            var allCharacters = await _characterProjection.GetAll(ct);
            var participants = allCharacters
                .Where(c => _state.ParticipantIds.Contains(c.Id))
                .ToList();

            foreach (var character in participants)
            {
                await GrantRewards(character.Id, _state.Rewards, ct);
            }

            _state.QuestStatus = QuestSagaStatus.Completed;
            _state.Status = SagaStatus.Completed;
            _state.UpdatedAt = DateTime.UtcNow;
            await _trackingStore.RemoveQuestAsync(e.QuestId, ct);
        }

        private async Task OnQuestFailed(QuestFailed e, CancellationToken ct)
        {
            _state.QuestStatus = QuestSagaStatus.Failed;
            _state.Status = SagaStatus.Failed;
            _state.UpdatedAt = DateTime.UtcNow;
            await _trackingStore.RemoveQuestAsync(e.QuestId, ct);
        }

        // --------------------------------------------------------------------------------------------
        // Выдача наград
        // --------------------------------------------------------------------------------------------

        private async Task GrantRewards(Guid characterId, List<QuestRewardData> rewards, CancellationToken ct)
        {
            if (rewards == null || rewards.Count == 0)
                return;

            foreach (var reward in rewards)
            {
                if (reward.ExperiencePoints > 0)
                    await _commandBus.SendAsync(
                        new GainExperience(characterId, reward.ExperiencePoints),
                        new CommandContext { CancellationToken = ct });

                if (reward.Gold > 0)
                    await _commandBus.SendAsync(
                        new AddGold(characterId, reward.Gold),
                        new CommandContext { CancellationToken = ct });

                foreach (var itemId in reward.ItemIds)
                    await _commandBus.SendAsync(
                        new AddInventoryItem(characterId, itemId, itemId, 1),
                        new CommandContext { CancellationToken = ct });

                if (!string.IsNullOrEmpty(reward.FactionReputationChange))
                {
                    var parts = reward.FactionReputationChange.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int change))
                    {
                        // Исправлено: команда ожидает CampaignId, а не CharacterId
                        await _commandBus.SendAsync(
                            new ChangeFactionReputationCommand(_state.CampaignId, parts[0], change),
                            new CommandContext { CancellationToken = ct });
                    }
                }
            }
        }

        // --------------------------------------------------------------------------------------------
        // Внутренние классы
        // --------------------------------------------------------------------------------------------

        private class QuestSagaState : ISagaState
        {
            public Guid SagaId { get; set; }
            public Guid CorrelationId { get; set; }
            public SagaStatus Status { get; set; } = SagaStatus.Started;
            public int Version { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? UpdatedAt { get; set; }

            public Guid QuestId { get; set; }
            public Guid CampaignId { get; set; }
            public QuestSagaStatus QuestStatus { get; set; } = QuestSagaStatus.InProgress;
            public List<TrackedObjective> Objectives { get; set; } = [];
            public List<QuestRewardData> Rewards { get; set; } = [];
            public List<Guid> ParticipantIds { get; set; } = [];
            public string? CompletionReason { get; set; }
        }

        private class TrackedObjective
        {
            public string Description { get; set; } = string.Empty;
            public bool IsCompleted { get; set; }
            public int CurrentProgress { get; set; }
            public int RequiredProgress { get; set; }
        }

        private enum QuestSagaStatus
        {
            InProgress,
            Completed,
            Failed
        }
    }
}