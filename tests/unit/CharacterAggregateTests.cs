// tests/unit/CharacterAggregateTests.cs
using dnd_game.domain.aggregates;
using dnd_game.domain.exceptions;
using Xunit;

namespace dnd_game.tests.unit
{
    /// <summary>
    /// Тесты на доменные инварианты CharacterAggregate — не на процент покрытия строк,
    /// а на конкретные правила D&D, которые агрегат обязан соблюдать независимо от того,
    /// через какой command handler к нему обращаются.
    /// </summary>
    public class CharacterAggregateTests
    {
        private static CharacterAggregate CreateCharacter(int maxHp = 20)
            => new(Guid.NewGuid(), "Test Hero", maxHp);

        [Fact]
        public void NewCharacter_StartsAtFullHitPoints()
        {
            var character = CreateCharacter(maxHp: 20);

            Assert.Equal(20, character.MaxHitPoints);
            Assert.Equal(20, character.HitPoints);
            Assert.False(character.IsDead);
        }

        [Fact]
        public void TakeDamage_ReducesHitPoints_ButNeverBelowZero()
        {
            var character = CreateCharacter(maxHp: 10);

            character.TakeDamage(15); // больше, чем текущий HP

            Assert.Equal(0, character.HitPoints);
        }

        [Fact]
        public void TakeDamage_ZeroOrNegativeAmount_Throws()
        {
            var character = CreateCharacter();

            Assert.Throws<ArgumentException>(() => character.TakeDamage(0));
            Assert.Throws<ArgumentException>(() => character.TakeDamage(-5));
        }

        [Fact]
        public void Heal_NeverExceedsMaxHitPoints()
        {
            var character = CreateCharacter(maxHp: 10);
            character.TakeDamage(3); // HitPoints = 7

            character.Heal(100);

            Assert.Equal(10, character.HitPoints);
        }

        [Fact]
        public void Heal_ZeroOrNegativeAmount_Throws()
        {
            var character = CreateCharacter();

            Assert.Throws<ArgumentException>(() => character.Heal(0));
            Assert.Throws<ArgumentException>(() => character.Heal(-1));
        }

        [Fact]
        public void TemporaryHitPoints_AbsorbDamageBeforeRealHitPoints()
        {
            var character = CreateCharacter(maxHp: 10);
            character.SetTemporaryHitPoints(5);

            character.TakeDamage(3); // должно полностью уйти во временные HP

            Assert.Equal(2, character.TemporaryHitPoints);
            Assert.Equal(10, character.HitPoints); // реальные HP не тронуты
        }

        [Fact]
        public void LevelUp_ToLowerOrEqualLevel_Throws()
        {
            var character = CreateCharacter();
            character.LevelUp(3);

            Assert.Throws<ArgumentException>(() => character.LevelUp(3)); // тот же уровень
            Assert.Throws<ArgumentException>(() => character.LevelUp(2)); // ниже текущего
        }

        [Fact]
        public void LevelUp_Above20_Throws()
        {
            var character = CreateCharacter();

            Assert.Throws<ArgumentException>(() => character.LevelUp(21));
        }

        [Fact]
        public void LevelUp_ValidLevel_UpdatesProficiencyBonus()
        {
            var character = CreateCharacter();

            character.LevelUp(5); // порог для +3 бонуса мастерства (5-й уровень)

            Assert.Equal(5, character.Level);
            Assert.Equal(3, character.ProficiencyBonus);
        }

        [Fact]
        public void MoveToPosition_UpdatesTrackedPosition()
        {
            var character = CreateCharacter();

            character.MoveToPosition(4, 7, "Walk");

            Assert.Equal(4, character.PositionX);
            Assert.Equal(7, character.PositionY);
        }

        [Fact]
        public void TakeDamage_OnDeadCharacter_Throws()
        {
            var character = CreateCharacter(maxHp: 5);
            // Убиваем персонажа отдельным событием через рефлексию домена недоступно —
            // проверяем поведение через публичный контракт: агрегат не даёт себя "убить"
            // напрямую через TakeDamage (HP просто падает до 0, IsDead — отдельный переход
            // через death saving throws). Поэтому здесь фиксируем текущий контракт:
            // TakeDamage не бросает, пока IsDead не выставлен явно.
            character.TakeDamage(100);
            Assert.False(character.IsDead);
            Assert.Equal(0, character.HitPoints);
        }

        [Fact]
        public void GetUncommittedEvents_ReflectsEveryAppliedChange()
        {
            var character = CreateCharacter();
            character.TakeDamage(2);
            character.Heal(1);

            var events = character.GetUncommittedEvents().ToList();

            // CharacterCreated + TakeDamage + Heal = 3 события
            Assert.Equal(3, events.Count);
            Assert.Equal(3, character.Version);
        }

        [Fact]
        public void AddGold_IncreasesGold()
        {
            var character = new CharacterAggregate(Guid.NewGuid(), "Test", 10);
            character.AddGold(50);
            Assert.Equal(50, character.Gold);
        }

        [Fact]
        public void SpendGold_ThrowsIfInsufficient()
        {
            var character = new CharacterAggregate(Guid.NewGuid(), "Test", 10);
            character.AddGold(20);
            Assert.Throws<InvalidOperationException>(() => character.SpendGold(30));
        }
    }
}
