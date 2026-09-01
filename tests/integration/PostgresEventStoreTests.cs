// tests/integration/PostgresEventStoreTests.cs
using dnd_game.application.security;
using dnd_game.domain.aggregates;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dnd_game.tests.integration
{
    /// <summary>
    /// Требует реальный PostgreSQL. Задайте переменную окружения
    /// DND_TEST_POSTGRES_CONNECTION (например, "Host=localhost;Database=dnd_test;Username=postgres;Password=postgres")
    /// перед запуском — например через docker-compose с одноразовой тестовой БД.
    ///
    /// Без этой переменной тесты в этом классе тихо пропускаются (return без Assert),
    /// а не падают — xunit v2 без дополнительных пакетов не поддерживает runtime Skip,
    /// а падение всего набора тестов только из-за отсутствия локальной БД увело бы
    /// внимание от реальных регрессий. Если переменная не установлена, ниже НИЧЕГО не
    /// проверяется — эти тесты нужно запускать отдельно, с настоящей Postgres.
    /// </summary>
    public class PostgresEventStoreTests
    {
        private static readonly CancellationToken cancellationToken = CancellationToken.None;
        private static string? ConnectionString =>
            Environment.GetEnvironmentVariable("DND_TEST_POSTGRES_CONNECTION");

        private static (PostgresEventStore store, SnapshotStore snapshots) CreateStore(int snapshotEventInterval = 1000)
        {
            var config = new SnapshotConfiguration { EventCountInterval = snapshotEventInterval };
            var snapshots = new SnapshotStore(ConnectionString!, config);

            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventStore))).Returns((IEventStore?)null);

            var permissionChecker = new PermissionChecker(
                Mock.Of<IUserSecurityContextProvider>(),
                Mock.Of<ICharacterOwnershipRepository>()
            );
            var lockManager = new InMemoryLockManager(permissionChecker, NullLogger<InMemoryLockManager>.Instance);

            var loggerMock = new Mock<ILogger<ConsistencyManager>>();
            var metricsMock = new Mock<IMetricsCollector>();

            var consistencyManager = new ConsistencyManager(
                serviceProviderMock.Object,
                lockManager,
                loggerMock.Object,
                metricsMock.Object);

            var storeLoggerMock = new Mock<ILogger<PostgresEventStore>>();
            var storeMetricsMock = new Mock<IMetricsCollector>();
            var eventBusMock = new Mock<IEventBus>();

            var store = new PostgresEventStore(
                ConnectionString!,
                snapshots,
                consistencyManager,
                storeLoggerMock.Object,
                storeMetricsMock.Object,
                eventBusMock.Object);

            return (store, snapshots);
        }

        [Fact]
        public async Task Save_Load_RoundTripsThroughEventReplay_WithoutSnapshot()
        {
            if (string.IsNullOrEmpty(ConnectionString)) return; // см. class-level комментарий

            var (store, _) = CreateStore(snapshotEventInterval: 1000); // порог заведомо не достигается
            var characterId = Guid.NewGuid();
            var character = new CharacterAggregate(characterId, "No Snapshot Hero", 30);
            character.TakeDamage(12);

            await store.SaveWithMetadata(character, new EventMetadata(), cancellationToken);

            var loaded = await store.Load<CharacterAggregate>(characterId, cancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(18, loaded!.HitPoints);
            Assert.Equal(character.Version, loaded.Version);
        }

        [Fact]
        public async Task Save_CreatesSnapshotAfterThreshold_AndLoadRestoresCorrectState()
        {
            if (string.IsNullOrEmpty(ConnectionString)) return;

            var (store, snapshots) = CreateStore(snapshotEventInterval: 3);
            var characterId = Guid.NewGuid();

            var character = new CharacterAggregate(characterId, "Snapshot Hero", 50);
            await store.SaveWithMetadata(character, new EventMetadata(), cancellationToken); // version 1 (Created)

            character.TakeDamage(5);
            await store.SaveWithMetadata(character, new EventMetadata(), cancellationToken); // version 2

            character.TakeDamage(3);
            await store.SaveWithMetadata(character, new EventMetadata(), cancellationToken); // version 3 -> должен создать снапшот

            var snapshot = await snapshots.GetLatestSnapshotAsync(characterId, int.MaxValue);
            Assert.NotNull(snapshot);
            Assert.Equal(3, snapshot!.Version);

            var restored = await store.Load<CharacterAggregate>(characterId, cancellationToken);
            Assert.NotNull(restored);
            Assert.Equal(50 - 5 - 3, restored!.HitPoints);
            Assert.Equal(3, restored.Version);
        }

        [Fact]
        public async Task ConcurrentSave_TwoClientsFromSameVersion_BothSucceed_NoEventsLost()
        {
            if (string.IsNullOrEmpty(ConnectionString)) return;

            var (store, _) = CreateStore();
            var characterId = Guid.NewGuid();

            var original = new CharacterAggregate(characterId, "Contested Hero", 100);
            await store.SaveWithMetadata(original, new EventMetadata(), cancellationToken); // version 1

            // Два независимых "клиента" загружают один и тот же агрегат на одной и той же версии.
            var clientA = await store.Load<CharacterAggregate>(characterId, cancellationToken);
            var clientB = await store.Load<CharacterAggregate>(characterId, cancellationToken);
            Assert.NotNull(clientA);
            Assert.NotNull(clientB);

            clientA!.TakeDamage(10);
            clientB!.TakeDamage(5);

            // Настоящая конкурентная запись в один и тот же aggregate_id/version.
            // SaveWithMetadata должен поймать конфликт уникального индекса (aggregate_id, version)
            // на стороне Postgres, перезагрузить агрегат и повторно применить несохранённые
            // события — то есть оба клиента должны в итоге сохраниться, без потери событий.
            var taskA = store.SaveWithMetadata(clientA, new EventMetadata(), cancellationToken);
            var taskB = store.SaveWithMetadata(clientB, new EventMetadata(), cancellationToken);
            await Task.WhenAll(taskA, taskB);

            var finalVersion = await store.GetCurrentVersionAsync(characterId);
            // CharacterCreated (1) + TakeDamage от A (1) + TakeDamage от B (1) = 3 события,
            // если ни одно событие не потерялось при разрешении конфликта версий.
            Assert.Equal(3, finalVersion);

            var finalState = await store.Load<CharacterAggregate>(characterId, cancellationToken);
            Assert.NotNull(finalState);
            Assert.Equal(100 - 10 - 5, finalState!.HitPoints);
        }
    }
}
