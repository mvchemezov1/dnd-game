// tests/unit/CommandHandlers/CombatHandlerTests.cs
using dnd_game.application.command_handlers;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;
using Moq;
using Xunit;

namespace dnd_game.tests.unit.command_handlers;

public class CombatHandlerTests
{
    private readonly Mock<IEventStore> _eventStoreMock;
    private readonly CombatHandler _handler;

    public CombatHandlerTests()
    {
        _eventStoreMock = new Mock<IEventStore>();
        _handler = new CombatHandler(_eventStoreMock.Object);
    }

    [Fact]
    public async Task StartCombat_CreatesNewAggregateAndSaves()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var participants = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var command = new StartCombat(combatId, participants);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _eventStoreMock.Verify(es => es.Save(
            It.Is<CombatAggregate>(c => c.Id == combatId && c.Participants.Count == 2),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EndCombat_LoadsCombat_EndsCombat_Saves()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var participants = new[] { (Guid.NewGuid(), 30), (Guid.NewGuid(), 30) };
        var combat = new CombatAggregate(combatId, participants);
        combat.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CombatAggregate>(combatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(combat);

        var command = new EndCombat(combatId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(combat.IsActive);
        _eventStoreMock.Verify(es => es.Save(combat, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RollInitiative_LoadsCombat_UpdatesInitiative_Saves()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var combat = new CombatAggregate(combatId, new[] { (participantId, 30), (Guid.NewGuid(), 30) });
        combat.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CombatAggregate>(combatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(combat);

        var command = new RollInitiative(combatId, participantId, 15, 2);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var participant = combat.Participants.First(p => p.CharacterId == participantId);
        Assert.Equal(15, participant.Initiative);
        Assert.Equal(2, participant.DexterityModifier);
        _eventStoreMock.Verify(es => es.Save(combat, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NextTurn_LoadsCombat_AdvancesTurn_Saves()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var combat = new CombatAggregate(combatId, new[] { (p1, 30), (p2, 30) });
        combat.RollInitiative(p1, 10, 1);
        combat.RollInitiative(p2, 20, 2);
        combat.StartRound(); // начинает раунд, первый ход у p2
        combat.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CombatAggregate>(combatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(combat);

        var command = new NextTurn(combatId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        // После NextTurn должен начаться ход следующего участника (p1)
        // Проверим, что текущий ход установлен на p1
        Assert.True(combat.Participants.First(p => p.CharacterId == p1).IsCurrentTurn);
        _eventStoreMock.Verify(es => es.Save(combat, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CombatNotFound_ThrowsInvalidAction()
    {
        // Arrange
        var combatId = Guid.NewGuid();
        _eventStoreMock
            .Setup(es => es.Load<CombatAggregate>(combatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CombatAggregate?)null);

        var command = new EndCombat(combatId);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidAction>(() => _handler.Handle(command, CancellationToken.None));
        _eventStoreMock.Verify(es => es.Save(It.IsAny<CombatAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Аналогично для:
    // - StartRound
    // - EndRound
    // - AddParticipantToCombat
    // - RemoveParticipantFromCombat
    // - TakeMoveAction
    // - TakeStandardAction
    // - TakeBonusAction
    // - TakeReaction
    // - ReadyAction
    // - TriggerReadyAction
    // - DealDamageToTarget
    // - HealTarget
    // - ApplyConditionToTarget
    // - RemoveConditionFromTarget
    // - MakeSavingThrowInCombat
    // - MakeDeathSavingThrowInCombat
    // - StabilizeInCombat
    // - MakeConcentrationCheck
    // - DelayTurn
    // - SurrenderInCombat
    // - PerformAction
}