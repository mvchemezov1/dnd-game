// tests/unit/CombatAggregateTests.cs
using dnd_game.domain.aggregates;
using Xunit;

namespace dnd_game.tests.unit
{
    /// <summary>
    /// Тесты на доменные инварианты CombatAggregate: порядок ходов, бюджет движения за ход,
    /// запрет действий вне боя/вне своего хода — то, что реально ломает игру, если сломано.
    /// </summary>
    public class CombatAggregateTests
    {
        private static (CombatAggregate combat, Guid p1, Guid p2) CreateActiveCombatWithTwoParticipants()
        {
            var p1 = Guid.NewGuid();
            var p2 = Guid.NewGuid();
            // Передаём участников с базовой скоростью 30
            var participants = new[] { (p1, 30), (p2, 30) };
            var combat = new CombatAggregate(Guid.NewGuid(), participants);
            return (combat, p1, p2);
        }

        [Fact]
        public void NewCombat_IsActive_WithGivenParticipants()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();

            Assert.True(combat.IsActive);
            Assert.Equal(2, combat.Participants.Count);
            Assert.Contains(combat.Participants, p => p.CharacterId == p1);
            Assert.Contains(combat.Participants, p => p.CharacterId == p2);
        }

        [Fact]
        public void StartRound_WithoutAllInitiativeRolled_Throws()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();
            combat.RollInitiative(p1, 15, 2);
            // p2 инициативу не бросал — Initiative остаётся 0

            Assert.Throws<InvalidOperationException>(() => combat.StartRound());
        }

        [Fact]
        public void StartRound_OrdersParticipantsByInitiativeDescending()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();
            combat.RollInitiative(p1, 10, 1);
            combat.RollInitiative(p2, 20, 3);

            combat.StartRound();

            Assert.Equal(p2, combat.Participants[0].CharacterId); // выше инициатива — первый
            Assert.Equal(p1, combat.Participants[1].CharacterId);
        }

        [Fact]
        public void UseMovement_MoreThanRemaining_Throws()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();
            combat.RollInitiative(p1, 10, 1);
            combat.RollInitiative(p2, 20, 3);
            combat.StartRound(); // GetSpeed(characterId) для каждого участника

            Assert.Throws<InvalidOperationException>(() => combat.UseMovement(p1, 35));
        }

        [Fact]
        public void UseMovement_WithinBudget_ReducesMovementRemaining()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();
            combat.RollInitiative(p1, 10, 1);
            combat.RollInitiative(p2, 20, 3);
            combat.StartRound();

            combat.UseMovement(p1, 20);

            var participant = combat.Participants.Single(p => p.CharacterId == p1);
            Assert.Equal(10, participant.MovementRemaining);
        }

        [Fact]
        public void UseMovement_AccumulatesAcrossMultipleCalls_AndEventuallyThrows()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();
            combat.RollInitiative(p1, 10, 1);
            combat.RollInitiative(p2, 20, 3);
            combat.StartRound();

            combat.UseMovement(p1, 15);
            combat.UseMovement(p1, 15); // ровно 30 — должно пройти

            var participant = combat.Participants.Single(p => p.CharacterId == p1);
            Assert.Equal(0, participant.MovementRemaining);

            Assert.Throws<InvalidOperationException>(() => combat.UseMovement(p1, 1)); // бюджет исчерпан
        }

        [Fact]
        public void EndTurn_WhenNotCurrentTurn_Throws()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();
            combat.RollInitiative(p1, 10, 1);
            combat.RollInitiative(p2, 20, 3);
            combat.StartRound();
            combat.StartTurn(p2); // ход p2, не p1

            Assert.Throws<InvalidOperationException>(() => combat.EndTurn(p1));
        }

        [Fact]
        public void ActionsOnInactiveCombat_Throw()
        {
            var (combat, p1, p2) = CreateActiveCombatWithTwoParticipants();
            combat.RollInitiative(p1, 10, 1);
            combat.RollInitiative(p2, 20, 3);
            combat.StartRound();
            combat.EndCombat();

            Assert.Throws<InvalidOperationException>(() => combat.RollInitiative(p1, 5, 1));
            Assert.Throws<InvalidOperationException>(() => combat.UseAction(p1));
            Assert.Throws<InvalidOperationException>(() => combat.EndCombat()); // уже закончен
        }

        [Fact]
        public void RemoveParticipant_UnknownCharacter_Throws()
        {
            var (combat, _, _) = CreateActiveCombatWithTwoParticipants();

            Assert.Throws<InvalidOperationException>(() => combat.RemoveParticipant(Guid.NewGuid()));
        }
    }
}
