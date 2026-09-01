using dnd_game.domain.aggregates;
using Xunit;

namespace dnd_game.tests.unit
{
    public class JourneyAggregateTests
    {
        [Fact]
        public void StartJourney_InitializesStateCorrectly()
        {
            var journey = new JourneyAggregate(Guid.NewGuid(), Guid.NewGuid(), "Normal");
            Assert.True(journey.IsActive);
            Assert.Equal("Normal", journey.Pace);
            Assert.Equal(1, journey.CurrentDay);
            Assert.Equal(10, journey.Resources["Food"]);
        }

        [Fact]
        public void AdvanceDay_IncrementsDay()
        {
            var journey = new JourneyAggregate(Guid.NewGuid(), Guid.NewGuid(), "Normal");
            journey.AdvanceDay("Forest", 8, 10);
            Assert.Equal(2, journey.CurrentDay);
            Assert.Equal(8, journey.CurrentHour);
        }

        [Fact]
        public void ForcedMarch_MoreThan8Hours_AddsExhaustion()
        {
            var journey = new JourneyAggregate(Guid.NewGuid(), Guid.NewGuid(), "Normal");
            journey.ForcedMarch(12);
            Assert.Equal(1, journey.ExhaustionLevel);
        }

        [Fact]
        public void ConsumeResources_DecreasesFoodAndWater()
        {
            var journey = new JourneyAggregate(Guid.NewGuid(), Guid.NewGuid(), "Normal");
            journey.ConsumeResources(2);
            Assert.Equal(8, journey.Resources["Food"]);
            Assert.Equal(8, journey.Resources["Water"]);
        }

        [Fact]
        public void EndJourney_Deactivates()
        {
            var journey = new JourneyAggregate(Guid.NewGuid(), Guid.NewGuid(), "Normal");
            journey.EndJourney();
            Assert.False(journey.IsActive);
        }
    }
}