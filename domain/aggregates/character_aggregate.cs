using System;
using System.Collections.Generic;
using System.Linq;
using dnd_game.domain.events;
using dnd_game.domain.exceptions;
using dnd_game.domain.rules;
using dnd_game.domain.value_objects;

namespace dnd_game.domain.aggregates
{
    /// <summary>
    /// Агрегат персонажа. Управляет всеми аспектами персонажа: характеристики, бой, заклинания, инвентарь, отдых, смерть и перемещение.
    /// </summary>
    public class CharacterAggregate : AggregateRoot
    {
        // ---------- Основные параметры ----------
        public string Name { get; private set; } = string.Empty;
        public int HitPoints { get; private set; }
        public int MaxHitPoints { get; private set; }
        public int TemporaryHitPoints { get; private set; }
        public int ArmorClass { get; private set; } = 10;
        public int Speed { get; private set; } = 30;
        public int PositionX { get; private set; }
        public int PositionY { get; private set; }
        public int ExperiencePoints { get; private set; }
        public int Level { get; private set; } = 1;
        public int ProficiencyBonus { get; private set; } = 2;
        public int Gold { get; private set; }
        public string Race { get; private set; } = string.Empty;
        public string Class { get; private set; } = string.Empty;
        public string Background { get; private set; } = string.Empty;

        private string _currentRestType = "";
        private const int DefaultBaseSpeed = 30;
        private int _baseSpeed = DefaultBaseSpeed;

        public List<string> PreparedSpells { get; private set; } = [];
        public Dictionary<string, int> ClassFeatureUses { get; private set; } = [];
        public List<string> AttunedItems { get; private set; } = [];

        public bool IsDashing { get; private set; }
        public bool IsDisengaged { get; private set; }
        public bool IsHiding { get; private set; }

        public Dictionary<string, int> MovementModifiers { get; private set; } = [];
        public string? LastMovementAction { get; private set; }

        public Dictionary<string, int> AbilityScores { get; private set; } = new()
        {
            {"Strength", 10}, {"Dexterity", 10}, {"Constitution", 10},
            {"Intelligence", 10}, {"Wisdom", 10}, {"Charisma", 10}
        };

        public List<string> SkillProficiencies { get; private set; } = [];
        public List<string> SavingThrowProficiencies { get; private set; } = [];
        public List<string> Feats { get; private set; } = [];

        public List<string> KnownSpells { get; private set; } = [];
        public Dictionary<int, int> MaxSpellSlots { get; private set; } = [];
        public Dictionary<int, int> UsedSpellSlots { get; private set; } = [];

        public Dictionary<int, int> HitDiceRemaining { get; private set; } = [];
        public Dictionary<int, int> MaxHitDice { get; private set; } = [];

        public bool IsDead { get; private set; }
        public bool IsStable { get; private set; }
        public int DeathSaveSuccesses { get; private set; }
        public int DeathSaveFailures { get; private set; }

        public List<string> Conditions { get; private set; } = [];
        public List<string> Resistances { get; private set; } = [];
        public List<string> Vulnerabilities { get; private set; } = [];
        public List<string> Immunities { get; private set; } = [];

        public List<EquippedItem> Equipment { get; private set; } = [];
        public List<InventoryItem> Inventory { get; private set; } = [];

        public bool Concentrating { get; private set; }
        public string? ConcentratingOnSpellId { get; private set; }

        public bool IsAlive => !IsDead;
        public bool IsUnconscious => HitPoints <= 0 && !IsDead && !IsStable;

        // ---------- Конструкторы ----------
        public CharacterAggregate(Guid id, string name, int maxHp = 10)
        {
            if (id == Guid.Empty) throw new ArgumentException("Идентификатор персонажа не может быть пустым.", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Имя персонажа не может быть пустым.", nameof(name));
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp), "Максимальные хиты должны быть положительными.");

            ApplyChange(new CharacterCreated(id, name, maxHp, DateTime.UtcNow));
        }

        // Для event sourcing
        public CharacterAggregate() { }

