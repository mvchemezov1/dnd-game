#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace dnd_game.application.projections
{
    /// <summary>
    /// Методы глубокого копирования DTO проекций без использования сериализации.
    /// </summary>
    public static class ProjectionCloner
    {
        public static QuestInfo CloneQuest(QuestInfo source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new QuestInfo
            {
                QuestId = source.QuestId,
                CampaignId = source.CampaignId,
                Title = source.Title,
                Description = source.Description,
                Status = source.Status,
                Objectives = source.Objectives.Select(CloneObjective).ToList(),
                Rewards = source.Rewards.Select(CloneReward).ToList(),
                IssuedAt = source.IssuedAt,
                CompletedAt = source.CompletedAt
            };
        }

        public static QuestObjective CloneObjective(QuestObjective source)
        {
            return new QuestObjective
            {
                Description = source.Description,
                IsCompleted = source.IsCompleted,
                CurrentProgress = source.CurrentProgress,
                RequiredProgress = source.RequiredProgress
            };
        }

        public static QuestReward CloneReward(QuestReward source)
        {
            return new QuestReward
            {
                Description = source.Description,
                ExperiencePoints = source.ExperiencePoints,
                ItemIds = new List<string>(source.ItemIds),
                Gold = source.Gold,
                FactionReputationChange = source.FactionReputationChange
            };
        }

        public static FactionState CloneFaction(FactionState source)
        {
            return new FactionState
            {
                FactionId = source.FactionId,
                Name = source.Name,
                Reputation = source.Reputation
            };
        }

        public static CampaignState CloneCampaignState(CampaignState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new CampaignState
            {
                CampaignId = source.CampaignId,
                CampaignName = source.CampaignName,
                CurrentAct = source.CurrentAct,
                Day = source.Day,
                Hour = source.Hour,
                Minute = source.Minute,
                Weather = source.Weather,
                DiscoveredRegions = new List<string>(source.DiscoveredRegions),
                GlobalFlags = new Dictionary<string, string>(source.GlobalFlags)
            };
        }

        // Для CombatProjection, если потребуется:
        public static CombatStatusDto CloneCombatStatus(CombatStatusDto source)
        {
            return new CombatStatusDto
            {
                CombatId = source.CombatId,
                IsActive = source.IsActive,
                Round = source.Round,
                CurrentTurnIndex = source.CurrentTurnIndex,
                PlayerCharacterIds = new List<Guid>(source.PlayerCharacterIds),
                Participants = source.Participants.Select(CloneCombatParticipant).ToList()
            };
        }

        public static CombatParticipantDto CloneCombatParticipant(CombatParticipantDto source)
        {
            return new CombatParticipantDto
            {
                CharacterId = source.CharacterId,
                Name = source.Name,
                Initiative = source.Initiative,
                IsCurrentTurn = source.IsCurrentTurn,
                HasAction = source.HasAction,
                HasBonusAction = source.HasBonusAction,
                HasReaction = source.HasReaction,
                HasMovement = source.HasMovement,
                MovementRemaining = source.MovementRemaining,
                Conditions = new List<string>(source.Conditions),
                Concentrating = source.Concentrating,
                ReadyActionType = source.ReadyActionType,
                ReadyTriggerCondition = source.ReadyTriggerCondition,
                HasReadiedAction = source.HasReadiedAction,
                CurrentHitPoints = source.CurrentHitPoints,
                MaxHitPoints = source.MaxHitPoints,
                TemporaryHitPoints = source.TemporaryHitPoints,
                ArmorClass = source.ArmorClass
            };
        }
    }
}