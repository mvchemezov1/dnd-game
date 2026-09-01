using dnd_game.domain.events;
using dnd_game.infrastructure.caching;
using dnd_game.infrastructure.event_store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.projections
{
    /// <summary>
    /// Проекция персонажей. Хранит текущее состояние всех персонажей в памяти
    /// и предоставляет методы чтения с кэшированием. Обновляется событиями домена.
    /// </summary>
    public class CharacterProjection(ICacheProvider cache, TimeSpan? cacheTtl = null, ILogger<CharacterProjection>? logger = null)
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<Guid, CharacterDto> _state = [];
        private readonly ICacheProvider _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        private readonly TimeSpan _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);
        private readonly ILogger<CharacterProjection> _logger = logger ?? NullLogger<CharacterProjection>.Instance;

        /// <summary>
        /// Инвалидирует записи кэша, связанные с указанным персонажем.
        /// </summary>
        private void InvalidateCache(Guid characterId)
        {
            // Синхронное удаление, чтобы кэш был очищен сразу после изменения состояния
            _cache.RemoveSync($"character:{characterId}");
            _cache.RemoveSync("characters:all");
        }

        // ==================== Обработчики событий ====================

        public void Apply(CharacterCreated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                var dto = new CharacterDto
                {
                    Id = e.CharacterId,
                    Name = e.Name,
                    MaxHitPoints = e.MaxHitPoints,
                    HitPoints = e.MaxHitPoints
                };
                _state[e.CharacterId] = dto;
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterUpdated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with
                    {
                        Name = e.Name ?? dto.Name,
                        MaxHitPoints = e.MaxHitPoints ?? dto.MaxHitPoints,
                        HitPoints = Math.Min(dto.HitPoints, e.MaxHitPoints ?? dto.MaxHitPoints)
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterMovedToPosition e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { PositionX = e.TargetX, PositionY = e.TargetY };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterMoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with
                    {
                        PositionX = e.ToX,
                        PositionY = e.ToY
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterDamageTaken e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    int remainingDamage = Math.Max(0, e.Amount);
                    int newTemp = dto.TemporaryHitPoints;

                    if (newTemp > 0)
                    {
                        int absorbed = Math.Min(newTemp, remainingDamage);
                        newTemp -= absorbed;
                        remainingDamage -= absorbed;
                    }

                    int newHp = Math.Max(0, dto.HitPoints - remainingDamage);

                    _state[e.CharacterId] = dto with
                    {
                        TemporaryHitPoints = newTemp,
                        HitPoints = newHp
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterHealed e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with
                    {
                        HitPoints = Math.Min(dto.HitPoints + Math.Max(0, e.Amount), dto.MaxHitPoints)
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(TemporaryHitPointsSet e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { TemporaryHitPoints = Math.Max(0, e.Amount) };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ExperienceGained e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { ExperiencePoints = dto.ExperiencePoints + Math.Max(0, e.Amount) };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterLevelUp e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with
                    {
                        Level = e.NewLevel,
                        ProficiencyBonus = e.NewProficiencyBonus
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(AbilityScoreSet e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var scores = new Dictionary<string, int>(dto.AbilityScores) { [e.Ability] = e.Score };
                    _state[e.CharacterId] = dto with { AbilityScores = scores };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SkillProficiencyAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var skills = new Dictionary<string, bool>(dto.SkillProficiencies) { [e.Skill] = true };
                    _state[e.CharacterId] = dto with { SkillProficiencies = skills };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SkillProficiencyRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var skills = new Dictionary<string, bool>(dto.SkillProficiencies);
                    skills.Remove(e.Skill);
                    _state[e.CharacterId] = dto with { SkillProficiencies = skills };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SavingThrowProficiencyAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var saves = new Dictionary<string, bool>(dto.SavingThrowProficiencies) { [e.Ability] = true };
                    _state[e.CharacterId] = dto with { SavingThrowProficiencies = saves };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SavingThrowProficiencyRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var saves = new Dictionary<string, bool>(dto.SavingThrowProficiencies);
                    saves.Remove(e.Ability);
                    _state[e.CharacterId] = dto with { SavingThrowProficiencies = saves };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(RaceChosen e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Race = e.Race };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ClassChosen e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Class = e.ClassName };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(BackgroundChosen e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Background = e.BackgroundName };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(FeatAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var feats = new List<string>(dto.Feats);
                    if (!feats.Contains(e.FeatName))
                        feats.Add(e.FeatName);
                    _state[e.CharacterId] = dto with { Feats = feats };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(FeatRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var feats = new List<string>(dto.Feats);
                    feats.Remove(e.FeatName);
                    _state[e.CharacterId] = dto with { Feats = feats };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SpellAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var spells = new List<string>(dto.KnownSpells);
                    if (!spells.Contains(e.SpellId))
                        spells.Add(e.SpellId);
                    _state[e.CharacterId] = dto with { KnownSpells = spells };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SpellRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var spells = new List<string>(dto.KnownSpells);
                    spells.Remove(e.SpellId);
                    _state[e.CharacterId] = dto with { KnownSpells = spells };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SpellSlotUsed e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var used = new Dictionary<int, int>(dto.UsedSpellSlots);
                    used[e.SlotLevel] = used.GetValueOrDefault(e.SlotLevel, 0) + 1;
                    _state[e.CharacterId] = dto with { UsedSpellSlots = used };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SpellSlotsRestored e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { UsedSpellSlots = [] };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterDashed e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { IsDashing = true };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterDisengaged e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { IsDisengaged = true };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterHid e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { IsHiding = true };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ConditionApplied e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var conditions = new List<string>(dto.Conditions);
                    if (!conditions.Contains(e.Condition))
                        conditions.Add(e.Condition);
                    _state[e.CharacterId] = dto with { Conditions = conditions };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(AllConditionsCleared e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Conditions = [] };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ConditionRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var conditions = new List<string>(dto.Conditions);
                    conditions.Remove(e.Condition);
                    _state[e.CharacterId] = dto with { Conditions = conditions };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ArmorClassUpdated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { ArmorClass = e.NewArmorClass };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(SpeedUpdated e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Speed = e.NewSpeed };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ResistanceAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var res = new List<string>(dto.Resistances);
                    if (!res.Contains(e.DamageType))
                        res.Add(e.DamageType);
                    _state[e.CharacterId] = dto with { Resistances = res };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ResistanceRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Resistances = [.. dto.Resistances.Where(r => r != e.DamageType)] };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(VulnerabilityAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var vul = new List<string>(dto.Vulnerabilities);
                    if (!vul.Contains(e.DamageType))
                        vul.Add(e.DamageType);
                    _state[e.CharacterId] = dto with { Vulnerabilities = vul };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(VulnerabilityRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Vulnerabilities = [.. dto.Vulnerabilities.Where(v => v != e.DamageType)] };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ImmunityAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var imm = new List<string>(dto.Immunities);
                    if (!imm.Contains(e.DamageType))
                        imm.Add(e.DamageType);
                    _state[e.CharacterId] = dto with { Immunities = imm };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ImmunityRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Immunities = [.. dto.Immunities.Where(i => i != e.DamageType)] };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(DeathSavingThrowSuccess e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    int successes = Math.Min(dto.DeathSaveSuccesses + 1, 3);
                    bool stable = successes >= 3;
                    _state[e.CharacterId] = dto with
                    {
                        DeathSaveSuccesses = successes,
                        IsStable = stable
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(DeathSavingThrowFailure e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    int failures = Math.Min(dto.DeathSaveFailures + 1, 3);
                    bool dead = failures >= 3;
                    _state[e.CharacterId] = dto with
                    {
                        DeathSaveFailures = failures,
                        IsDead = dead
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterStabilized e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with
                    {
                        IsStable = true,
                        DeathSaveSuccesses = 0,
                        DeathSaveFailures = 0
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterDied e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { IsDead = true };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(MaxHitPointsIncreased e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with
                    {
                        MaxHitPoints = dto.MaxHitPoints + e.Amount,
                        HitPoints = dto.HitPoints + e.Amount
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(HitDieAdded e)
        {
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var remaining = new Dictionary<int, int>(dto.HitDiceRemaining);
                    var max = new Dictionary<int, int>(dto.MaxHitDice);

                    remaining[e.HitDieType] = remaining.GetValueOrDefault(e.HitDieType) + 1;
                    max[e.HitDieType] = max.GetValueOrDefault(e.HitDieType) + 1;

                    _state[e.CharacterId] = dto with
                    {
                        HitDiceRemaining = remaining,
                        MaxHitDice = max
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(CharacterRevived e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with
                    {
                        IsDead = false,
                        HitPoints = e.NewHitPoints,
                        DeathSaveSuccesses = 0,
                        DeathSaveFailures = 0,
                        IsStable = false
                    };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ItemEquipped e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var equipment = new List<EquippedItemDto>(dto.Equipment);
                    equipment.RemoveAll(i => i.Slot == e.Slot);
                    equipment.Add(new EquippedItemDto(e.ItemId, e.Slot, e.ItemName, e.ArmorBonus, e.DamageBonus));
                    _state[e.CharacterId] = dto with { Equipment = equipment };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ItemUnequipped e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var equipment = new List<EquippedItemDto>(dto.Equipment);
                    equipment.RemoveAll(i => i.ItemId == e.ItemId);
                    _state[e.CharacterId] = dto with { Equipment = equipment };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(InventoryItemAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var inventory = new List<InventoryItemDto>(dto.Inventory);
                    var existing = inventory.FirstOrDefault(i => i.ItemId == e.ItemId);
                    if (existing != null)
                    {
                        inventory.Remove(existing);
                        inventory.Add(existing with { Quantity = existing.Quantity + e.Quantity });
                    }
                    else
                    {
                        inventory.Add(new InventoryItemDto(e.ItemId, e.ItemName, e.Quantity));
                    }
                    _state[e.CharacterId] = dto with { Inventory = inventory };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(InventoryItemRemoved e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var inventory = new List<InventoryItemDto>(dto.Inventory);
                    var existing = inventory.FirstOrDefault(i => i.ItemId == e.ItemId);
                    if (existing != null)
                    {
                        inventory.Remove(existing);
                        int newQuantity = existing.Quantity - e.Quantity;
                        if (newQuantity > 0)
                            inventory.Add(existing with { Quantity = newQuantity });
                    }
                    _state[e.CharacterId] = dto with { Inventory = inventory };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(GoldAdded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Gold = dto.Gold + Math.Max(0, e.Amount) };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(GoldSpent e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Gold = Math.Max(0, dto.Gold - Math.Max(0, e.Amount)) };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(GoldSet e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Gold = Math.Max(0, e.Amount) };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(HitDieSpent e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var dice = new Dictionary<int, int>(dto.HitDiceRemaining);
                    if (dice.TryGetValue(e.HitDieType, out int value))
                        dice[e.HitDieType] = Math.Max(0, value - 1);
                    _state[e.CharacterId] = dto with { HitDiceRemaining = dice };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(HitDiceRecovered e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    var dice = new Dictionary<int, int>(dto.HitDiceRemaining);
                    foreach (var kv in e.Recovered)
                    {
                        int max = dto.MaxHitDice.GetValueOrDefault(kv.Key, 0);
                        int current = dice.GetValueOrDefault(kv.Key, 0);
                        dice[kv.Key] = Math.Min(max, current + kv.Value);
                    }
                    _state[e.CharacterId] = dto with { HitDiceRemaining = dice };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ConcentrationStarted e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Concentrating = true };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        public void Apply(ConcentrationEnded e)
        {
            ArgumentNullException.ThrowIfNull(e);
            lock (_syncRoot)
            {
                if (_state.TryGetValue(e.CharacterId, out var dto))
                {
                    _state[e.CharacterId] = dto with { Concentrating = false };
                }
            }
            InvalidateCache(e.CharacterId);
        }

        // ==================== Методы чтения ====================

        public async Task<CharacterDto?> GetById(Guid id, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"character:{id}";
            var cached = await _cache.GetAsync<CharacterDto>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;

            CharacterDto? dto;
            lock (_syncRoot)
            {
                _state.TryGetValue(id, out dto);
            }

            if (dto != null)
            {
                await _cache.SetAsync(cacheKey, dto, _cacheTtl, cancellationToken);
                return dto;
            }
            return null;
        }

        public async Task<List<CharacterDto>> GetAll(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "characters:all";
            var cached = await _cache.GetAsync<List<CharacterDto>>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;

            List<CharacterDto> list;
            lock (_syncRoot)
            {
                list = [.. _state.Values];
            }

            await _cache.SetAsync(cacheKey, list, _cacheTtl, cancellationToken);
            return list;
        }

        // ==================== Восстановление проекции ====================

        public async Task RebuildAsync(IEventStore eventStore, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventStore);
            lock (_syncRoot)
            {
                _state.Clear();
            }

            var allEvents = await eventStore.GetAllEvents(cancellationToken);
            if (allEvents is IAsyncEnumerable<IDomainEvent> asyncEvents)
            {
                await foreach (var e in asyncEvents.WithCancellation(cancellationToken))
                {
                    Apply(e);
                }
            }
            else if (allEvents is IAsyncEnumerable<object> asyncObjects)
            {
                await foreach (var e in asyncObjects.WithCancellation(cancellationToken))
                {
                    if (e is IDomainEvent domainEvent)
                        Apply(domainEvent);
                }
            }
            else
            {
                foreach (var e in allEvents)
                {
                    if (e is IDomainEvent domainEvent)
                        Apply(domainEvent);
                }
            }
            await _cache.RemoveAsync("characters:all", cancellationToken);
        }

        // ==================== Диспетчеризация событий ====================

        public void Apply(IDomainEvent e)
        {
            ArgumentNullException.ThrowIfNull(e);
            switch (e)
            {
                case CharacterCreated ev: Apply(ev); break;
                case CharacterUpdated ev: Apply(ev); break;
                case CharacterMovedToPosition ev: Apply(ev); break;
                case CharacterDamageTaken ev: Apply(ev); break;
                case CharacterHealed ev: Apply(ev); break;
                case TemporaryHitPointsSet ev: Apply(ev); break;
                case ExperienceGained ev: Apply(ev); break;
                case CharacterLevelUp ev: Apply(ev); break;
                case AbilityScoreSet ev: Apply(ev); break;
                case SkillProficiencyAdded ev: Apply(ev); break;
                case SkillProficiencyRemoved ev: Apply(ev); break;
                case SavingThrowProficiencyAdded ev: Apply(ev); break;
                case SavingThrowProficiencyRemoved ev: Apply(ev); break;
                case RaceChosen ev: Apply(ev); break;
                case ClassChosen ev: Apply(ev); break;
                case BackgroundChosen ev: Apply(ev); break;
                case FeatAdded ev: Apply(ev); break;
                case FeatRemoved ev: Apply(ev); break;
                case SpellAdded ev: Apply(ev); break;
                case SpellRemoved ev: Apply(ev); break;
                case SpellSlotUsed ev: Apply(ev); break;
                case SpellSlotsRestored ev: Apply(ev); break;
                case ConditionApplied ev: Apply(ev); break;
                case ConditionRemoved ev: Apply(ev); break;
                case AllConditionsCleared ev: Apply(ev); break;
                case ArmorClassUpdated ev: Apply(ev); break;
                case SpeedUpdated ev: Apply(ev); break;
                case ResistanceAdded ev: Apply(ev); break;
                case ResistanceRemoved ev: Apply(ev); break;
                case VulnerabilityAdded ev: Apply(ev); break;
                case VulnerabilityRemoved ev: Apply(ev); break;
                case ImmunityAdded ev: Apply(ev); break;
                case ImmunityRemoved ev: Apply(ev); break;
                case DeathSavingThrowSuccess ev: Apply(ev); break;
                case DeathSavingThrowFailure ev: Apply(ev); break;
                case CharacterStabilized ev: Apply(ev); break;
                case CharacterDied ev: Apply(ev); break;
                case CharacterRevived ev: Apply(ev); break;
                case ItemEquipped ev: Apply(ev); break;
                case ItemUnequipped ev: Apply(ev); break;
                case InventoryItemAdded ev: Apply(ev); break;
                case InventoryItemRemoved ev: Apply(ev); break;
                case GoldAdded ev: Apply(ev); break;
                case GoldSpent ev: Apply(ev); break;
                case GoldSet ev: Apply(ev); break;
                case HitDieSpent ev: Apply(ev); break;
                case HitDiceRecovered ev: Apply(ev); break;
                case ConcentrationStarted ev: Apply(ev); break;
                case ConcentrationEnded ev: Apply(ev); break;
                case MaxHitPointsIncreased ev: Apply(ev); break;
                case HitDieAdded ev: Apply(ev); break;
                case CharacterMoved ev: Apply(ev); break;
                default:
                    _logger.LogDebug("Получено неизвестное событие {EventType} в проекции персонажей", e.GetType().Name);
                    break;
            }
        }
    }
}