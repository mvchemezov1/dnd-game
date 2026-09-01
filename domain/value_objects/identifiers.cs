#nullable enable
using System;

namespace dnd_game.domain.value_objects
{
    /// <summary>
    /// Базовый идентификатор агрегата общего назначения.
    /// </summary>
    public record AggregateId(Guid Value)
    {
        public static readonly AggregateId Empty = new(Guid.Empty);
        public static implicit operator Guid(AggregateId id) => id.Value;
        public static implicit operator AggregateId(Guid id) => new(id);
        public override string ToString() => Value.ToString();
    }

    // ---------- Идентификаторы персонажей и игроков ----------

    /// <summary>
    /// Идентификатор персонажа.
    /// </summary>
    public record CharacterId(Guid Value)
    {
        public static readonly CharacterId Empty = new(Guid.Empty);
        public static implicit operator Guid(CharacterId id) => id.Value;
        public static implicit operator CharacterId(Guid id) => new(id);
        public override string ToString() => $"Character({Value})";
    }

    /// <summary>
    /// Идентификатор игрока (учётной записи пользователя).
    /// </summary>
    public record PlayerId(Guid Value)
    {
        public static readonly PlayerId Empty = new(Guid.Empty);
        public static implicit operator Guid(PlayerId id) => id.Value;
        public static implicit operator PlayerId(Guid id) => new(id);
        public override string ToString() => $"Player({Value})";
    }

    /// <summary>
    /// Идентификатор неигрового персонажа (NPC).
    /// </summary>
    public record NpcId(Guid Value)
    {
        public static readonly NpcId Empty = new(Guid.Empty);
        public static implicit operator Guid(NpcId id) => id.Value;
        public static implicit operator NpcId(Guid id) => new(id);
        public override string ToString() => $"NPC({Value})";
    }

    // ---------- Идентификаторы игровых сессий и кампаний ----------

    /// <summary>
    /// Идентификатор кампании.
    /// </summary>
    public record CampaignId(Guid Value)
    {
        public static readonly CampaignId Empty = new(Guid.Empty);
        public static implicit operator Guid(CampaignId id) => id.Value;
        public static implicit operator CampaignId(Guid id) => new(id);
        public override string ToString() => $"Campaign({Value})";
    }

    /// <summary>
    /// Идентификатор игровой сессии.
    /// </summary>
    public record GameSessionId(Guid Value)
    {
        public static readonly GameSessionId Empty = new(Guid.Empty);
        public static implicit operator Guid(GameSessionId id) => id.Value;
        public static implicit operator GameSessionId(Guid id) => new(id);
        public override string ToString() => $"Session({Value})";
    }

    // ---------- Боевые идентификаторы ----------

    /// <summary>
    /// Идентификатор боя.
    /// </summary>
    public record CombatId(Guid Value)
    {
        public static readonly CombatId Empty = new(Guid.Empty);
        public static implicit operator Guid(CombatId id) => id.Value;
        public static implicit operator CombatId(Guid id) => new(id);
        public override string ToString() => $"Combat({Value})";
    }

    // ---------- Квесты ----------

    /// <summary>
    /// Идентификатор квеста.
    /// </summary>
    public record QuestId(Guid Value)
    {
        public static readonly QuestId Empty = new(Guid.Empty);
        public static implicit operator Guid(QuestId id) => id.Value;
        public static implicit operator QuestId(Guid id) => new(id);
        public override string ToString() => $"Quest({Value})";
    }

    // ---------- Предметы, заклинания, черты ----------

    /// <summary>
    /// Идентификатор предмета (строковый ключ из игровых данных).
    /// </summary>
    public record ItemId(string Value)
    {
        public static readonly ItemId Empty = new(string.Empty);
        public static implicit operator string(ItemId id) => id.Value;
        public static implicit operator ItemId(string id) => new(id);
        public override string ToString() => $"Item({Value})";
    }

    /// <summary>
    /// Идентификатор заклинания (строковый ключ из игровых данных).
    /// </summary>
    public record SpellId(string Value)
    {
        public static readonly SpellId Empty = new(string.Empty);
        public static implicit operator string(SpellId id) => id.Value;
        public static implicit operator SpellId(string id) => new(id);
        public override string ToString() => $"Spell({Value})";
    }

    /// <summary>
    /// Идентификатор черты (feat) — строковый ключ из игровых данных.
    /// </summary>
    public record FeatId(string Value)
    {
        public static readonly FeatId Empty = new(string.Empty);
        public static implicit operator string(FeatId id) => id.Value;
        public static implicit operator FeatId(string id) => new(id);
        public override string ToString() => $"Feat({Value})";
    }

    // ---------- Фракции ----------

