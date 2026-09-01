using dnd_game.domain.aggregates;
using dnd_game.domain.events;
using Xunit;
using QuestStatus = dnd_game.application.projections.QuestStatus;

namespace dnd_game.tests.integration;

public class QuestSagaIntegrationTests : SagaIntegrationTestBase
{
    [Fact]
    public async Task Quest_CompletesWhenAllObjectivesAreMet()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var questId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var participantIds = new List<Guid> { characterId };

        var campaign = new CampaignAggregate(campaignId, "Test Campaign", Guid.NewGuid());
        await EventStore.Save(campaign, CancellationToken.None);

        var character = new CharacterAggregate(characterId, "Hero", 20);
        await EventStore.Save(character, CancellationToken.None);

        var objectives = new List<QuestObjectiveData>
        {
            new QuestObjectiveData { Description = "Kill 5 goblins", RequiredProgress = 5, CurrentProgress = 0 },
            new QuestObjectiveData { Description = "Find the amulet", RequiredProgress = 1, CurrentProgress = 0 }
        };
        var rewards = new List<QuestRewardData>();

        var createQuestEvent = new QuestCreated(
            campaignId, questId, "Goblin Slayer", "Kill goblins and find amulet",
            objectives, rewards, participantIds, DateTime.UtcNow);
        await PublishAndDispatch(createQuestEvent);

        var acceptEvent = new QuestAccepted(campaignId, questId, participantIds, DateTime.UtcNow);
        await PublishAndDispatch(acceptEvent);

        var quests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Active);
        Assert.Contains(quests, q => q.QuestId == questId);

        var update1 = new QuestObjectiveUpdated(campaignId, questId, 0, true, 5);
        await PublishAndDispatch(update1);

        var activeQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Active);
        Assert.Contains(activeQuests, q => q.QuestId == questId);

        var update2 = new QuestObjectiveUpdated(campaignId, questId, 1, true, 1);
        await PublishAndDispatch(update2);

        var completedQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Completed);
        Assert.Contains(completedQuests, q => q.QuestId == questId);
    }

    [Fact]
    public async Task Quest_DoesNotCompleteIfObjectivesNotFullyMet()
    {
        var campaignId = Guid.NewGuid();
        var questId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var campaign = new CampaignAggregate(campaignId, "Test Campaign", Guid.NewGuid());
        await EventStore.Save(campaign, CancellationToken.None);

        var character = new CharacterAggregate(characterId, "Hero", 20);
        await EventStore.Save(character, CancellationToken.None);

        var objectives = new List<QuestObjectiveData>
        {
            new QuestObjectiveData { Description = "Kill 5 goblins", RequiredProgress = 5, CurrentProgress = 0 }
        };
        var rewards = new List<QuestRewardData>();

        var createQuestEvent = new QuestCreated(
            campaignId, questId, "Goblin Slayer", "Kill goblins",
            objectives, rewards, new List<Guid> { characterId }, DateTime.UtcNow);
        await PublishAndDispatch(createQuestEvent);

        var acceptEvent = new QuestAccepted(campaignId, questId, new List<Guid> { characterId }, DateTime.UtcNow);
        await PublishAndDispatch(acceptEvent);

        var update = new QuestObjectiveUpdated(campaignId, questId, 0, false, 3);
        await PublishAndDispatch(update);

        var activeQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Active);
        Assert.Contains(activeQuests, q => q.QuestId == questId);
        var completedQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Completed);
        Assert.DoesNotContain(completedQuests, q => q.QuestId == questId);
    }
}