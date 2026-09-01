// tests/unit/CommandHandlers/CharacterHandlerTests.cs
using dnd_game.application.command_handlers;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;
using Moq;
using Xunit;

namespace dnd_game.tests.unit.command_handlers;

public class CharacterHandlerTests
{
    private readonly Mock<IEventStore> _eventStoreMock;
    private readonly CharacterHandler _handler;

    public CharacterHandlerTests()
    {
        _eventStoreMock = new Mock<IEventStore>();
        _handler = new CharacterHandler(_eventStoreMock.Object);
    }

    [Fact]
    public async Task CreateCharacter_CreatesNewAggregateAndSaves()
    {
        // Arrange
        var command = new CreateCharacter(Guid.NewGuid(), "Test Hero", 20);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _eventStoreMock.Verify(es => es.Save(
            It.Is<CharacterAggregate>(a => a.Name == "Test Hero" && a.MaxHitPoints == 20),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DealDamage_LoadsCharacter_AppliesDamage_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.ClearUncommittedEvents(); // сбрасываем события создания, чтобы не мешали

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new DealDamage(characterId, 5);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _eventStoreMock.Verify(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()), Times.Once);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(15, character.HitPoints); // 20 - 5
    }

    [Fact]
    public async Task DealDamage_CharacterNotFound_ThrowsInvalidAction()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterAggregate?)null);

        var command = new DealDamage(characterId, 5);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidAction>(() => _handler.Handle(command, CancellationToken.None));
        _eventStoreMock.Verify(es => es.Save(It.IsAny<CharacterAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HealCharacter_LoadsCharacter_AppliesHeal_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.TakeDamage(10); // HP = 10
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new HealCharacter(characterId, 5);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(15, character.HitPoints);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddGold_LoadsCharacter_AddsGold_Saves()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new AddGold(characterId, 50);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(50, character.Gold);
        _eventStoreMock.Verify(es => es.Save(character, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SpendGold_InsufficientGold_ThrowsInvalidOperation()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        character.AddGold(20);
        character.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CharacterAggregate>(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var command = new SpendGold(characterId, 50);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(command, CancellationToken.None));
        _eventStoreMock.Verify(es => es.Save(It.IsAny<CharacterAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Аналогично можно написать тесты для:
    // - UpdateCharacter
    // - GainExperience
    // - LevelUpCharacter
    // - SetAbilityScore
    // - EquipItem / UnequipItem
    // - AddSpell / RemoveSpell
    // - CastSpell
    // - TakeShortRest / TakeLongRest
    // - ApplyCondition / RemoveCondition
    // - MakeSavingThrow
    // - DeathSavingThrow
    // - StabilizeCharacter
    // - UpdateTemporaryHitPoints
    // - UpdateArmorClass / UpdateSpeed
    // - UpdateProficiencyBonus
    // - ChooseRace / ChooseClass / ChooseBackground
    // - AddSkillProficiency / RemoveSkillProficiency
    // - AddSavingThrowProficiency / RemoveSavingThrowProficiency
    // - AddFeat / RemoveFeat
    // - PrepareSpell / UnprepareSpell
    // - UseClassFeature / RechargeFeature
    // - AttuneItem / UnattuneItem
    // - AddResistance / RemoveResistance
    // - AddVulnerability / RemoveVulnerability
    // - AddImmunity / RemoveImmunity
    // - ReviveCharacter
    // - ResetDeathSavingThrows
    // - ClearAllConditionsCommand
    // - SetGoldCommand
}