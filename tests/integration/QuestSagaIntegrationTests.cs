// tests/integration/QuestSagaIntegrationTests.cs
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.message_bus;
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

        // Создаём кампанию и персонажа
        var campaign = new CampaignAggregate(campaignId, "Test Campaign", Guid.NewGuid());
        await EventStore.Save(campaign, CancellationToken.None);


        var character = new CharacterAggregate(characterId, "Hero", 20);
        await EventStore.Save(character, CancellationToken.None);


        // Создаём квест с двумя целями
        var objectives = new List<QuestObjectiveData>
        {
            new QuestObjectiveData { Description = "Kill 5 goblins", RequiredProgress = 5, CurrentProgress = 0 },
            new QuestObjectiveData { Description = "Find the amulet", RequiredProgress = 1, CurrentProgress = 0 }
        };
        var rewards = new List<QuestRewardData>
        {
            new QuestRewardData { Description = "Gold reward", Gold = 100, ExperiencePoints = 50 }
        };

        var createQuestEvent = new QuestCreated(
            campaignId, questId, "Goblin Slayer", "Kill goblins and find amulet",
            objectives, rewards, participantIds, DateTime.UtcNow);
        await PublishAndDispatch(createQuestEvent);

        // Принимаем квест
        var acceptEvent = new QuestAccepted();
        await PublishAndDispatch(acceptEvent);

        // Проверяем, что квест активен
        var quests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Active);
        Assert.Contains(quests, q => q.QuestId == questId);

        // Обновляем цели до завершения
        var update1 = new QuestObjectiveUpdated(campaignId, questId, 0, true, 5);
        await PublishAndDispatch(update1);

        // Проверяем, что квест ещё не завершён (вторая цель не выполнена)
        var activeQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Active);
        Assert.Contains(activeQuests, q => q.QuestId == questId);

        var update2 = new QuestObjectiveUpdated(campaignId, questId, 1, true, 1);
        await PublishAndDispatch(update2);

        // Теперь квест должен быть завершён
        var completedQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Completed);
        Assert.Contains(completedQuests, q => q.QuestId == questId);

        // Проверяем, что награды выданы (персонаж получил золото и опыт)
        var characterDto = await CharacterProjection.GetById(characterId);
        Assert.NotNull(characterDto);
        Assert.Equal(100, characterDto.Gold);
        Assert.Equal(50, characterDto.ExperiencePoints); // начальный опыт 0 + 50
    }

    [Fact]
    public async Task Quest_FailsWhenCharacterDies()
    {
        // Arrange
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
            campaignId, questId, "Dangerous Quest", "Survive",
            objectives, rewards, new List<Guid> { characterId }, DateTime.UtcNow);
        await PublishAndDispatch(createQuestEvent);

        var acceptEvent = new QuestAccepted();
        await PublishAndDispatch(acceptEvent);

        // Персонаж умирает
        var deathEvent = new CharacterDied(characterId, DateTime.UtcNow);
        await PublishAndDispatch(deathEvent);

        // Квест должен быть провален
        var failedQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Failed);
        Assert.Contains(failedQuests, q => q.QuestId == questId);
    }

    [Fact]
    public async Task Quest_DoesNotCompleteIfObjectivesNotFullyMet()
    {
        // Arrange
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

        var acceptEvent = new QuestAccepted();
        await PublishAndDispatch(acceptEvent);

        // Обновляем цель, но не до конца
        var update = new QuestObjectiveUpdated(campaignId, questId, 0, false, 3);
        await PublishAndDispatch(update);

        // Квест должен оставаться активным
        var activeQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Active);
        Assert.Contains(activeQuests, q => q.QuestId == questId);
        var completedQuests = await CampaignProjection.GetQuests(campaignId, QuestStatus.Completed);
        Assert.DoesNotContain(completedQuests, q => q.QuestId == questId);
    }
}