        // ---------- Применение событий ----------
        protected override void ApplyEvent(IDomainEvent @event)
        {
            switch (@event)
            {
                case CharacterCreated e:
                    Id = e.CharacterId;
                    Name = e.Name;
                    MaxHitPoints = e.MaxHitPoints;
                    HitPoints = e.MaxHitPoints;
                    _baseSpeed = DefaultBaseSpeed;
                    break;

                case CharacterUpdated e:
                    if (e.Name != null) Name = e.Name;
                    if (e.MaxHitPoints.HasValue)
                    {
                        MaxHitPoints = e.MaxHitPoints.Value;
                        if (HitPoints > MaxHitPoints) HitPoints = MaxHitPoints;
                    }
                    break;

                case CharacterDamageTaken e:
                    ApplyDamage(e.Amount);
                    break;

                case CharacterHealed e:
                    HealHitPoints(e.Amount);
                    break;

                case TemporaryHitPointsSet e:
                    TemporaryHitPoints = e.Amount;
                    break;

                case ExperienceGained e:
                    ExperiencePoints += e.Amount;
                    break;

                case CharacterLevelUp e:
                    Level = e.NewLevel;
                    ProficiencyBonus = e.NewProficiencyBonus;
                    break;

                case AbilityScoreSet e:
                    AbilityScores[e.Ability] = Math.Clamp(e.Score, 1, 30);
                    break;

                case RaceChosen e: Race = e.Race; break;
                case ClassChosen e: Class = e.ClassName; break;
                case BackgroundChosen e: Background = e.BackgroundName; break;

                case SkillProficiencyAdded e:
                    if (!SkillProficiencies.Contains(e.Skill)) SkillProficiencies.Add(e.Skill);
                    break;
                case SkillProficiencyRemoved e:
                    SkillProficiencies.Remove(e.Skill);
                    break;
                case SavingThrowProficiencyAdded e:
                    if (!SavingThrowProficiencies.Contains(e.Ability)) SavingThrowProficiencies.Add(e.Ability);
                    break;
                case SavingThrowProficiencyRemoved e:
                    SavingThrowProficiencies.Remove(e.Ability);
                    break;

                case FeatAdded e:
                    if (!Feats.Contains(e.FeatName)) Feats.Add(e.FeatName);
                    break;
                case FeatRemoved e:
                    Feats.Remove(e.FeatName);
                    break;

                case SpellAdded e:
                    if (!KnownSpells.Contains(e.SpellId)) KnownSpells.Add(e.SpellId);
                    break;
                case SpellRemoved e:
                    KnownSpells.Remove(e.SpellId);
                    break;

                case SpellSlotsSet e:
                    MaxSpellSlots = new Dictionary<int, int>(e.MaxSlots);
                    UsedSpellSlots = e.MaxSlots.ToDictionary(kvp => kvp.Key, _ => 0);
                    break;
                case SpellSlotUsed e:
                    if (UsedSpellSlots.TryGetValue(e.SlotLevel, out int used))
                        UsedSpellSlots[e.SlotLevel] = used + 1;
                    break;
                case SpellSlotsRestored e:
                    if (UsedSpellSlots.TryGetValue(e.SlotLevel, out int used2))
                        UsedSpellSlots[e.SlotLevel] = Math.Max(0, used2 - e.RestoredCount);
                    break;

                case HitDiceSet e:
                    HitDiceRemaining = new Dictionary<int, int>(e.Dice);
                    MaxHitDice = new Dictionary<int, int>(e.Dice);
                    break;
                case HitDieSpent e:
                    if (HitDiceRemaining.TryGetValue(e.HitDieType, out int remaining))
                        HitDiceRemaining[e.HitDieType] = Math.Max(0, remaining - 1);
                    HealHitPoints(e.HealedAmount);
                    break;
                case HitDiceRecovered e:
                    foreach (var kvp in e.Recovered)
                    {
                        if (HitDiceRemaining.TryGetValue(kvp.Key, out int current))
                        {
                            int maxForType = MaxHitDice.GetValueOrDefault(kvp.Key);
                            HitDiceRemaining[kvp.Key] = Math.Min(maxForType, current + kvp.Value);
                        }
                    }
                    break;

                case ConditionApplied e:
                    if (!Conditions.Contains(e.Condition)) Conditions.Add(e.Condition);
                    break;
                case ConditionRemoved e:
                    Conditions.Remove(e.Condition);
                    break;
                case AllConditionsCleared e:
                    Conditions.Clear();
                    break;

                case ArmorClassUpdated e: ArmorClass = e.NewArmorClass; break;
                case SpeedUpdated e: Speed = e.NewSpeed; _baseSpeed = e.NewSpeed; break;
                case CharacterMovedToPosition e: PositionX = e.TargetX; PositionY = e.TargetY; break;

                case ResistanceAdded e:
                    if (!Resistances.Contains(e.DamageType)) Resistances.Add(e.DamageType);
                    break;
                case ResistanceRemoved e: Resistances.Remove(e.DamageType); break;
                case VulnerabilityAdded e:
                    if (!Vulnerabilities.Contains(e.DamageType)) Vulnerabilities.Add(e.DamageType);
                    break;
                case VulnerabilityRemoved e: Vulnerabilities.Remove(e.DamageType); break;
                case ImmunityAdded e:
                    if (!Immunities.Contains(e.DamageType)) Immunities.Add(e.DamageType);
                    break;
                case ImmunityRemoved e: Immunities.Remove(e.DamageType); break;

                case ItemEquipped e:
                    Equipment.RemoveAll(i => i.Slot == e.Slot);
                    Equipment.Add(new EquippedItem
                    {
                        ItemId = e.ItemId,
                        Slot = e.Slot,
                        Name = e.ItemName,
                        ArmorBonus = e.ArmorBonus,
                        DamageBonus = e.DamageBonus
                    });
                    break;
                case ItemUnequipped e:
                    Equipment.RemoveAll(i => i.ItemId == e.ItemId);
                    break;
                case InventoryItemAdded e:
                    var existing = Inventory.FirstOrDefault(i => i.ItemId == e.ItemId);
                    if (existing != null) existing.Quantity += e.Quantity;
                    else Inventory.Add(new InventoryItem { ItemId = e.ItemId, Name = e.ItemName, Quantity = e.Quantity });
                    break;
                case InventoryItemRemoved e:
                    var invItem = Inventory.FirstOrDefault(i => i.ItemId == e.ItemId);
                    if (invItem != null)
                    {
                        invItem.Quantity -= e.Quantity;
                        if (invItem.Quantity <= 0) Inventory.Remove(invItem);
                    }
                    break;

                case DeathSavingThrowSuccess e:
                    DeathSaveSuccesses = Math.Min(DeathSaveSuccesses + 1, 3);
                    if (DeathSaveSuccesses >= 3) IsStable = true;
                    break;
                case DeathSavingThrowFailure e:
                    DeathSaveFailures = Math.Min(DeathSaveFailures + 1, 3);
                    if (DeathSaveFailures >= 3) IsDead = true;
                    break;
                case CharacterStabilized e:
                    IsStable = true;
                    DeathSaveSuccesses = 0;
                    DeathSaveFailures = 0;
                    break;
                case CharacterDied e:
                    IsDead = true;
                    break;
                case CharacterRevived e:
                    IsDead = false;
                    HitPoints = e.NewHitPoints;
                    DeathSaveSuccesses = 0;
                    DeathSaveFailures = 0;
                    IsStable = false;
                    break;

                case ConcentrationStarted e:
                    Concentrating = true;
                    ConcentratingOnSpellId = e.SpellId;
                    break;
                case ConcentrationEnded e:
                    Concentrating = false;
                    ConcentratingOnSpellId = null;
                    break;

                case GoldAdded e: Gold += e.Amount; break;
                case GoldSpent e: Gold -= e.Amount; break;
                case GoldSet e: Gold = e.Amount; break;

                case RestStarted e:
                    _currentRestType = e.RestType;
                    break;
                case RestInterrupted e:
                    _currentRestType = "";
                    break;
                case RestCompleted e:
                    _currentRestType = "";
                    if (e.RestType == "Long")
                        HitPoints = MaxHitPoints;
                    break;

                case ProficiencyBonusUpdated e: ProficiencyBonus = e.Bonus; break;

                case SpellPrepared e:
                    if (!PreparedSpells.Contains(e.SpellId)) PreparedSpells.Add(e.SpellId);
                    break;
                case SpellUnprepared e:
                    PreparedSpells.Remove(e.SpellId);
                    break;

                case ClassFeatureUsed e:
                    ClassFeatureUses[e.FeatureId] = ClassFeatureUses.GetValueOrDefault(e.FeatureId) + 1;
                    break;
                case ClassFeatureRecharged e:
                    ClassFeatureUses[e.FeatureId] = 0;
                    break;

                case ItemAttuned e:
                    if (!AttunedItems.Contains(e.ItemId)) AttunedItems.Add(e.ItemId);
                    break;
                case ItemUnattuned e:
                    AttunedItems.Remove(e.ItemId);
                    break;

                case DeathSavingThrowsReset e:
                    DeathSaveSuccesses = 0;
                    DeathSaveFailures = 0;
                    IsStable = false;
                    break;

                case CharacterDashed e: IsDashing = true; break;
                case CharacterDisengaged e: IsDisengaged = true; break;
                case CharacterHid e: IsHiding = true; break;

                case CharacterSpeedChanged e:
                    Speed = e.NewSpeed;
                    break;
                case CharacterSpeedReset e:
                    Speed = _baseSpeed;
                    break;

                case DifficultTerrainApplied e:
                    MovementModifiers["DifficultTerrain"] = e.Multiplier;
                    break;
                case DifficultTerrainRemoved e:
                    MovementModifiers.Remove("DifficultTerrain");
                    break;
                case MovementImpaired e:
                    MovementModifiers[e.ImpairmentType] = e.SpeedReduction;
                    break;
                case MovementRestored e:
                    MovementModifiers.Remove(e.ImpairmentType);
                    break;

                case CharacterClimbed e:
                    LastMovementAction = $"Climb:{e.DistanceFeet}ft";
                    break;
                case CharacterSwam e:
                    LastMovementAction = $"Swim:{e.DistanceFeet}ft";
                    break;
                case CharacterFlew e:
                    LastMovementAction = $"Fly:{e.DistanceFeet}ft";
                    break;
                case CharacterBurrowed e:
                    LastMovementAction = $"Burrow:{e.DistanceFeet}ft";
                    break;
                case CharacterJumped e:
                    LastMovementAction = $"Jump:{e.JumpType}";
                    break;
                case AthleticsCheckForMovementMade e:
                    LastMovementAction = $"AthleticsCheck:DC{e.DifficultyClass} roll={e.RollResult} success={e.Success}";
                    break;
                case AcrobaticsCheckForMovementMade e:
                    LastMovementAction = $"AcrobaticsCheck:DC{e.DifficultyClass} roll={e.RollResult} success={e.Success}";
                    break;
                case SavingThrowAttempted e:
                    // Информация о спасброске может быть использована для аналитики, но не влияет на состояние
                    break;
                case FallDamageTaken e:
                    LastMovementAction = $"FallDamage:{e.FallDistanceFeet}ft damage={e.DamageAmount}";
                    ApplyDamage(e.DamageAmount);
                    break;
                case MaxHitPointsIncreased e:
                    MaxHitPoints += e.Amount;
                    HitPoints += e.Amount; // текущие хиты тоже увеличиваются
                    break;

                case HitDieAdded e:
                    if (!HitDiceRemaining.ContainsKey(e.HitDieType))
                        HitDiceRemaining[e.HitDieType] = 0;
                    if (!MaxHitDice.ContainsKey(e.HitDieType))
                        MaxHitDice[e.HitDieType] = 0;

                    HitDiceRemaining[e.HitDieType] += 1;
                    MaxHitDice[e.HitDieType] += 1;
                    break;
            }
        }

