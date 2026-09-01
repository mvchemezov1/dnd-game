using System;
using System.Collections.Generic;
using dnd_game.domain.aggregates;
using dnd_game.domain.events;
using Xunit;

namespace dnd_game.tests.unit
{
    public class CampaignAggregateTests
    {
        private static CampaignAggregate CreateCampaign()
            => new(Guid.NewGuid(), "Test Campaign", Guid.NewGuid());

        private static void CreateQuest(CampaignAggregate campaign, Guid questId, string title)
        {
            var objectives = new List<QuestObjectiveData>
    {
        new QuestObjectiveData { Description = "Test objective", RequiredProgress = 1 }
    };
            campaign.CreateQuest(questId, title, description: "", objectives: objectives,
                rewards: new List<QuestRewardData>(), participantIds: new List<Guid>());
        }

        [Fact]
        public void NewCampaign_HasNoPlayersOrQuests()
        {
            var campaign = CreateCampaign();
            Assert.Empty(campaign.PlayerIds);
            Assert.Empty(campaign.ActiveQuestIds);
        }

        [Fact]
        public void JoinPlayer_Twice_Throws()
        {
            var campaign = CreateCampaign();
            var playerId = Guid.NewGuid();
            campaign.JoinPlayer(playerId);
            Assert.Throws<InvalidOperationException>(() => campaign.JoinPlayer(playerId));
        }

        [Fact]
        public void LeavePlayer_NotInCampaign_Throws()
        {
            var campaign = CreateCampaign();
            Assert.Throws<InvalidOperationException>(() => campaign.LeavePlayer(Guid.NewGuid()));
        }

        [Fact]
        public void AcceptQuest_NotCreatedFirst_Throws()
        {
            var campaign = CreateCampaign();
            Assert.Throws<InvalidOperationException>(() => campaign.AcceptQuest(Guid.NewGuid()));
        }

        [Fact]
        public void CreateQuest_ThenAccept_AddsToActiveQuests()
        {
            var campaign = CreateCampaign();
            var questId = Guid.NewGuid();
            CreateQuest(campaign, questId, "Slay the Dragon");

            campaign.AcceptQuest(questId);
            Assert.Contains(questId, campaign.ActiveQuestIds);
        }

        [Fact]
        public void AcceptQuest_AlreadyActive_Throws()
        {
            var campaign = CreateCampaign();
            var questId = Guid.NewGuid();
            CreateQuest(campaign, questId, "Slay the Dragon");
            campaign.AcceptQuest(questId);

            Assert.Throws<InvalidOperationException>(() => campaign.AcceptQuest(questId));
        }

        [Fact]
        public void CreateQuest_DuplicateId_Throws()
        {
            var campaign = CreateCampaign();
            var questId = Guid.NewGuid();
            CreateQuest(campaign, questId, "Slay the Dragon");

            Assert.Throws<InvalidOperationException>(() =>
                CreateQuest(campaign, questId, "Slay the Dragon"));
        }

        [Fact]
        public void CompleteQuest_NotAccepted_Throws()
        {
            var campaign = CreateCampaign();
            var questId = Guid.NewGuid();
            CreateQuest(campaign, questId, "Slay the Dragon");

            Assert.Throws<InvalidOperationException>(() => campaign.CompleteQuest(questId));
        }

        [Fact]
        public void CompleteQuest_RemovesFromActiveQuests()
        {
            var campaign = CreateCampaign();
            var questId = Guid.NewGuid();
            CreateQuest(campaign, questId, "Slay the Dragon");
            campaign.AcceptQuest(questId);

            campaign.CompleteQuest(questId);
            Assert.DoesNotContain(questId, campaign.ActiveQuestIds);
        }

        [Fact]
        public void DiscoverRegion_CalledTwice_IsIdempotent()
        {
            var campaign = CreateCampaign();
            campaign.DiscoverRegion("Waterdeep");
            campaign.DiscoverRegion("Waterdeep");
            Assert.Single(campaign.DiscoveredRegions, r => r == "Waterdeep");
        }
    }
}