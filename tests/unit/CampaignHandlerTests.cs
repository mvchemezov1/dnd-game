// tests/unit/CommandHandlers/CampaignHandlerTests.cs
using dnd_game.application.command_handlers;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;
using Moq;
using Xunit;

namespace dnd_game.tests.unit.command_handlers;

public class CampaignHandlerTests
{
    private readonly Mock<IEventStore> _eventStoreMock;
    private readonly CampaignHandler _handler;

    public CampaignHandlerTests()
    {
        _eventStoreMock = new Mock<IEventStore>();
        _handler = new CampaignHandler(_eventStoreMock.Object);
    }

    // Вспомогательный метод создания списка целей (хотя бы одна)
    private static List<QuestObjectiveData> CreateTestObjectives()
        => new()
        {
            new QuestObjectiveData { Description = "Test objective", RequiredProgress = 1 }
        };

    [Fact]
    public async Task CreateQuest_CreatesQuestInCampaign_Saves()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var questId = Guid.NewGuid();
        var campaign = new CampaignAggregate(campaignId, "Test Campaign", Guid.NewGuid());
        campaign.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CampaignAggregate>(campaignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(campaign);

        var command = new CreateQuestCommand(
            campaignId,
            questId,
            "Slay the Dragon",
            "Description",
            CreateTestObjectives(),
            new List<QuestRewardData>(),
            new List<Guid>());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Contains(campaign.Quests, q => q.QuestId == questId);
        _eventStoreMock.Verify(es => es.Save(campaign, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptQuest_LoadsCampaign_AcceptsQuest_Saves()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var questId = Guid.NewGuid();
        var campaign = new CampaignAggregate(campaignId, "Test Campaign", Guid.NewGuid());
        campaign.CreateQuest(questId, "Slay the Dragon", "Description", CreateTestObjectives(), new List<QuestRewardData>(), new List<Guid>());
        campaign.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CampaignAggregate>(campaignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(campaign);

        var command = new AcceptQuestCommand(campaignId, questId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Contains(questId, campaign.ActiveQuestIds);
        _eventStoreMock.Verify(es => es.Save(campaign, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteQuest_RemovesQuestFromActive_Saves()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var questId = Guid.NewGuid();
        var campaign = new CampaignAggregate(campaignId, "Test Campaign", Guid.NewGuid());
        campaign.CreateQuest(questId, "Slay the Dragon", "Description", CreateTestObjectives(), new List<QuestRewardData>(), new List<Guid>());
        campaign.AcceptQuest(questId);
        campaign.ClearUncommittedEvents();

        _eventStoreMock
            .Setup(es => es.Load<CampaignAggregate>(campaignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(campaign);

        var command = new CompleteQuestCommand(campaignId, questId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.DoesNotContain(questId, campaign.ActiveQuestIds);
        _eventStoreMock.Verify(es => es.Save(campaign, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CampaignNotFound_ThrowsInvalidAction()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        _eventStoreMock
            .Setup(es => es.Load<CampaignAggregate>(campaignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignAggregate?)null);

        var command = new AcceptQuestCommand(campaignId, Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidAction>(() => _handler.Handle(command, CancellationToken.None));
        _eventStoreMock.Verify(es => es.Save(It.IsAny<CampaignAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}