        // ---------- Приватные методы модификации ----------
        private void ApplyDamage(int amount)
        {
            int remaining = amount;
            if (TemporaryHitPoints > 0)
            {
                int absorbed = Math.Min(TemporaryHitPoints, remaining);
                TemporaryHitPoints -= absorbed;
                remaining -= absorbed;
            }
            HitPoints = Math.Max(0, HitPoints - remaining);
        }

        private void HealHitPoints(int amount)
        {
            HitPoints = Math.Min(HitPoints + amount, MaxHitPoints);
            if (HitPoints > 0)
            {
                IsStable = false;
                DeathSaveSuccesses = 0;
                DeathSaveFailures = 0;
            }
        }

        // ---------- Инварианты ----------
        public override void EnsureInvariants()
        {
            if (HitPoints < 0) HitPoints = 0;
            if (HitPoints > MaxHitPoints) HitPoints = MaxHitPoints;
            if (Level < 1) Level = 1;
            if (Level > 20) throw new RuleViolation("Level", "Уровень не может превышать 20.");
            foreach (var score in AbilityScores.Values)
                if (score < 1 || score > 30)
                    throw new RuleViolation("AbilityScore", "Характеристики должны быть в диапазоне от 1 до 30.");
            foreach (var slot in UsedSpellSlots)
                if (MaxSpellSlots.TryGetValue(slot.Key, out int max) && slot.Value > max)
                    throw new RuleViolation("SpellSlots", "Использованные ячейки заклинаний превышают максимум.");
        }

