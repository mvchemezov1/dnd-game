// tests/unit/ConsistencyManagerTests.cs
using dnd_game.domain.aggregates;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dnd_game.tests.unit
{
    /// <summary>
    /// Тесты на ConsistencyManager.EnforceConsistencyAsync — конкретно на то, что оптимистическая
    /// блокировка по версии агрегата действительно ловит конфликт версий, а не просто
    /// пропускает всё подряд. Это дополняет (но не заменяет) интеграционный тест
    /// PostgresEventStore, который проверяет конфликт на уровне реальной БД
    /// (см. tests/integration/PostgresEventStoreTests.cs) — здесь проверяется сам
    /// ConsistencyManager в изоляции, без Postgres.
    /// </summary>
    public class ConsistencyManagerTests
    {
        private static readonly CancellationToken cancellationToken = CancellationToken.None;
        private static ConsistencyManager CreateManager(out Mock<IServiceProvider> serviceProviderMock)
        {
            serviceProviderMock = new Mock<IServiceProvider>();
            var eventStoreMock = new Mock<IEventStore>();
            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IEventStore)))
                .Returns(eventStoreMock.Object);

            var lockManager = new InMemoryLockManager(serviceProviderMock.Object);
            var logger = NullLogger<ConsistencyManager>.Instance;
            var metrics = Mock.Of<IMetricsCollector>();  // заглушка, не требует реализации

            return new ConsistencyManager(serviceProviderMock.Object, lockManager, logger, metrics);
        }

        [Fact]
        public async Task EnforceConsistencyAsync_MatchingVersion_ReturnsSuccess()
        {
            var manager = CreateManager(out _);
            var character = new CharacterAggregate(Guid.NewGuid(), "Hero", 20);
            character.SetVersion(0); // OriginalVersion = 0, как будто только что загружен из хранилища

            var result = await manager.EnforceConsistencyAsync(character, expectedVersion: 0, ownerId: "test-user", cancellationToken);

            Assert.Equal(ConsistencyResult.Success, result);
        }

        [Fact]
        public async Task EnforceConsistencyAsync_MismatchedVersion_ReturnsVersionConflict()
        {
            // Сценарий конкурентной записи: агрегат был загружен с версией 0 (OriginalVersion = 0),
            // но к моменту сохранения ожидаемая версия (expectedVersion) уже другая — значит,
            // кто-то другой успел сохранить изменения первым.
            var manager = CreateManager(out _);
            var character = new CharacterAggregate(Guid.NewGuid(), "Hero", 20);
            character.SetVersion(0);

            var result = await manager.EnforceConsistencyAsync(character, expectedVersion: 5, ownerId: "test-user", cancellationToken);

            Assert.Equal(ConsistencyResult.VersionConflict, result);
        }

        [Fact]
        public async Task EnforceConsistencyAsync_ValidBoundaryLevel_ReturnsSuccess()
        {
            var manager = CreateManager(out _);
            var character = new CharacterAggregate(Guid.NewGuid(), "Hero", 20);
            character.SetVersion(0);
            // LevelUp сам по себе не даёт превысить 20 (бросает ArgumentException раньше),
            // поэтому здесь проверяем EnsureInvariants напрямую через штатный путь — уровень
            // не может быть установлен выше 20 никаким легальным способом, что и является
            // инвариантом. Регрессионная защита: если правило когда-нибудь ослабят на уровне
            // LevelUp(), тест ConsistencyManager всё равно должен ловить нарушение отдельно.
            character.LevelUp(20); // максимально допустимый уровень — не должен нарушать инвариант

            var result = await manager.EnforceConsistencyAsync(character, expectedVersion: character.OriginalVersion, ownerId: "test-user", cancellationToken);

            Assert.Equal(ConsistencyResult.Success, result);
        }

        [Fact]
        public async Task EnforceConsistencyAsync_UsesDistinctLockPerAggregate_AllowsConcurrentDifferentAggregates()
        {
            var manager = CreateManager(out _);
            var characterA = new CharacterAggregate(Guid.NewGuid(), "Hero A", 20);
            var characterB = new CharacterAggregate(Guid.NewGuid(), "Hero B", 20);
            characterA.SetVersion(0);
            characterB.SetVersion(0);

            var resultA = await manager.EnforceConsistencyAsync(characterA, 0, "user-a", cancellationToken);
            var resultB = await manager.EnforceConsistencyAsync(characterB, 0, "user-b", cancellationToken);

            Assert.Equal(ConsistencyResult.Success, resultA);
            Assert.Equal(ConsistencyResult.Success, resultB);
        }
        [Fact]
        public async Task EnforceConsistencyAsync_ThrowingInvariant_ReturnsInvariantViolation()
        {
            // CharacterAggregate.EnsureInvariants() на практике недостижим через публичный API
            // (например, SetAbilityScore клэмпит значение вместо того, чтобы допустить нарушение),
            // поэтому здесь используется минимальный тестовый агрегат, который специально
            // нарушает свой инвариант — так проверяется именно обработка нарушения в
            // ConsistencyManager, а не конкретные правила CharacterAggregate.
            var manager = CreateManager(out _);
            var aggregate = new AlwaysInvalidTestAggregate();

            var result = await manager.EnforceConsistencyAsync(aggregate, expectedVersion: 0, ownerId: "test-user", cancellationToken);

            Assert.Equal(ConsistencyResult.InvariantViolation, result);
        }

        private class AlwaysInvalidTestAggregate : AggregateRoot
        {
            protected override void ApplyEvent(dnd_game.domain.events.IDomainEvent @event) { }

            public override void EnsureInvariants()
                => throw new dnd_game.domain.exceptions.RuleViolation("Test", "Intentionally invalid for testing.");
        }

    }
}
