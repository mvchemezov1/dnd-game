#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.domain.events; // QuestObjectiveData и QuestRewardData определены в файле событий

namespace dnd_game.domain.commands
{
    // ---------- Управление квестами ----------

    /// <summary>
    /// Команда создания нового квеста в кампании.
    /// </summary>
    public record CreateQuestCommand(
        Guid CampaignId,
        Guid QuestId,
        string Title,
        string Description,
        List<QuestObjectiveData> Objectives,
        List<QuestRewardData> Rewards,
        List<Guid> ParticipantIds
    ) : ICommand;

    /// <summary>
    /// Команда обновления прогресса конкретной цели квеста.
    /// </summary>
    public record UpdateQuestObjectiveCommand(
        Guid CampaignId,
        Guid QuestId,
        int ObjectiveIndex,
        bool IsCompleted,
        int CurrentProgress
    ) : ICommand;

    /// <summary>
    /// Команда принятия квеста (перевод в активное состояние).
    /// </summary>
    public record AcceptQuestCommand(
        Guid CampaignId,
        Guid QuestId
    ) : ICommand;

    /// <summary>
    /// Команда успешного завершения квеста.
    /// </summary>
    public record CompleteQuestCommand(
        Guid CampaignId,
        Guid QuestId
    ) : ICommand;

    /// <summary>
    /// Команда провала квеста.
    /// </summary>
    public record FailQuestCommand(
        Guid CampaignId,
        Guid QuestId
    ) : ICommand;

    /// <summary>
    /// Команда удаления квеста (административное действие).
    /// </summary>
    public record DeleteQuestCommand(
        Guid CampaignId,
        Guid QuestId
    ) : ICommand;

    // ---------- Глобальные флаги ----------

    /// <summary>
    /// Команда установки глобального флага кампании.
    /// </summary>
    public record SetGlobalFlagCommand(
        Guid CampaignId,
        string FlagName,
        string FlagValue
    ) : ICommand;

    /// <summary>
    /// Команда удаления глобального флага.
    /// </summary>
    public record RemoveGlobalFlagCommand(
        Guid CampaignId,
        string FlagName
    ) : ICommand;

    // ---------- Игровое время и погода ----------

    /// <summary>
    /// Команда продвижения игрового времени на указанное количество минут.
    /// </summary>
    public record AdvanceTimeCommand(
        Guid CampaignId,
        int Minutes
    ) : ICommand;

    /// <summary>
    /// Команда изменения текущей погоды в кампании.
    /// </summary>
    public record ChangeWeatherCommand(
        Guid CampaignId,
        string NewWeather
    ) : ICommand;

    // ---------- Фракции ----------

    /// <summary>
    /// Команда изменения репутации указанной фракции.
    /// </summary>
    public record ChangeFactionReputationCommand(
        Guid CampaignId,
        string FactionId,
        int Change
    ) : ICommand;

    /// <summary>Команда создания новой кампании.</summary>
    public record CreateCampaignCommand(
        Guid CampaignId,
        string Name,
        Guid GameMasterId) : ICommand;

    /// <summary>Команда добавления игрока в кампанию.</summary>
    public record AddPlayerToCampaignCommand(
        Guid CampaignId,
        Guid PlayerId) : ICommand;

    /// <summary>Команда удаления игрока из кампании.</summary>
    public record RemovePlayerFromCampaignCommand(
        Guid CampaignId,
        Guid PlayerId) : ICommand;
}