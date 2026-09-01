// tests/unit/TradeSagaTests.cs
using dnd_game.application.projections;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.interfaces;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.caching;
using dnd_game.infrastructure.common;
using dnd_game.infrastructure.coordination;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dnd_game.tests.unit
{
    /// <summary>
    /// Тесты на реальную TradeSaga через реальный SagaCoordinator — после миграции под
    /// модель "один инстанс = одна сделка" (SagaId = OfferId), см. domain/sagas/trade_saga.cs
    /// и infrastructure/coordination/saga_registrations.cs.
    ///
    /// Gold-часть сделки здесь не тестируется отдельным сценарием "не хватает золота", потому
    /// что CharacterProjection сейчас не имеет Apply-обработчика для событий изменения золота —
    /// Gold в проекции всегда 0. Это отдельный, самостоятельный пробел в проекции, не связанный
    /// с миграцией саги, поэтому тесты ниже работают только с предметами (без золота).
    /// </summary>
    public class TradeSagaTests
    {
        private static bool IsRemoveInventoryItem(ICommand c, Guid characterId, string itemId)
    => c is RemoveInventoryItem r && r.CharacterId == characterId && r.ItemId == itemId;

        private static bool IsAddInventoryItem(ICommand c, Guid characterId, string itemId)
            => c is AddInventoryItem a && a.CharacterId == characterId && a.ItemId == itemId;

        private static bool IsRemoveInventoryItem(ICommand c, Guid characterId)
            => c is RemoveInventoryItem r && r.CharacterId == characterId;

        private static (SagaCoordinator coordinator, InMemorySagaStateRepository stateRepository, Mock<ICommandBus> commandBus, CharacterProjection characterProjection)
    CreateSut()
        {
            var stateRepository = new InMemorySagaStateRepository();
            var registry = new SagaRegistry();
            var commandBusMock = new Mock<ICommandBus>();
            var eventBusMock = new Mock<IEventBus>();

            // Заглушка для ICacheProvider (можно использовать NoOpCacheProvider из Infrastructure.Caching)
            var cacheProvider = new NoOpCacheProvider();
            var characterProjection = new CharacterProjection(cacheProvider, TimeSpan.FromMinutes(5));

            // Регистрируем фабрики с ПРАВИЛЬНЫМ количеством аргументов (4)
            registry.Register<TradeOfferCreated>(e => new TradeSaga(
                e.OfferId,
                commandBusMock.Object,
                eventBusMock.Object,
                characterProjection
            ));
            registry.Register<TradeOfferAccepted>(e => new TradeSaga(
                e.OfferId,
                commandBusMock.Object,
                eventBusMock.Object,
                characterProjection
            ));
            registry.Register<TradeOfferDeclined>(e => new TradeSaga(
                e.OfferId,
                commandBusMock.Object,
                eventBusMock.Object,
                characterProjection
            ));

            var coordinator = new SagaCoordinator(
                registry,
                stateRepository,
                commandBusMock.Object,
                new InMemoryLockManager(Mock.Of<IServiceProvider>()),
                NullLogger<SagaCoordinator>.Instance
            );

            return (coordinator, stateRepository, commandBusMock, characterProjection);
        }

        private static void SeedCharacterWithItem(CharacterProjection projection, Guid characterId, string itemId, string itemName, int quantity)
        {
            projection.Apply(new CharacterCreated(characterId, "Trader", 10, DateTime.UtcNow));
            projection.Apply(new InventoryItemAdded(characterId, itemId, itemName, quantity));
        }

        [Fact]
        public async Task TradeOfferCreated_InitializesSagaState_AsPending()
        {
            var (coordinator, stateRepository, _, characterProjection) = CreateSut();
            var offerId = Guid.NewGuid();
            var fromId = Guid.NewGuid();
            var toId = Guid.NewGuid();
            SeedCharacterWithItem(characterProjection, fromId, "sword-1", "Iron Sword", 1);
            SeedCharacterWithItem(characterProjection, toId, "shield-1", "Wooden Shield", 1);

            await coordinator.DispatchAsync(new TradeOfferCreated(
                offerId, fromId, toId,
                OfferedItems: [new TradeItem("sword-1", "Iron Sword", 1)], OfferedGold: 0,
                RequestedItems: [new TradeItem("shield-1", "Wooden Shield", 1)], RequestedGold: 0,
                OccurredOn: DateTime.UtcNow));

            var state = await stateRepository.LoadAsync(offerId);
            Assert.NotNull(state);
            Assert.Equal(offerId, state!.SagaId);
            Assert.Equal(SagaStatus.Started, state.Status);
        }

        [Fact]
        public async Task TradeOfferAccepted_WithSufficientItems_CompletesSuccessfully()
        {
            var (coordinator, stateRepository, commandBus, characterProjection) = CreateSut();
            var offerId = Guid.NewGuid();
            var fromId = Guid.NewGuid();
            var toId = Guid.NewGuid();
            SeedCharacterWithItem(characterProjection, fromId, "sword-1", "Iron Sword", 1);
            SeedCharacterWithItem(characterProjection, toId, "shield-1", "Wooden Shield", 1);

            await coordinator.DispatchAsync(new TradeOfferCreated(
                offerId, fromId, toId,
                OfferedItems: [new TradeItem("sword-1", "Iron Sword", 1)], OfferedGold: 0,
                RequestedItems: [new TradeItem("shield-1", "Wooden Shield", 1)], RequestedGold: 0,
                OccurredOn: DateTime.UtcNow));

            await coordinator.DispatchAsync(new TradeOfferAccepted(offerId, DateTime.UtcNow));

            var state = await stateRepository.LoadAsync(offerId);
            Assert.NotNull(state);
            Assert.Equal(SagaStatus.Completed, state!.Status);

            // Обе стороны должны были получить встречный предмет: 4 команды AddInventoryItem/
            // RemoveInventoryItem с каждой стороны (списание своего + начисление чужого).
            commandBus.Verify(cb => cb.SendAsync(
                It.Is<ICommand>(c => IsRemoveInventoryItem(c, fromId, "sword-1")),
                It.IsAny<CommandContext>()), Times.Once);
            commandBus.Verify(cb => cb.SendAsync(
                It.Is<ICommand>(c => IsAddInventoryItem(c, toId, "sword-1")),
                It.IsAny<CommandContext>()), Times.Once);
            commandBus.Verify(cb => cb.SendAsync(
                It.Is<ICommand>(c => IsRemoveInventoryItem(c, toId, "shield-1")),
                It.IsAny<CommandContext>()), Times.Once);
            commandBus.Verify(cb => cb.SendAsync(
                It.Is<ICommand>(c => IsAddInventoryItem(c, fromId, "shield-1")),
                It.IsAny<CommandContext>()), Times.Once);
        }

        [Fact]
        public async Task TradeOfferAccepted_WithInsufficientItems_Fails_AndDoesNotDebitAnyone()
        {
            var (coordinator, stateRepository, commandBus, characterProjection) = CreateSut();
            var offerId = Guid.NewGuid();
            var fromId = Guid.NewGuid();
            var toId = Guid.NewGuid();
            SeedCharacterWithItem(characterProjection, fromId, "sword-1", "Iron Sword", 1);
            // toId НЕ владеет запрошенным предметом — сделка должна провалиться до списаний.
            characterProjection.Apply(new CharacterCreated(toId, "Trader 2", 10, DateTime.UtcNow));

            await coordinator.DispatchAsync(new TradeOfferCreated(
                offerId, fromId, toId,
                OfferedItems: [new TradeItem("sword-1", "Iron Sword", 1)], OfferedGold: 0,
                RequestedItems: [new TradeItem("shield-1", "Wooden Shield", 1)], RequestedGold: 0,
                OccurredOn: DateTime.UtcNow));

            await coordinator.DispatchAsync(new TradeOfferAccepted(offerId, DateTime.UtcNow));

            var state = await stateRepository.LoadAsync(offerId);
            Assert.NotNull(state);
            Assert.Equal(SagaStatus.Failed, state!.Status);

            // Ни одна сторона не должна была потерять свой предмет при неуспешной сделке.
            commandBus.Verify(cb => cb.SendAsync(
                It.Is<ICommand>(c => IsRemoveInventoryItem(c, fromId)),
                It.IsAny<CommandContext>()), Times.Never);
        }

        [Fact]
        public async Task TradeOfferDeclined_MarksSagaAsCancelled_WithoutMovingItems()
        {
            var (coordinator, stateRepository, commandBus, characterProjection) = CreateSut();
            var offerId = Guid.NewGuid();
            var fromId = Guid.NewGuid();
            var toId = Guid.NewGuid();
            SeedCharacterWithItem(characterProjection, fromId, "sword-1", "Iron Sword", 1);
            SeedCharacterWithItem(characterProjection, toId, "shield-1", "Wooden Shield", 1);

            await coordinator.DispatchAsync(new TradeOfferCreated(
                offerId, fromId, toId,
                OfferedItems: [new TradeItem("sword-1", "Iron Sword", 1)], OfferedGold: 0,
                RequestedItems: [new TradeItem("shield-1", "Wooden Shield", 1)], RequestedGold: 0,
                OccurredOn: DateTime.UtcNow));

            await coordinator.DispatchAsync(new TradeOfferDeclined(offerId, DateTime.UtcNow));

            var state = await stateRepository.LoadAsync(offerId);
            Assert.NotNull(state);
            Assert.Equal(SagaStatus.Cancelled, state!.Status);
            commandBus.Verify(cb => cb.SendAsync(It.IsAny<ICommand>(), It.IsAny<CommandContext>()), Times.Never);
        }

        [Fact]
        public async Task TwoIndependentOffers_TrackSeparateState_ByOfferId()
        {
            // Регрессионный тест на исходную проблему: раньше один TradeSaga-инстанс вёл сразу
            // много сделок в своём _activeTrades. Теперь каждая сделка обязана иметь собственное,
            // независимое состояние в ISagaStateRepository, адресуемое по OfferId.
            var (coordinator, stateRepository, _, characterProjection) = CreateSut();
            var offerA = Guid.NewGuid();
            var offerB = Guid.NewGuid();
            var fromA = Guid.NewGuid();
            var toA = Guid.NewGuid();
            var fromB = Guid.NewGuid();
            var toB = Guid.NewGuid();
            SeedCharacterWithItem(characterProjection, fromA, "item-a", "Item A", 1);
            SeedCharacterWithItem(characterProjection, toA, "item-b", "Item B", 1);
            SeedCharacterWithItem(characterProjection, fromB, "item-c", "Item C", 1);
            SeedCharacterWithItem(characterProjection, toB, "item-d", "Item D", 1);

            await coordinator.DispatchAsync(new TradeOfferCreated(
                offerA, fromA, toA,
                OfferedItems: [new TradeItem("item-a", "Item A", 1)], OfferedGold: 0,
                RequestedItems: [new TradeItem("item-b", "Item B", 1)], RequestedGold: 0,
                OccurredOn: DateTime.UtcNow));
            await coordinator.DispatchAsync(new TradeOfferCreated(
                offerB, fromB, toB,
                OfferedItems: [new TradeItem("item-c", "Item C", 1)], OfferedGold: 0,
                RequestedItems: [new TradeItem("item-d", "Item D", 1)], RequestedGold: 0,
                OccurredOn: DateTime.UtcNow));

            var stateA = await stateRepository.LoadAsync(offerA);
            var stateB = await stateRepository.LoadAsync(offerB);

            Assert.NotNull(stateA);
            Assert.NotNull(stateB);
            Assert.NotEqual(stateA!.SagaId, stateB!.SagaId);
        }
    }
}
