using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;

namespace dnd_game.application.command_handlers
{
    /// <summary>
    /// Базовый класс для обработчиков команд, работающих с агрегатом Campaign.
    /// Содержит общую логику загрузки и сохранения агрегата.
    /// </summary>
    public abstract class CampaignCommandHandlerBase(IEventStore eventStore)
    {
        protected readonly IEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));

        /// <summary>
        /// Загружает агрегат Campaign по идентификатору. Если агрегат не найден, выбрасывает исключение с русским сообщением.
        /// </summary>
        protected async Task<CampaignAggregate> GetCampaignAsync(Guid campaignId, CancellationToken cancellationToken)
        {
            var aggregate = await _eventStore.Load<CampaignAggregate>(campaignId, cancellationToken) ?? throw new InvalidAction("Кампания не найдена");
            return aggregate;
        }

        /// <summary>
        /// Сохраняет изменения агрегата в Event Store.
        /// </summary>
        protected async Task SaveCampaignAsync(CampaignAggregate aggregate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(aggregate);
            await _eventStore.Save(aggregate, cancellationToken);
        }
    }

    /// <summary>
    /// Обработчик команд для управления кампаниями.
    /// Реализует все команды, связанные с квестами и репутацией фракций.
    /// </summary>
    public class CampaignHandler(IEventStore eventStore) : CampaignCommandHandlerBase(eventStore),
                                   ICommandHandler<AcceptQuestCommand>,
                                   ICommandHandler<CompleteQuestCommand>,
                                   ICommandHandler<FailQuestCommand>,
                                   ICommandHandler<CreateQuestCommand>,
                                   ICommandHandler<UpdateQuestObjectiveCommand>,
                                   ICommandHandler<ChangeFactionReputationCommand>,
                                   ICommandHandler<DeleteQuestCommand>,
                                   ICommandHandler<CreateCampaignCommand>,
                                   ICommandHandler<AddPlayerToCampaignCommand>,
                                   ICommandHandler<RemovePlayerFromCampaignCommand>
    {
        public async Task Handle(AcceptQuestCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.AcceptQuest(command.QuestId);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(ChangeFactionReputationCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            // Исправлено имя метода: ChangeFactionReputation вместо ChangeFactionReputationCommand
            aggregate.ChangeFactionReputation(command.FactionId, command.Change);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(CompleteQuestCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.CompleteQuest(command.QuestId);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(FailQuestCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.FailQuest(command.QuestId);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(CreateQuestCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.CreateQuest(
                command.QuestId,
                command.Title,
                command.Description,   // если поле есть
                command.Objectives,
                command.Rewards,
                command.ParticipantIds ?? []
            );
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(DeleteQuestCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.DeleteQuest(command.QuestId);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(UpdateQuestObjectiveCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.UpdateQuestObjective(
                command.QuestId,
                command.ObjectiveIndex,
                command.IsCompleted,
                command.CurrentProgress
            );
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(CreateCampaignCommand command, CancellationToken cancellationToken)
        {
            // Проверяем, что кампания с таким ID ещё не существует
            var existing = await _eventStore.Load<CampaignAggregate>(command.CampaignId, cancellationToken);
            if (existing != null)
                throw new InvalidOperationException($"Кампания с ID {command.CampaignId} уже существует.");

            var aggregate = new CampaignAggregate(command.CampaignId, command.Name, command.GameMasterId);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(AddPlayerToCampaignCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.JoinPlayer(command.PlayerId);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }

        public async Task Handle(RemovePlayerFromCampaignCommand command, CancellationToken cancellationToken)
        {
            var aggregate = await GetCampaignAsync(command.CampaignId, cancellationToken);
            aggregate.LeavePlayer(command.PlayerId);
            await SaveCampaignAsync(aggregate, cancellationToken);
        }
    }
}