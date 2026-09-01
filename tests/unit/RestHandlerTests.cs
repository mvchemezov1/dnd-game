// tests/unit/CommandHandlers/RestHandlerTests.cs
using dnd_game.application.command_handlers;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;
using Moq;
using Xunit;

namespace dnd_game.tests.unit.command_handlers;

public class RestHandlerTests
{
    private readonly Mock<IEventStore> _eventStoreMock;
    private readonly RestHandler _handler;

    public RestHandlerTests()
    {
        _eventStoreMock = new Mock<IEventStore>();
        _handler = new RestHandler(_eventStoreMock.Object);
    }

    [Fact]
    public async Task StartRest_LoadsCharacter_StartsRest_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new StartRest(characterId, "Short");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var events = character.GetUncommittedEvents().ToList();
        Assert.Contains(events, e => e is RestStarted);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EndRest_CompletesRest_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.StartRest("Short");
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new EndRest(characterId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var events = character.GetUncommittedEvents().ToList();
        Assert.Contains(events, e => e is RestCompleted);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SpendHitDie_LoadsCharacter_SpendsHitDie_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        // Устанавливаем кости хитов (например, 4d8)
        character.SetHitDice(new Dictionary<int, int> { { 8, 4 } });
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new SpendHitDie(characterId, 8, 5, 2);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var events = character.GetUncommittedEvents().ToList();
        Assert.Contains(events, e => e is HitDieSpent);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CharacterNotFound_ThrowsInvalidAction()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterAggregate?)null);

        var command = new StartRest(characterId, "Short");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidAction>(() => _handler.Handle(command, CancellationToken.None));
        _eventStoreMock.Verify(es => es.Save(It.IsAny<CharacterAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Аналогично для InterruptRest
}