// tests/unit/CommandHandlers/MovementHandlerTests.cs
using dnd_game.application.command_handlers;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.world;
using Moq;
using Xunit;

namespace dnd_game.tests.unit.command_handlers;

public class MovementHandlerTests
{
    private readonly Mock<IEventStore> _eventStoreMock;
    private readonly IGridProvider _gridProvider;
    private readonly MovementHandler _handler;

    public MovementHandlerTests()
    {
        _eventStoreMock = new Mock<IEventStore>();
        _gridProvider = new GridProvider(50, 50);
        _handler = new MovementHandler(_eventStoreMock.Object, _gridProvider);
    }

    [Fact]
    public async Task MoveCharacter_LoadsCharacter_Moves_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new MoveCharacter(characterId, 5, 5);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(5, character.PositionX);
        Assert.Equal(5, character.PositionY);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoveCharacter_OutOfBounds_ThrowsInvalidAction()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new MoveCharacter(characterId, 100, 100);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidAction>(() => _handler.Handle(command, CancellationToken.None));
        _eventStoreMock.Verify(es => es.Save(It.IsAny<CharacterAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MoveCharacter_NotEnoughMovement_ThrowsInvalidAction()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.UpdateSpeed(10); // скорость 10 футов
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new MoveCharacter(characterId, 5, 0); // дистанция 25 футов (5 клеток * 5) > 10

        // Act & Assert
        await Assert.ThrowsAsync<InvalidAction>(() => _handler.Handle(command, CancellationToken.None));
        _eventStoreMock.Verify(es => es.Save(It.IsAny<CharacterAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MoveCharacterWithDash_LoadsCharacter_AppliesDash_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new MoveCharacterWithDash(characterId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        // Проверяем, что событие Dash применено (у нас нет публичного свойства, но можно проверить через события)
        var events = character.GetUncommittedEvents().ToList();
        Assert.Contains(events, e => e is CharacterDashed);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Аналогично для:
    // - MoveCharacterWithDisengage
    // - MoveCharacterStealthily
    // - ClimbCharacter
    // - SwimCharacter
    // - FlyCharacter
    // - BurrowCharacter
    // - JumpCharacter
    // - SetCharacterSpeed
    // - ResetCharacterSpeed
    // - ApplyDifficultTerrain
    // - RemoveDifficultTerrain
    // - ApplyMovementImpairment
    // - RemoveMovementImpairment
    // - MakeAthleticsCheckForMovement
    // - MakeAcrobaticsCheckForMovement
    // - TakeFallDamage
}