        // ---------- Публичные команды ----------
        public void TakeDamage(int amount, string damageType = "bludgeoning")
        {
            if (amount <= 0) throw new ArgumentException("Урон должен быть положительным.", nameof(amount));
            if (IsDead) throw new RuleViolation("Character", "Нельзя нанести урон мёртвому персонажу.");
            if (string.IsNullOrWhiteSpace(damageType)) throw new ArgumentException("Тип урона не может быть пустым.", nameof(damageType));

            // Применяем сопротивления, уязвимости, иммунитеты
            int finalDamage = CombatRules.ApplyDamageModifiers(
                amount,
                damageType,
                Resistances,
                Vulnerabilities,
                Immunities);

            // Если после модификаторов урон нулевой, ничего не делаем
            if (finalDamage <= 0)
                return;

            ApplyChange(new CharacterDamageTaken(Id, finalDamage, DateTime.UtcNow));
        }

        public void Heal(int amount)
        {
            if (amount <= 0) throw new ArgumentException("Лечение должно быть положительным.", nameof(amount));
            if (IsDead) throw new RuleViolation("Character", "Нельзя лечить мёртвого персонажа.");
            ApplyChange(new CharacterHealed(Id, amount, DateTime.UtcNow));
        }

        public void Update(string? name, int? maxHp)
        {
            if (name == null && maxHp == null)
                throw new ArgumentException("Необходимо указать хотя бы одно поле для обновления.");
            if (name != null && !ValidationRules.IsValidCharacterName(name))
                throw new RuleViolation("Validation", "Некорректное имя персонажа.");
            ApplyChange(new CharacterUpdated(Id, name, maxHp, DateTime.UtcNow));
        }

        public void SetTemporaryHitPoints(int amount)
        {
            if (amount < 0) throw new ArgumentException("Временные хиты не могут быть отрицательными.", nameof(amount));
            ApplyChange(new TemporaryHitPointsSet(Id, amount));
        }

        public void GainExperience(int amount)
        {
            if (amount <= 0) throw new ArgumentException("Опыт должен быть положительным.", nameof(amount));
            ApplyChange(new ExperienceGained(Id, amount));
        }

        public void SetAbilityScore(string ability, int score)
        {
            if (!AbilityScores.ContainsKey(ability)) throw new ArgumentException("Неизвестная характеристика.", nameof(ability));
            ApplyChange(new AbilityScoreSet(Id, ability, score));
        }

        public void ChooseRace(string race)
        {
            if (string.IsNullOrWhiteSpace(race)) throw new ArgumentException("Раса не может быть пустой.", nameof(race));
            ApplyChange(new RaceChosen(Id, race));
        }

        public void ChooseClass(string className)
        {
            if (string.IsNullOrWhiteSpace(className)) throw new ArgumentException("Класс не может быть пустым.", nameof(className));
            ApplyChange(new ClassChosen(Id, className));
        }

        public void ChooseBackground(string backgroundName)
        {
            if (string.IsNullOrWhiteSpace(backgroundName)) throw new ArgumentException("Предыстория не может быть пустой.", nameof(backgroundName));
            ApplyChange(new BackgroundChosen(Id, backgroundName));
        }

        public void AddSkillProficiency(string skill)
        {
            if (string.IsNullOrWhiteSpace(skill)) throw new ArgumentException("Название навыка не может быть пустым.", nameof(skill));
            if (SkillProficiencies.Contains(skill)) throw new InvalidOperationException("Навык уже освоен.");
            ApplyChange(new SkillProficiencyAdded(Id, skill));
        }

        public void RemoveSkillProficiency(string skill)
        {
            if (!SkillProficiencies.Contains(skill)) throw new InvalidOperationException("Навык не освоен.");
            ApplyChange(new SkillProficiencyRemoved(Id, skill));
        }

        public void AddSavingThrowProficiency(string ability)
        {
            if (SavingThrowProficiencies.Contains(ability)) throw new InvalidOperationException("Спасбросок уже освоен.");
            ApplyChange(new SavingThrowProficiencyAdded(Id, ability));
        }

