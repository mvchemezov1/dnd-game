#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using dnd_game.application.projections;
using dnd_game.application.security; // добавлено
using dnd_game.domain.events;
using dnd_game.domain.interfaces;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.caching;
using dnd_game.infrastructure.common;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.message_bus;
using dnd_game.tests.infrastructure;

namespace dnd_game.tests.integration
{
    public abstract class SagaIntegrationTestBase
    {
        protected readonly InMemoryEventStore EventStore;
        protected readonly InMemoryBus Bus;
        protected readonly InMemorySagaStateRepository SagaStateRepository;
        protected readonly InMemoryQuestTrackingStore QuestTrackingStore;
        protected readonly CharacterProjection CharacterProjection;
        protected readonly CampaignProjection CampaignProjection;
        protected readonly SagaCoordinator Coordinator;

        protected SagaIntegrationTestBase()
        {
            EventStore = new InMemoryEventStore();
            Bus = new InMemoryBus(new Mock<IServiceProvider>().Object, NullLogger<InMemoryBus>.Instance);
            SagaStateRepository = new InMemorySagaStateRepository();
            QuestTrackingStore = new InMemoryQuestTrackingStore();

            var cacheProvider = new NoOpCacheProvider();
            CharacterProjection = new CharacterProjection(cacheProvider, TimeSpan.FromMinutes(5));
            CampaignProjection = new CampaignProjection(cacheProvider, TimeSpan.FromMinutes(10));

            var registry = new SagaRegistry();
            registry.Register<QuestAccepted>(e => new QuestSaga(
                e.QuestId,
                Bus,
                CampaignProjection,
                CharacterProjection,
                QuestTrackingStore));
            registry.Register<QuestObjectiveUpdated>(e => new QuestSaga(
                e.QuestId,
                Bus,
                CampaignProjection,
                CharacterProjection,
                QuestTrackingStore));
            registry.Register<QuestCompleted>(e => new QuestSaga(
                e.QuestId,
                Bus,
                CampaignProjection,
                CharacterProjection,
                QuestTrackingStore));
            registry.Register<QuestFailed>(e => new QuestSaga(
                e.QuestId,
                Bus,
                CampaignProjection,
                CharacterProjection,
                QuestTrackingStore));
            registry.Register<ExperienceGained>(e => new LevelUpSaga(
                e.CharacterId,
                Bus,
                CharacterProjection));

            // Правильное создание InMemoryLockManager с моком PermissionChecker
            var permissionCheckerMock = new Mock<PermissionChecker>();
            var lockManager = new InMemoryLockManager(
                permissionCheckerMock.Object,
                NullLogger<InMemoryLockManager>.Instance);

            Coordinator = new SagaCoordinator(
                registry,
                SagaStateRepository,
                Bus,
                lockManager,
                NullLogger<SagaCoordinator>.Instance);
        }

        /// <summary>
        /// Обёртка для публикации события с автоматическим обновлением проекций и запуском саг.
        /// </summary>
        protected async Task PublishAndDispatch(IDomainEvent @event)
        {
            ApplyToProjections(@event);
            await Coordinator.DispatchAsync(@event);
        }

