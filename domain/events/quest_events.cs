#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События кампании: управление игроками, квестами, фракциями, временем, погодой и флагами.
    // Все события, связанные с кампанией, реализуют ICampaignEvent.
    // --------------------------------------------------------------------------------------------

    /// <summary>Кампания создана.</summary>
    public record CampaignCreated(
        Guid CampaignId,
        string Name,
        Guid GameMasterId,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Управление игроками в кампании ----------

    /// <summary>Игрок присоединился к кампании.</summary>
    public record PlayerJoinedCampaign(
        Guid CampaignId,
        Guid PlayerId,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Игрок покинул кампанию.</summary>
    public record PlayerLeftCampaign(
        Guid CampaignId,
        Guid PlayerId,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Квесты: жизненный цикл ----------

    /// <summary>Создан новый квест в кампании.</summary>
    public record QuestCreated(
        Guid CampaignId,
        Guid QuestId,
        string Title,
        string Description,
        List<QuestObjectiveData> Objectives,
        List<QuestRewardData> Rewards,
        List<Guid> ParticipantIds,
        DateTime IssuedAt) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Квест принят участниками (стал активным).</summary>
    public record QuestAccepted(
        Guid CampaignId,
        Guid QuestId,
        List<Guid> ParticipantIds,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Квест успешно завершён.</summary>
    public record QuestCompleted(
        Guid CampaignId,
        Guid QuestId,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Квест провален.</summary>
    public record QuestFailed(
        Guid CampaignId,
        Guid QuestId,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Квест оставлен (отменён).</summary>
    public record QuestAbandoned(
        Guid CampaignId,
        Guid QuestId,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Квест удалён из кампании.</summary>
    public record QuestDeleted(
        Guid CampaignId,
        Guid QuestId,
        DateTime Timestamp) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Цели квеста ----------

    /// <summary>Обновлён прогресс цели квеста.</summary>
    public record QuestObjectiveUpdated(
        Guid CampaignId,
        Guid QuestId,
        int ObjectiveIndex,
        bool IsCompleted,
        int CurrentProgress) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Награды квеста ----------

    /// <summary>Награда за квест получена персонажем.</summary>
    public record QuestRewardClaimed(
        Guid CampaignId,
        Guid QuestId,
        Guid CharacterId,
        int ExperiencePoints,
        int Gold,
        List<string> ItemIds,
        string? FactionReputationChange) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Фракции ----------

    /// <summary>В кампанию добавлена фракция.</summary>
    public record FactionAdded(
        Guid CampaignId,
        string FactionId,
        int InitialReputation) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Изменена репутация фракции.</summary>
    public record FactionReputationChanged(
        Guid CampaignId,
        string FactionId,
        int Change) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Глобальные флаги ----------

    /// <summary>Установлен глобальный флаг кампании.</summary>
    public record GlobalFlagSet(
        Guid CampaignId,
        string FlagName,
        string FlagValue) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Глобальный флаг удалён.</summary>
    public record GlobalFlagRemoved(
        Guid CampaignId,
        string FlagName) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Игровое время и погода ----------

    /// <summary>Игровое время продвинуто вперёд.</summary>
    public record GameTimeAdvanced(
        Guid CampaignId,
        int Minutes) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Погода в кампании изменена.</summary>
    public record WeatherChanged(
        Guid CampaignId,
        string NewWeather) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Исследование регионов ----------

    /// <summary>Открыт новый регион на карте.</summary>
    public record RegionDiscovered(
        Guid CampaignId,
        string RegionName) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Дополнительные события кампании ----------

    /// <summary>Фракция обнаружена (с указанием времени).</summary>
    public record FactionDiscovered(
        Guid CampaignId,
        string FactionId,
        string FactionName,
        DateTime OccurredOn) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    /// <summary>Произошло мировое событие.</summary>
    public record WorldEventTriggered(
        Guid CampaignId,
        string EventName,
        DateTime OccurredOn) : ICampaignEvent
    {
        public Guid AggregateId => CampaignId;
    }

    // ---------- Вспомогательные типы данных ----------

    /// <summary>Данные цели квеста.</summary>
    public class QuestObjectiveData
    {
        public string Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public int CurrentProgress { get; set; }
        public int RequiredProgress { get; set; }
    }

    /// <summary>Данные награды квеста.</summary>
    public class QuestRewardData
    {
        public string Description { get; set; } = string.Empty;
        public int ExperiencePoints { get; set; }
        public List<string> ItemIds { get; set; } = [];
        public int Gold { get; set; }
        public string? FactionReputationChange { get; set; }
    }

    /// <summary>Персонаж получил предмет (универсальное событие).</summary>
    public record ItemAcquired(
        Guid CharacterId,
        string ItemId,
        int Quantity,
        DateTime OccurredOn) : ICharacterEvent
    {
        public Guid AggregateId => CharacterId;
    }
}