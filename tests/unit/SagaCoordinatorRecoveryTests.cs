using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using dnd_game.application.security;
using dnd_game.domain.events;
using dnd_game.domain.interfaces;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.common;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.tests.unit
{
    public class SagaCoordinatorRecoveryTests
    {
        private record StepEvent(Guid CorrelationId, int Step, bool SimulateCrash) : IDomainEvent;

        private class StepTrackingSagaState : ISagaState
        {
            public Guid SagaId { get; set; }
            public Guid CorrelationId { get; set; }
            public SagaStatus Status { get; set; } = SagaStatus.Started;
            public int Version { get; set; }
            public DateTime CreatedAt { get; } = DateTime.UtcNow;
            public DateTime? UpdatedAt { get; set; }
            public int LastCompletedStep { get; set; }
        }

        private class StepProcessingSaga : ISaga
        {
            private StepTrackingSagaState _state;

            public StepProcessingSaga(Guid correlationId)
            {
                _state = new StepTrackingSagaState { SagaId = correlationId, CorrelationId = correlationId };
            }

            public Guid SagaId => _state.SagaId;
            public ISagaState State => _state;

            public void LoadState(ISagaState state) => _state = (StepTrackingSagaState)state;

            public Task Handle(IDomainEvent @event, CancellationToken cancellationToken = default)
            {
                var stepEvent = (StepEvent)@event;
                if (stepEvent.SimulateCrash)
                    throw new InvalidOperationException($"Simulated crash at step {stepEvent.Step}");

                _state.LastCompletedStep = stepEvent.Step;
                _state.Status = SagaStatus.InProgress;
                return Task.CompletedTask;
            }

            public Task Complete(bool success, string? reason = null, CancellationToken cancellationToken = default)
            {
                _state.Status = success ? SagaStatus.Completed : SagaStatus.Failed;
                return Task.CompletedTask;
            }
        }

        private static SagaCoordinator CreateCoordinator(InMemorySagaStateRepository stateRepository, out SagaRegistry registry)
        {
            registry = new SagaRegistry();
            registry.Register<StepEvent>(e => new StepProcessingSaga(e.CorrelationId));

            var permissionChecker = new PermissionChecker(
                Mock.Of<IUserSecurityContextProvider>(),
                Mock.Of<ICharacterOwnershipRepository>());
            var lockManager = new InMemoryLockManager(
                permissionChecker,
                NullLogger<InMemoryLockManager>.Instance);

            return new SagaCoordinator(
                registry,
                stateRepository,
                Mock.Of<ICommandBus>(),
                lockManager,
                NullLogger<SagaCoordinator>.Instance);
        }

        [Fact]
        public async Task Saga_FailsAtStep2_RestartsAndRecoversFromPersistedState()
        {
            var stateRepository = new InMemorySagaStateRepository();
            var coordinator = CreateCoordinator(stateRepository, out _);
            var correlationId = Guid.NewGuid();

            await coordinator.DispatchAsync(new StepEvent(correlationId, Step: 1, SimulateCrash: false));

            var afterStep1 = await stateRepository.LoadAsync(correlationId);
            Assert.NotNull(afterStep1);
            var step1State = Assert.IsType<StepTrackingSagaState>(afterStep1);
            Assert.Equal(1, step1State.LastCompletedStep);
            Assert.Equal(SagaStatus.InProgress, step1State.Status);

            await coordinator.DispatchAsync(new StepEvent(correlationId, Step: 2, SimulateCrash: true));

            var afterCrash = await stateRepository.LoadAsync(correlationId);
            var crashedState = Assert.IsType<StepTrackingSagaState>(afterCrash);
            Assert.Equal(1, crashedState.LastCompletedStep);
            Assert.Equal(SagaStatus.Failed, crashedState.Status);

            await coordinator.DispatchAsync(new StepEvent(correlationId, Step: 2, SimulateCrash: false));

            var afterRecovery = await stateRepository.LoadAsync(correlationId);
            var recoveredState = Assert.IsType<StepTrackingSagaState>(afterRecovery);
            Assert.Equal(2, recoveredState.LastCompletedStep);
            Assert.Equal(SagaStatus.InProgress, recoveredState.Status);
        }

        [Fact]
        public async Task Saga_WithoutPriorState_StartsFresh()
        {
            var stateRepository = new InMemorySagaStateRepository();
            var coordinator = CreateCoordinator(stateRepository, out _);
            var correlationId = Guid.NewGuid();

            await coordinator.DispatchAsync(new StepEvent(correlationId, Step: 1, SimulateCrash: false));

            var state = await stateRepository.LoadAsync(correlationId);
            Assert.NotNull(state);
        }

        [Fact]
        public async Task UnregisteredEventType_DispatchesToNoSaga_AndDoesNotThrow()
        {
            var stateRepository = new InMemorySagaStateRepository();
            var coordinator = CreateCoordinator(stateRepository, out _);

            var unrelatedEvent = new UnrelatedTestEvent();

            await coordinator.DispatchAsync(unrelatedEvent);
        }

        private record UnrelatedTestEvent : IDomainEvent;
    }
}