        /// <summary>
        /// Применяет событие к соответствующим проекциям.
        /// </summary>
        private void ApplyToProjections(IDomainEvent @event)
        {
            switch (@event)
            {
                case CharacterCreated e: CharacterProjection.Apply(e); break;
                case CharacterUpdated e: CharacterProjection.Apply(e); break;
                case CharacterDamageTaken e: CharacterProjection.Apply(e); break;
                case CharacterHealed e: CharacterProjection.Apply(e); break;
                case TemporaryHitPointsSet e: CharacterProjection.Apply(e); break;
                case ExperienceGained e: CharacterProjection.Apply(e); break;
                case CharacterLevelUp e: CharacterProjection.Apply(e); break;
                case AbilityScoreSet e: CharacterProjection.Apply(e); break;
                case SkillProficiencyAdded e: CharacterProjection.Apply(e); break;
                case SkillProficiencyRemoved e: CharacterProjection.Apply(e); break;
                case SavingThrowProficiencyAdded e: CharacterProjection.Apply(e); break;
                case SavingThrowProficiencyRemoved e: CharacterProjection.Apply(e); break;
                case RaceChosen e: CharacterProjection.Apply(e); break;
                case ClassChosen e: CharacterProjection.Apply(e); break;
                case BackgroundChosen e: CharacterProjection.Apply(e); break;
                case FeatAdded e: CharacterProjection.Apply(e); break;
                case FeatRemoved e: CharacterProjection.Apply(e); break;
                case SpellAdded e: CharacterProjection.Apply(e); break;
                case SpellRemoved e: CharacterProjection.Apply(e); break;
                case SpellSlotUsed e: CharacterProjection.Apply(e); break;
                case SpellSlotsRestored e: CharacterProjection.Apply(e); break;
                case ConditionApplied e: CharacterProjection.Apply(e); break;
                case ConditionRemoved e: CharacterProjection.Apply(e); break;
                case AllConditionsCleared e: CharacterProjection.Apply(e); break;
                case ArmorClassUpdated e: CharacterProjection.Apply(e); break;
                case SpeedUpdated e: CharacterProjection.Apply(e); break;
                case ResistanceAdded e: CharacterProjection.Apply(e); break;
                case ResistanceRemoved e: CharacterProjection.Apply(e); break;
                case VulnerabilityAdded e: CharacterProjection.Apply(e); break;
                case VulnerabilityRemoved e: CharacterProjection.Apply(e); break;
                case ImmunityAdded e: CharacterProjection.Apply(e); break;
                case ImmunityRemoved e: CharacterProjection.Apply(e); break;
                case DeathSavingThrowSuccess e: CharacterProjection.Apply(e); break;
                case DeathSavingThrowFailure e: CharacterProjection.Apply(e); break;
                case CharacterStabilized e: CharacterProjection.Apply(e); break;
                case CharacterDied e: CharacterProjection.Apply(e); break;
                case CharacterRevived e: CharacterProjection.Apply(e); break;
                case ItemEquipped e: CharacterProjection.Apply(e); break;
                case ItemUnequipped e: CharacterProjection.Apply(e); break;
                case InventoryItemAdded e: CharacterProjection.Apply(e); break;
                case InventoryItemRemoved e: CharacterProjection.Apply(e); break;
                case HitDieSpent e: CharacterProjection.Apply(e); break;
                case HitDiceRecovered e: CharacterProjection.Apply(e); break;
                case ConcentrationStarted e: CharacterProjection.Apply(e); break;
                case ConcentrationEnded e: CharacterProjection.Apply(e); break;
                case GoldAdded e: CharacterProjection.Apply(e); break;
                case GoldSpent e: CharacterProjection.Apply(e); break;
                case GoldSet e: CharacterProjection.Apply(e); break;

                case CampaignCreated e: CampaignProjection.Apply(e); break;
                case QuestCreated e: CampaignProjection.Apply(e); break;
                case QuestAccepted e: CampaignProjection.Apply(e); break;
                case QuestCompleted e: CampaignProjection.Apply(e); break;
                case QuestFailed e: CampaignProjection.Apply(e); break;
                case QuestObjectiveUpdated e: CampaignProjection.Apply(e); break;
                case FactionDiscovered e: CampaignProjection.Apply(e); break;
                case FactionReputationChanged e: CampaignProjection.Apply(e); break;
                case GameTimeAdvanced e: CampaignProjection.Apply(e); break;
                case WeatherChanged e: CampaignProjection.Apply(e); break;
                case RegionDiscovered e: CampaignProjection.Apply(e); break;
                case GlobalFlagSet e: CampaignProjection.Apply(e); break;
                case GlobalFlagRemoved e: CampaignProjection.Apply(e); break;
                case WorldEventTriggered e: CampaignProjection.Apply(e); break;
            }
        }
    }
}