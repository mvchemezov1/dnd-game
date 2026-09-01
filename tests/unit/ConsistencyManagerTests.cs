#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using dnd_game.application.security; // добавлено для PermissionChecker
using dnd_game.domain.aggregates;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.monitoring;

namespace dnd_game.tests.unit
{
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

            // Создаём замоканный PermissionChecker для InMemoryLockManager
            var permissionCheckerMock = new Mock<PermissionChecker>();
            var lockManager = new InMemoryLockManager(
                permissionCheckerMock.Object,
                NullLogger<InMemoryLockManager>.Instance);

            var logger = NullLogger<ConsistencyManager>.Instance;
            var metrics = Mock.Of<IMetricsCollector>();

            return new ConsistencyManager(serviceProviderMock.Object, lockManager, logger, metrics);
        }

        [Fact]
        public async Task EnforceConsistencyAsync_MatchingVersion_ReturnsSuccess()
        {
            var manager = CreateManager(out _);
            var character = new CharacterAggregate(Guid.NewGuid(), "Hero", 20);
            character.SetVersion(0);

            var result = await manager.EnforceConsistencyAsync(character, expectedVersion: 0, ownerId: "test-user", cancellationToken);

            Assert.Equal(ConsistencyResult.Success, result);
        }

        [Fact]
        public async Task EnforceConsistencyAsync_MismatchedVersion_ReturnsVersionConflict()
        {
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
            character.LevelUp(20); // максимально допустимый уровень

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
            var manager = CreateManager(out _);
            var aggregate = new AlwaysInvalidTestAggregate();

            var result = await manager.EnforceConsistencyAsync(aggregate, expectedVersion: 0, ownerId: "test-user", cancellationToken);

            Assert.Equal(ConsistencyResult.InvariantViolation, result);
        }

        private class AlwaysInvalidTestAggregate : AggregateRoot
        {
            protected override void ApplyEvent(dnd_game.domain.events.IDomainEvent @event) { }

            public override void EnsureInvariants()
                => throw new RuleViolation("Test", "Намеренно невалидный для теста.");
        }
    }
}