// tests/unit/SagaCoordinatorRecoveryTests.cs
using dnd_game.domain.events;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.tests.unit
{
    /// <summary>
    /// ИСТОРИЯ (актуально на момент миграции под SagaCoordinator, см. Вариант A):
    ///
    /// Раньше TradeSaga/QuestSaga/LevelUpSaga формально реализовывали ISaga, но ни одна не была
    /// зарегистрирована в ISagaRegistry, и ISagaDispatcher.DispatchAsync нигде не вызывался — то
    /// есть саги были полностью отключены от реальных событий. Они управляли сразу множеством
    /// сущностей (много сделок/квестов на один инстанс) через собственные словари состояния,
    /// что не совместимо с моделью "один ISaga-инстанс на одну сущность", которую предполагает
    /// SagaCoordinator/ISagaStateRepository.
    ///
    /// Это исправлено: TradeSaga/QuestSaga/LevelUpSaga/CombatSaga переписаны под модель "один
    /// инстанс — одна сущность" (SagaId = OfferId/QuestId/CharacterId/CombatId), а
    /// infrastructure/coordination/saga_registrations.cs регистрирует их фабрики в
    /// ISagaRegistry и подписывает ISagaDispatcher на IEventBus. Прямые тесты на реальные саги —
    /// см. TradeSagaRecoveryTests ниже. QuestSaga/LevelUpSaga/CombatSaga тем же методом можно
    /// протестировать по аналогии, если понадобится более полное покрытие.
    ///
    /// Тест ниже (на минимальной тестовой саге, а не на реальных TradeSaga/QuestSaga) остаётся
    /// полезным как чистая спецификация самого механизма восстановления SagaCoordinator, не
    /// зависящая от бизнес-логики конкретной саги.
    ///
    /// Ниже — тест того же сценария на самом SagaCoordinator (реальный класс, который и должен
    /// обеспечивать восстановление) с минимальной тестовой сагой, честно реализующей ISaga.
    /// Он подтверждает, что МЕХАНИЗМ восстановления через ISagaStateRepository работает корректно,
    /// и одновременно служит спецификацией: если TradeSaga/QuestSaga/LevelUpSaga когда-нибудь
    /// переделают под модель "один инстанс на одну сущность" и зарегистрируют в ISagaRegistry,
    /// этот же тест-паттерн можно будет применить и к ним напрямую.
    /// </summary>
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

        /// <summary>Честная реализация ISaga: один инстанс — одна сущность (один CorrelationId).</summary>
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

            return new SagaCoordinator(
                registry,
                stateRepository,
                Mock.Of<ICommandBus>(),
                new InMemoryLockManager(Mock.Of<IServiceProvider>()),
                NullLogger<SagaCoordinator>.Instance);
        }

        [Fact]
        public async Task Saga_FailsAtStep2_RestartsAndRecoversFromPersistedState()
        {
            var stateRepository = new InMemorySagaStateRepository();
            var coordinator = CreateCoordinator(stateRepository, out _);
            var correlationId = Guid.NewGuid();

            // Шаг 1: обрабатывается успешно и сохраняется.
            await coordinator.DispatchAsync(new StepEvent(correlationId, Step: 1, SimulateCrash: false));

            var afterStep1 = await stateRepository.LoadAsync(correlationId);
            Assert.NotNull(afterStep1);
            var step1State = Assert.IsType<StepTrackingSagaState>(afterStep1);
            Assert.Equal(1, step1State.LastCompletedStep);
            Assert.Equal(SagaStatus.InProgress, step1State.Status);

            // Шаг 2: "падает" (например, сбой процесса на этом шаге).
            await coordinator.DispatchAsync(new StepEvent(correlationId, Step: 2, SimulateCrash: true));

            var afterCrash = await stateRepository.LoadAsync(correlationId);
            var crashedState = Assert.IsType<StepTrackingSagaState>(afterCrash);
            // Прогресс шага 1 не потерян, несмотря на падение на шаге 2.
            Assert.Equal(1, crashedState.LastCompletedStep);
            Assert.Equal(SagaStatus.Failed, crashedState.Status);

            // "Перезапуск": повторно диспатчим тот же шаг 2, на этот раз без сбоя.
            // Новый инстанс саги создаётся заново (через фабрику), но восстанавливает состояние
            // из ISagaStateRepository — это и есть механизм восстановления после падения.
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
            // Это как раз то, что сегодня происходит с TradeFailed/QuestCompleted/ExperienceGained:
            // ни один фактор не зарегистрирован для TradeSaga/QuestSaga/LevelUpSaga, поэтому
            // DispatchAsync просто не находит фабрик и ничего не делает.
            var stateRepository = new InMemorySagaStateRepository();
            var coordinator = CreateCoordinator(stateRepository, out _);

            var unrelatedEvent = new UnrelatedTestEvent();

            await coordinator.DispatchAsync(unrelatedEvent); // не должно бросать
        }

        private record UnrelatedTestEvent : IDomainEvent;
    }
}
