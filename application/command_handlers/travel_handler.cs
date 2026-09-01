#nullable enable
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
    /// Обработчик команд путешествий. Работает с агрегатом JourneyAggregate.
    /// </summary>
    public class TravelHandler : ICommandHandler<StartJourneyCommand>,
                                 ICommandHandler<EndJourneyCommand>,
                                 ICommandHandler<TravelDayCommand>,
                                 ICommandHandler<SetTravelPaceCommand>,
                                 ICommandHandler<ForcedMarchCommand>,
                                 ICommandHandler<NavigationCheckCommand>,
                                 ICommandHandler<PartyLostCommand>,
                                 ICommandHandler<ConsumeResourcesCommand>,
                                 ICommandHandler<RandomEncounterCheckCommand>,
                                 ICommandHandler<ApplyExhaustionCommand>
    {
        private readonly IEventStore _eventStore;

        public TravelHandler(IEventStore eventStore)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        }

        private async Task<JourneyAggregate> LoadJourneyAsync(Guid partyId, CancellationToken ct)
        {
            var journey = await _eventStore.Load<JourneyAggregate>(partyId, ct);
            if (journey == null)
                throw new InvalidAction($"Путешествие для группы {partyId} не найдено. Сначала начните путешествие.");
            return journey;
        }

        private async Task SaveJourneyAsync(JourneyAggregate journey, CancellationToken ct)
        {
            await _eventStore.Save(journey, ct);
        }

        public async Task Handle(StartJourneyCommand command, CancellationToken cancellationToken)
        {
            // PartyId используется как идентификатор агрегата
            var journey = new JourneyAggregate(command.PartyId, command.RouteId, command.Pace);
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(EndJourneyCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.EndJourney();
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(TravelDayCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.AdvanceDay(command.Terrain, command.HoursTraveled, command.NavigationCheckResult);
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(SetTravelPaceCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.ChangePace(command.Pace);
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(ForcedMarchCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.ForcedMarch(command.AdditionalHours);
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(NavigationCheckCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.PerformNavigationCheck(command.Roll, command.WisdomModifier, command.IsProficient);
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(PartyLostCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.MarkAsLost();
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(ConsumeResourcesCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.ConsumeResources(command.Days);
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(RandomEncounterCheckCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.CheckRandomEncounter(command.Terrain);
            await SaveJourneyAsync(journey, cancellationToken);
        }

        public async Task Handle(ApplyExhaustionCommand command, CancellationToken cancellationToken)
        {
            var journey = await LoadJourneyAsync(command.PartyId, cancellationToken);
            journey.ApplyExhaustion(command.ExhaustionLevel);
            await SaveJourneyAsync(journey, cancellationToken);
        }
    }
}