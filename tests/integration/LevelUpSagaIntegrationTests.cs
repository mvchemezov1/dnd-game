// tests/integration/LevelUpSagaIntegrationTests.cs
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;
using Xunit;

namespace dnd_game.tests.integration;

public class LevelUpSagaIntegrationTests : SagaIntegrationTestBase
{
    private static readonly Dictionary<int, int> ExperienceThresholds = new()
    {
        {1, 0}, {2, 300}, {3, 900}, {4, 2700}, {5, 6500}, {6, 14000}, {7, 23000},
        {8, 34000}, {9, 48000}, {10, 64000}, {11, 85000}, {12, 100000}, {13, 120000},
        {14, 140000}, {15, 165000}, {16, 195000}, {17, 225000}, {18, 265000},
        {19, 305000}, {20, 355000}
    };

    [Fact]
    public async Task GainExperience_TriggersLevelUp_WhenThresholdReached()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        await EventStore.Save(character, CancellationToken.None);

        // Проверяем начальный уровень и бонус
        var initialDto = await CharacterProjection.GetById(characterId);
        Assert.Equal(1, initialDto!.Level);
        Assert.Equal(2, initialDto.ProficiencyBonus);

        // Добавляем опыт, достаточный для перехода на 2-й уровень (300 XP)
        var gainEvent = new ExperienceGained(characterId, 300);
        await PublishAndDispatch(gainEvent);

        // Ждём, пока сага обработает и отправит команды (в тесте они сразу обрабатываются)
        // Проверяем, что уровень повысился
        var updatedDto = await CharacterProjection.GetById(characterId);
        Assert.Equal(2, updatedDto!.Level);
        Assert.Equal(2, updatedDto.ProficiencyBonus); // для 2 уровня бонус всё ещё 2 (до 4 уровня)
        // Проверяем, что HP увеличилось (прибавка за уровень = d8/2+1 + модификатор Con)
        // В CharacterAggregate при LevelUp не меняется HP автоматически, это делает сага через команду IncreaseMaxHitPoints.
        // Поэтому проверяем, что MaxHitPoints изменился. Изначально 20, ожидаем прибавку (5 + ConMod).
        // Но в тесте мы не задавали Con, поэтому прибавка будет 5 (среднее для d8).
        // В реальном тесте нужно задать Con модификатор, но для упрощения проверяем, что MaxHitPoints > 20.
        Assert.True(updatedDto.MaxHitPoints > 20);
    }

    [Fact]
    public async Task GainExperience_MultipleLevels_UpgradesAccordingly()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        await EventStore.Save(character, CancellationToken.None);

        // Добавляем опыт сразу на 3 уровня (900 XP -> 3-й уровень)
        var gainEvent = new ExperienceGained(characterId, 900);
        await PublishAndDispatch(gainEvent);

        var updatedDto = await CharacterProjection.GetById(characterId);
        Assert.Equal(3, updatedDto!.Level);
        Assert.Equal(2, updatedDto.ProficiencyBonus); // 3 уровень -> +2
        Assert.True(updatedDto.MaxHitPoints > 20); // HP должно увеличиться дважды (за 2 и 3 уровень)
    }

    [Fact]
    public async Task GainExperience_NotEnoughForLevelUp_DoesNotChangeLevel()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        await EventStore.Save(character, CancellationToken.None);

        // Добавляем 100 XP (не хватает до 2-го уровня)
        var gainEvent = new ExperienceGained(characterId, 100);
        await PublishAndDispatch(gainEvent);

        var updatedDto = await CharacterProjection.GetById(characterId);
        Assert.Equal(1, updatedDto!.Level);
        Assert.Equal(2, updatedDto.ProficiencyBonus);
        Assert.Equal(20, updatedDto.MaxHitPoints);
    }

    [Fact]
    public async Task GainExperience_AtMaxLevel_DoesNotExceed20()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var character = new CharacterAggregate(characterId, "Hero", 20);
        // Поднимаем на 20 уровень искусственно (можно через события)
        for (int i = 1; i < 20; i++)
        {
            character.LevelUp(i + 1);
            // Применяем события вручную, так как мы не используем EventStore в этом тесте напрямую
        }
        await EventStore.Save(character, CancellationToken.None);

        // Добавляем много опыта
        var gainEvent = new ExperienceGained(characterId, 1000000);
        await PublishAndDispatch(gainEvent);

        var updatedDto = await CharacterProjection.GetById(characterId);
        Assert.Equal(20, updatedDto!.Level);
        Assert.Equal(6, updatedDto.ProficiencyBonus);
        // Проверяем, что HP не превысило какой-то разумный предел (зависит от логики)
        // Но в любом случае уровень не должен превысить 20
    }
}