    /// <summary>
    /// Идентификатор фракции (строковый ключ, например, "harper", "zharrum").
    /// </summary>
    public record FactionId(string Value)
    {
        public static readonly FactionId Empty = new(string.Empty);
        public static implicit operator string(FactionId id) => id.Value;
        public static implicit operator FactionId(string id) => new(id);
        public override string ToString() => $"Faction({Value})";
    }

    // ---------- Диалоги ----------

    /// <summary>
    /// Идентификатор диалога.
    /// </summary>
    public record DialogueId(Guid Value)
    {
        public static readonly DialogueId Empty = new(Guid.Empty);
        public static implicit operator Guid(DialogueId id) => id.Value;
        public static implicit operator DialogueId(Guid id) => new(id);
        public override string ToString() => $"Dialogue({Value})";
    }

    // ---------- Рецепты крафта ----------

    /// <summary>
    /// Идентификатор рецепта крафта.
    /// </summary>
    public record RecipeId(Guid Value)
    {
        public static readonly RecipeId Empty = new(Guid.Empty);
        public static implicit operator Guid(RecipeId id) => id.Value;
        public static implicit operator RecipeId(Guid id) => new(id);
        public override string ToString() => $"Recipe({Value})";
    }

    // ---------- Торговые предложения ----------

    /// <summary>
    /// Идентификатор торгового предложения.
    /// </summary>
    public record TradeOfferId(Guid Value)
    {
        public static readonly TradeOfferId Empty = new(Guid.Empty);
        public static implicit operator Guid(TradeOfferId id) => id.Value;
        public static implicit operator TradeOfferId(Guid id) => new(id);
        public override string ToString() => $"TradeOffer({Value})";
    }

    // ---------- Характеристики и навыки ----------

    /// <summary>
    /// Идентификатор характеристики (например, Strength, Dexterity).
    /// </summary>
    public record AbilityId(string Value)
    {
        public static readonly AbilityId Strength = new("Strength");
        public static readonly AbilityId Dexterity = new("Dexterity");
        public static readonly AbilityId Constitution = new("Constitution");
        public static readonly AbilityId Intelligence = new("Intelligence");
        public static readonly AbilityId Wisdom = new("Wisdom");
        public static readonly AbilityId Charisma = new("Charisma");

        public static implicit operator string(AbilityId id) => id.Value;
        public static implicit operator AbilityId(string id) => new(id);
        public override string ToString() => Value;
    }

    /// <summary>
    /// Идентификатор навыка (например, Acrobatics, Athletics).
    /// </summary>
    public record SkillId(string Value)
    {
        public static readonly SkillId Acrobatics = new("Acrobatics");
        public static readonly SkillId AnimalHandling = new("Animal Handling");
        public static readonly SkillId Arcana = new("Arcana");
        public static readonly SkillId Athletics = new("Athletics");
        public static readonly SkillId Deception = new("Deception");
        public static readonly SkillId History = new("History");
        public static readonly SkillId Insight = new("Insight");
        public static readonly SkillId Intimidation = new("Intimidation");
        public static readonly SkillId Investigation = new("Investigation");
        public static readonly SkillId Medicine = new("Medicine");
        public static readonly SkillId Nature = new("Nature");
        public static readonly SkillId Perception = new("Perception");
        public static readonly SkillId Performance = new("Performance");
        public static readonly SkillId Persuasion = new("Persuasion");
        public static readonly SkillId Religion = new("Religion");
        public static readonly SkillId SleightOfHand = new("Sleight of Hand");
        public static readonly SkillId Stealth = new("Stealth");
        public static readonly SkillId Survival = new("Survival");

        public static implicit operator string(SkillId id) => id.Value;
        public static implicit operator SkillId(string id) => new(id);
        public override string ToString() => Value;
    }

    /// <summary>
    /// Идентификатор состояния (например, Blinded, Charmed).
    /// </summary>
    public record ConditionId(string Value)
    {
        public static readonly ConditionId Blinded = new("Blinded");
        public static readonly ConditionId Charmed = new("Charmed");
        public static readonly ConditionId Deafened = new("Deafened");
        public static readonly ConditionId Frightened = new("Frightened");
        public static readonly ConditionId Grappled = new("Grappled");
        public static readonly ConditionId Incapacitated = new("Incapacitated");
        public static readonly ConditionId Invisible = new("Invisible");
        public static readonly ConditionId Paralyzed = new("Paralyzed");
        public static readonly ConditionId Petrified = new("Petrified");
        public static readonly ConditionId Poisoned = new("Poisoned");
        public static readonly ConditionId Prone = new("Prone");
        public static readonly ConditionId Restrained = new("Restrained");
        public static readonly ConditionId Stunned = new("Stunned");
        public static readonly ConditionId Unconscious = new("Unconscious");
        public static readonly ConditionId Exhaustion = new("Exhaustion");

        public static implicit operator string(ConditionId id) => id.Value;
        public static implicit operator ConditionId(string id) => new(id);
        public override string ToString() => Value;
    }
}