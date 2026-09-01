#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.application.projections; // для типов QuestInfo, CampaignState, FactionState

namespace dnd_game.domain.queries
{
    // --------------------------------------------------------------------------------------------
    // Запросы, связанные с кампаниями: квесты, состояние, фракции, время, регионы, флаги.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить список идентификаторов активных квестов кампании.
    /// </summary>
    public record GetActiveQuests(Guid CampaignId) : IQuery<List<Guid>>;

    /// <summary>
    /// Получить детальную информацию о конкретном квесте.
    /// </summary>
    public record GetQuestDetails(Guid CampaignId, Guid QuestId) : IQuery<QuestInfo?>;

    /// <summary>
    /// Получить список квестов с фильтрацией по статусу.
    /// </summary>
    public record GetQuestsByStatus(Guid CampaignId, QuestStatus? StatusFilter = null) : IQuery<List<QuestInfo>>;

    /// <summary>
    /// Получить текущее состояние кампании (день, час, погода, флаги, открытые регионы).
    /// </summary>
    public record GetCampaignState(Guid CampaignId) : IQuery<CampaignState?>;

    // --------------------------------------------------------------------------------------------
    // Репутация фракций
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить репутацию конкретной фракции.
    /// </summary>
    public record GetFactionReputation(string FactionId) : IQuery<FactionState?>;

    /// <summary>
    /// Получить список всех известных фракций с их репутацией.
    /// </summary>
    public record GetAllFactions : IQuery<List<FactionState>>;

    // --------------------------------------------------------------------------------------------
    // Мировые события
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить список активных мировых событий кампании.
    /// </summary>
    public record GetActiveWorldEvents(Guid CampaignId) : IQuery<List<string>>;

    // --------------------------------------------------------------------------------------------
    // Игровое время
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить текущее игровое время (день, час, минута).
    /// </summary>
    public record GetCurrentGameTime(Guid CampaignId) : IQuery<GameTimeDto>;

    /// <summary>
    /// DTO игрового времени.
    /// </summary>
    public record GameTimeDto(int Day, int Hour, int Minute);

    // --------------------------------------------------------------------------------------------
    // Регионы
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить список открытых (исследованных) регионов кампании.
    /// </summary>
    public record GetDiscoveredRegions(Guid CampaignId) : IQuery<List<string>>;

    // --------------------------------------------------------------------------------------------
    // Глобальные флаги
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить значение конкретного глобального флага кампании.
    /// </summary>
    public record GetGlobalFlag(Guid CampaignId, string FlagName) : IQuery<string?>;

    /// <summary>
    /// Получить все глобальные флаги кампании в виде словаря «имя → значение».
    /// </summary>
    public record GetAllGlobalFlags(Guid CampaignId) : IQuery<Dictionary<string, string>>;

    // --------------------------------------------------------------------------------------------
    // Поиск квестов с пагинацией
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Выполнить поиск квестов по названию и/или статусу с постраничным выводом.
    /// </summary>
    public record SearchQuests(
        Guid CampaignId,
        string? TitleFilter = null,
        QuestStatus? StatusFilter = null,
        int PageNumber = 1,
        int PageSize = 20
    ) : IQuery<PagedResult<QuestInfo>>;
}