        public void RemoveSavingThrowProficiency(string ability)
        {
            if (!SavingThrowProficiencies.Contains(ability)) throw new InvalidOperationException("Спасбросок не освоен.");
            ApplyChange(new SavingThrowProficiencyRemoved(Id, ability));
        }

        public void AddFeat(string featName)
        {
            if (Feats.Contains(featName)) throw new InvalidOperationException("Черта уже изучена.");
            ApplyChange(new FeatAdded(Id, featName));
        }

        public void RemoveFeat(string featName)
        {
            if (!Feats.Contains(featName)) throw new InvalidOperationException("Черта не изучена.");
            ApplyChange(new FeatRemoved(Id, featName));
        }

        public void AddSpell(string spellId)
        {
            if (KnownSpells.Contains(spellId)) throw new InvalidOperationException("Заклинание уже известно.");
            ApplyChange(new SpellAdded(Id, spellId));
        }

        public void RemoveSpell(string spellId)
        {
            if (!KnownSpells.Contains(spellId)) throw new InvalidOperationException("Заклинание неизвестно.");
            ApplyChange(new SpellRemoved(Id, spellId));
        }

        public void SetSpellSlots(Dictionary<int, int> maxSlots)
        {
            if (maxSlots == null || maxSlots.Count == 0)
                throw new ArgumentException("Словарь ячеек заклинаний не может быть пустым.", nameof(maxSlots));
            ApplyChange(new SpellSlotsSet(Id, maxSlots));
        }

        public void UseSpellSlot(int slotLevel)
        {
            if (!MaxSpellSlots.TryGetValue(slotLevel, out int maxSlots))
                throw new InvalidOperationException("Нет такого уровня ячеек заклинаний.");
            int used = UsedSpellSlots.GetValueOrDefault(slotLevel);
            if (used >= maxSlots)
                throw new InvalidOperationException("Нет доступных ячеек этого уровня.");
            ApplyChange(new SpellSlotUsed(Id, slotLevel));
        }

        public void RestoreAllSpellSlots()
        {
            foreach (var kvp in MaxSpellSlots)
                ApplyChange(new SpellSlotsRestored(Id, kvp.Key, kvp.Value));
        }

        public void SetHitDice(Dictionary<int, int> dice)
        {
            if (dice == null || dice.Count == 0)
                throw new ArgumentException("Словарь костей хитов не может быть пустым.", nameof(dice));
            ApplyChange(new HitDiceSet(Id, dice));
        }

        public void SpendHitDie(int hitDieType, int roll, int constitutionModifier)
        {
            if (!HitDiceRemaining.TryGetValue(hitDieType, out int remaining) || remaining <= 0)
                throw new InvalidOperationException("Нет доступных костей хитов этого типа.");
            if (HitPoints <= 0 && !IsStable)
                throw new InvalidOperationException("Нельзя тратить кости хитов, находясь при смерти.");
            int healed = roll + constitutionModifier;
            ApplyChange(new HitDieSpent(Id, hitDieType, healed));
        }

        public void RecoverHitDice(Dictionary<int, int> recovered)
        {
            if (recovered == null || recovered.Count == 0)
                throw new ArgumentException("Словарь восстановленных костей не может быть пустым.", nameof(recovered));
            ApplyChange(new HitDiceRecovered(Id, recovered));
        }

        public void ApplyCondition(string condition, int durationRounds)
        {
            if (string.IsNullOrWhiteSpace(condition)) throw new ArgumentException("Состояние не может быть пустым.", nameof(condition));
            if (durationRounds <= 0) throw new ArgumentOutOfRangeException(nameof(durationRounds), "Длительность должна быть положительной.");
            ApplyChange(new ConditionApplied(Id, condition));
        }

        public void RemoveCondition(string condition)
        {
            if (!Conditions.Contains(condition)) throw new InvalidOperationException("Состояние не активно.");
            ApplyChange(new ConditionRemoved(Id, condition));
        }

        public void ClearAllConditions()
        {
            if (Conditions.Count == 0) return; // Ничего не делаем, если состояний нет
            ApplyChange(new AllConditionsCleared(Id));
        }

        public void UpdateArmorClass(int newAC)
        {
            if (newAC < 0) throw new ArgumentException("Класс брони не может быть отрицательным.", nameof(newAC));
            ApplyChange(new ArmorClassUpdated(Id, newAC));
        }

        public void UpdateSpeed(int newSpeed)
        {
            if (newSpeed < 0) throw new ArgumentException("Скорость не может быть отрицательной.", nameof(newSpeed));
            ApplyChange(new SpeedUpdated(Id, newSpeed));
        }

        public void AddResistance(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType)) throw new ArgumentException("Тип урона не может быть пустым.", nameof(damageType));
            if (Resistances.Contains(damageType)) throw new InvalidOperationException("Уже есть сопротивление этому типу урона.");
            ApplyChange(new ResistanceAdded(Id, damageType));
        }

        public void RemoveResistance(string damageType)
        {
            if (!Resistances.Contains(damageType)) throw new InvalidOperationException("Нет сопротивления этому типу урона.");
            ApplyChange(new ResistanceRemoved(Id, damageType));
        }

        public void AddVulnerability(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType)) throw new ArgumentException("Тип урона не может быть пустым.", nameof(damageType));
            if (Vulnerabilities.Contains(damageType)) throw new InvalidOperationException("Уже есть уязвимость к этому типу урона.");
            ApplyChange(new VulnerabilityAdded(Id, damageType));
        }

        public void RemoveVulnerability(string damageType)
        {
            if (!Vulnerabilities.Contains(damageType)) throw new InvalidOperationException("Нет уязвимости к этому типу урона.");
            ApplyChange(new VulnerabilityRemoved(Id, damageType));
        }

        public void AddImmunity(string damageType)
        {
            if (string.IsNullOrWhiteSpace(damageType)) throw new ArgumentException("Тип урона не может быть пустым.", nameof(damageType));
            if (Immunities.Contains(damageType)) throw new InvalidOperationException("Уже есть иммунитет к этому типу урона.");
            ApplyChange(new ImmunityAdded(Id, damageType));
        }

        public void RemoveImmunity(string damageType)
        {
            if (!Immunities.Contains(damageType)) throw new InvalidOperationException("Нет иммунитета к этому типу урона.");
            ApplyChange(new ImmunityRemoved(Id, damageType));
        }

        public void EquipItem(string itemId, string slot, string itemName, int armorBonus = 0, int damageBonus = 0)
        {
            if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("Слот не может быть пустым.", nameof(slot));
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("Название предмета не может быть пустым.", nameof(itemName));
            ApplyChange(new ItemEquipped(Id, itemId, slot, itemName, armorBonus, damageBonus));
        }

        public void UnequipItem(string itemId)
        {
            if (!Equipment.Any(e => e.ItemId == itemId)) throw new InvalidOperationException("Предмет не экипирован.");
            ApplyChange(new ItemUnequipped(Id, itemId));
        }

        public void AddInventoryItem(string itemId, string itemName, int quantity = 1)
        {
            if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("Название предмета не может быть пустым.", nameof(itemName));
            if (quantity <= 0) throw new ArgumentException("Количество должно быть положительным.", nameof(quantity));
            ApplyChange(new InventoryItemAdded(Id, itemId, itemName, quantity));
        }

        public void RemoveInventoryItem(string itemId, int quantity = 1)
        {
            var inv = Inventory.FirstOrDefault(i => i.ItemId == itemId);
            if (inv == null || inv.Quantity < quantity) throw new InvalidOperationException("Недостаточно предметов в инвентаре.");
            if (quantity <= 0) throw new ArgumentException("Количество должно быть положительным.", nameof(quantity));
            ApplyChange(new InventoryItemRemoved(Id, itemId, quantity));
        }

        public void DeathSavingThrow(bool success)
        {
            if (HitPoints > 0 || IsDead || IsStable)
                throw new InvalidOperationException("Спасброски от смерти доступны только при смерти.");
            if (success)
                ApplyChange(new DeathSavingThrowSuccess(Id));
            else
                ApplyChange(new DeathSavingThrowFailure(Id));
        }

        public void MakeDeathSavingThrow(int rollResult)
        {
            DeathSavingThrow(rollResult >= 10);
        }

        public void Stabilize()
        {
            if (HitPoints > 0 || IsDead || IsStable)
                throw new InvalidOperationException("Персонаж не находится при смерти.");
            ApplyChange(new CharacterStabilized(Id));
        }

        public void MarkDead()
        {
            if (IsDead) throw new InvalidOperationException("Персонаж уже мёртв.");
            ApplyChange(new CharacterDied(Id, DateTime.UtcNow));
        }

        public void Revive(int newHitPoints)
        {
            if (!IsDead) throw new InvalidOperationException("Персонаж не мёртв.");
            if (newHitPoints <= 0) throw new ArgumentException("Количество хитов после воскрешения должно быть положительным.", nameof(newHitPoints));
            ApplyChange(new CharacterRevived(Id, newHitPoints));
        }

        public void StartConcentration(string spellId)
        {
            if (string.IsNullOrWhiteSpace(spellId)) throw new ArgumentException("Идентификатор заклинания не может быть пустым.", nameof(spellId));
            if (Concentrating) throw new InvalidOperationException("Персонаж уже концентрируется.");
            ApplyChange(new ConcentrationStarted(Id, spellId));
        }

        public void EndConcentration()
        {
            if (!Concentrating) throw new InvalidOperationException("Персонаж не концентрируется.");
            ApplyChange(new ConcentrationEnded(Id, ConcentratingOnSpellId ?? "", "voluntary"));
        }

        public void MoveToPosition(int targetX, int targetY, string movementType)
        {
            if (string.IsNullOrWhiteSpace(movementType)) throw new ArgumentException("Тип перемещения не может быть пустым.", nameof(movementType));
            ApplyChange(new CharacterMovedToPosition(Id, targetX, targetY, movementType, DateTime.UtcNow));
        }

        public void Dash() => ApplyChange(new CharacterDashed(Id));
        public void Disengage() => ApplyChange(new CharacterDisengaged(Id));
        public void Hide() => ApplyChange(new CharacterHid(Id));

        public void Climb(int distanceFeet, int climbSpeedUsed) => ApplyChange(new CharacterClimbed(Id, distanceFeet, climbSpeedUsed));
        public void Swim(int distanceFeet, int swimSpeedUsed) => ApplyChange(new CharacterSwam(Id, distanceFeet, swimSpeedUsed));
        public void Fly(int distanceFeet, int flySpeedUsed) => ApplyChange(new CharacterFlew(Id, distanceFeet, flySpeedUsed));
        public void Burrow(int distanceFeet, int burrowSpeedUsed) => ApplyChange(new CharacterBurrowed(Id, distanceFeet, burrowSpeedUsed));

        public void Jump(string jumpType, int strengthScore, bool runningStart)
        {
            if (string.IsNullOrWhiteSpace(jumpType)) throw new ArgumentException("Тип прыжка не может быть пустым.", nameof(jumpType));
            if (strengthScore <= 0) throw new ArgumentOutOfRangeException(nameof(strengthScore), "Сила должна быть положительной.");
            ApplyChange(new CharacterJumped(Id, jumpType, strengthScore, runningStart, 0));
        }

        public void SetTemporarySpeed(int newSpeed, string movementType)
        {
            if (newSpeed < 0) throw new ArgumentException("Скорость не может быть отрицательной.", nameof(newSpeed));
            if (string.IsNullOrWhiteSpace(movementType)) throw new ArgumentException("Тип перемещения не может быть пустым.", nameof(movementType));
            ApplyChange(new CharacterSpeedChanged(Id, newSpeed, movementType));
        }

        public void ResetSpeedToBase() => ApplyChange(new CharacterSpeedReset(Id));

        public void ApplyDifficultTerrain(int multiplier)
        {
            if (multiplier <= 1) throw new ArgumentException("Множитель трудной местности должен быть больше 1.", nameof(multiplier));
            ApplyChange(new DifficultTerrainApplied(Id, multiplier));
        }

        public void RemoveDifficultTerrain() => ApplyChange(new DifficultTerrainRemoved(Id));

        public void ApplyMovementImpairment(string impairmentType, int speedReduction)
        {
            if (string.IsNullOrWhiteSpace(impairmentType)) throw new ArgumentException("Тип ограничения не может быть пустым.", nameof(impairmentType));
            if (speedReduction < 0) throw new ArgumentOutOfRangeException(nameof(speedReduction), "Снижение скорости не может быть отрицательным.");
            ApplyChange(new MovementImpaired(Id, impairmentType, speedReduction));
        }

        public void RemoveMovementImpairment(string impairmentType)
        {
            if (string.IsNullOrWhiteSpace(impairmentType)) throw new ArgumentException("Тип ограничения не может быть пустым.", nameof(impairmentType));
            ApplyChange(new MovementRestored(Id, impairmentType));
        }

        public void MakeAthleticsCheck(int difficultyClass, int rollResult, int proficiencyBonus, int strengthModifier)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(difficultyClass);
            bool success = (rollResult + proficiencyBonus + strengthModifier) >= difficultyClass;
            ApplyChange(new AthleticsCheckForMovementMade(Id, difficultyClass, rollResult, proficiencyBonus, strengthModifier, success));
        }

        public void MakeAcrobaticsCheck(int difficultyClass, int rollResult, int proficiencyBonus, int dexterityModifier)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(difficultyClass);
            bool success = (rollResult + proficiencyBonus + dexterityModifier) >= difficultyClass;
            ApplyChange(new AcrobaticsCheckForMovementMade(Id, difficultyClass, rollResult, proficiencyBonus, dexterityModifier, success));
        }

        public void TakeFallDamage(int fallDistanceFeet)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(fallDistanceFeet);
            int diceCount = Math.Min(fallDistanceFeet / 10, 20);
            int damage = Enumerable.Range(0, diceCount).Sum(_ => Random.Shared.Next(1, 7));
            ApplyChange(new FallDamageTaken(Id, fallDistanceFeet, damage));
        }

        public void StartRest(string restType)
        {
            if (string.IsNullOrWhiteSpace(restType)) throw new ArgumentException("Тип отдыха не может быть пустым.", nameof(restType));
            _currentRestType = restType;
            ApplyChange(new RestStarted(Id, restType, DateTime.UtcNow));
        }

        public void InterruptRest(string interruptionType)
        {
            if (string.IsNullOrWhiteSpace(interruptionType)) throw new ArgumentException("Тип прерывания не может быть пустым.", nameof(interruptionType));
            ApplyChange(new RestInterrupted(Id, interruptionType, DateTime.UtcNow));
        }

        public void EndRest()
        {
            int hpRestored = _currentRestType == "Long" ? MaxHitPoints - HitPoints : 0;
            ApplyChange(new RestCompleted(Id, _currentRestType, hpRestored, DateTime.UtcNow));
            _currentRestType = "";
        }

        public void LevelUp(int newLevel)
        {
            if (newLevel <= Level || newLevel > 20)
                throw new ArgumentException("Новый уровень должен быть больше текущего и не превышать 20.", nameof(newLevel));

            int newProfBonus = 2 + (int)Math.Floor((newLevel - 1) / 4.0);
            int hitDieType = GetHitDieByClass(Class);
            int conModifier = ModifierCalculator.Calculate(AbilityScores.GetValueOrDefault("Constitution", 10));
            int hpIncrease = (hitDieType / 2 + 1) + conModifier;

            ApplyChange(new CharacterLevelUp(Id, newLevel, newProfBonus));
            ApplyChange(new MaxHitPointsIncreased(Id, hpIncrease));
            ApplyChange(new HitDieAdded(Id, hitDieType));
        }

        private static int GetHitDieByClass(string? className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return 8; // значение по умолчанию

            return className.ToLowerInvariant() switch
            {
                "barbarian" or "fighter" or "paladin" or "ranger" => 10,
                "bard" or "cleric" or "druid" or "monk" or "rogue" or "warlock" => 8,
                "sorcerer" or "wizard" => 6,
                _ => 8
            };
        }

        public void CastSpell(string spellId, int spellSlotLevel)
        {
            if (!KnownSpells.Contains(spellId))
                throw new InvalidOperationException("Заклинание неизвестно.");
            if (!MaxSpellSlots.TryGetValue(spellSlotLevel, out int maxSlots))
                throw new InvalidOperationException("Нет такого уровня ячеек заклинаний.");
            int used = UsedSpellSlots.GetValueOrDefault(spellSlotLevel);
            if (used >= maxSlots)
                throw new InvalidOperationException("Нет доступных ячеек этого уровня.");
            ApplyChange(new SpellSlotUsed(Id, spellSlotLevel));
        }

        public void TakeShortRest(IEnumerable<(int HitDieType, int Roll, int ConstitutionModifier)>? hitDiceSpent = null)
        {
            if (hitDiceSpent != null)
            {
                foreach (var (hitDieType, roll, constitutionModifier) in hitDiceSpent)
                {
                    SpendHitDie(hitDieType, roll, constitutionModifier);
                }
            }
            ApplyChange(new RestCompleted(Id, "Short", 0, DateTime.UtcNow));
        }

        public void TakeLongRest()
        {
            ApplyChange(new RestCompleted(Id, "Long", MaxHitPoints - HitPoints, DateTime.UtcNow));
        }

        /// <summary>Добавляет золото персонажу.</summary>
        public void AddGold(int amount)
        {
            if (amount <= 0) throw new ArgumentException("Сумма должна быть положительной.", nameof(amount));
            if (IsDead) throw new InvalidOperationException("Нельзя добавить золото мёртвому персонажу.");
            ApplyChange(new GoldAdded(Id, amount));
        }

        /// <summary>Тратит золото персонажа.</summary>
        public void SpendGold(int amount)
        {
            if (amount <= 0) throw new ArgumentException("Сумма должна быть положительной.", nameof(amount));
            if (IsDead) throw new InvalidOperationException("Нельзя тратить золото мёртвого персонажа.");
            if (Gold < amount) throw new InvalidOperationException($"Недостаточно золота. Требуется: {amount}, доступно: {Gold}.");
            ApplyChange(new GoldSpent(Id, amount));
        }

        /// <summary>Устанавливает точное количество золота (для административных целей).</summary>
        public void SetGold(int amount)
        {
            if (amount < 0) throw new ArgumentException("Золото не может быть отрицательным.", nameof(amount));
            ApplyChange(new GoldSet(Id, amount));
        }

        public void MakeSavingThrow(string abilityType, int difficultyClass, int rollResult)
        {
            if (!AbilityScores.TryGetValue(abilityType, out int value))
                throw new ArgumentException("Неизвестная характеристика.", nameof(abilityType));
            int abilityModifier = ModifierCalculator.Calculate(value);
            bool success = (rollResult + abilityModifier) >= difficultyClass;
            ApplyChange(new SavingThrowAttempted(Id, abilityType, difficultyClass, rollResult, success));
        }

        public void SetProficiencyBonus(int bonus)
        {
            if (bonus < 2 || bonus > 6)
                throw new ArgumentOutOfRangeException(nameof(bonus), "Бонус мастерства должен быть от 2 до 6.");
            ApplyChange(new ProficiencyBonusUpdated(Id, bonus));
        }

        public void PrepareSpell(string spellId)
        {
            if (!KnownSpells.Contains(spellId))
                throw new InvalidOperationException("Заклинание неизвестно.");
            ApplyChange(new SpellPrepared(Id, spellId));
        }

        public void UnprepareSpell(string spellId)
        {
            ApplyChange(new SpellUnprepared(Id, spellId));
        }

        public void UseClassFeature(string featureId)
        {
            if (string.IsNullOrWhiteSpace(featureId))
                throw new ArgumentException("Идентификатор умения не может быть пустым.", nameof(featureId));
            ApplyChange(new ClassFeatureUsed(Id, featureId));
        }

        public void RechargeFeature(string featureId)
        {
            if (string.IsNullOrWhiteSpace(featureId))
                throw new ArgumentException("Идентификатор умения не может быть пустым.", nameof(featureId));
            ApplyChange(new ClassFeatureRecharged(Id, featureId));
        }

        public void AttuneItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            if (AttunedItems.Count >= 3)
                throw new InvalidOperationException("Достигнут лимит аттунемента (3 предмета).");
            if (AttunedItems.Contains(itemId))
                throw new InvalidOperationException("Предмет уже аттунен.");
            ApplyChange(new ItemAttuned(Id, itemId));
        }

        public void UnattuneItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Идентификатор предмета не может быть пустым.", nameof(itemId));
            if (!AttunedItems.Contains(itemId))
                throw new InvalidOperationException("Предмет не аттунен.");
            ApplyChange(new ItemUnattuned(Id, itemId));
        }

        public void ResetDeathSavingThrows()
        {
            ApplyChange(new DeathSavingThrowsReset(Id));
        }
    }

    // Вспомогательные классы для внутренних коллекций
    public class EquippedItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string Slot { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int ArmorBonus { get; set; }
        public int DamageBonus { get; set; }
    }

    public class InventoryItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}