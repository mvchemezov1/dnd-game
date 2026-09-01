using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.aggregates;
using dnd_game.domain.commands;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.event_store;

namespace dnd_game.application.command_handlers;

/// <summary>
/// Базовый класс для обработчиков команд, работающих с агрегатом Character.
/// Содержит общую логику загрузки и сохранения агрегата.
/// </summary>
public abstract class CharacterCommandHandlerBase(IEventStore eventStore)
{
    protected readonly IEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));

    /// <summary>
    /// Загружает агрегат Character по идентификатору. Если агрегат не найден, выбрасывает исключение с русским сообщением.
    /// </summary>
    protected async Task<CharacterAggregate> GetCharacterAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var aggregate = await _eventStore.Load<CharacterAggregate>(characterId, cancellationToken) ?? throw new InvalidAction("Персонаж не найден");
        return aggregate;
    }

    /// <summary>
    /// Сохраняет изменения агрегата в Event Store.
    /// </summary>
    protected async Task SaveCharacterAsync(CharacterAggregate aggregate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await _eventStore.Save(aggregate, cancellationToken);
    }
}

/// <summary>
/// Обработчик команд для управления персонажами.
/// Реализует все команды, связанные с характеристиками, инвентарём, заклинаниями и т.д.
/// </summary>
public class CharacterHandler(IEventStore eventStore) : CharacterCommandHandlerBase(eventStore),
                                ICommandHandler<CreateCharacter>,
                                ICommandHandler<UpdateCharacter>,
                                ICommandHandler<DealDamage>,
                                ICommandHandler<HealCharacter>,
                                ICommandHandler<SetTemporaryHitPoints>,
                                ICommandHandler<GainExperience>,
                                ICommandHandler<LevelUpCharacter>,
                                ICommandHandler<SetAbilityScore>,
                                ICommandHandler<AddGold>,
                                ICommandHandler<SpendGold>,
                                ICommandHandler<SetGoldCommand>,
                                ICommandHandler<ClearAllConditionsCommand>,
                                ICommandHandler<AddSpell>,
                                ICommandHandler<RemoveSpell>,
                                ICommandHandler<CastSpell>,
                                ICommandHandler<TakeShortRest>,
                                ICommandHandler<TakeLongRest>,
                                ICommandHandler<ApplyCondition>,
                                ICommandHandler<RemoveCondition>,
                                ICommandHandler<MakeSavingThrow>,
                                ICommandHandler<DeathSavingThrow>,
                                ICommandHandler<StabilizeCharacter>,
                                ICommandHandler<UpdateTemporaryHitPoints>,
                                ICommandHandler<UpdateArmorClass>,
                                ICommandHandler<UpdateSpeed>,
                                ICommandHandler<UpdateProficiencyBonus>,
                                ICommandHandler<AddInventoryItem>,
                                ICommandHandler<RemoveInventoryItem>,
                                ICommandHandler<EquipItem>,
                                ICommandHandler<UnequipItem>,
                                ICommandHandler<ChooseRace>,
                                ICommandHandler<ChooseClass>,
                                ICommandHandler<ChooseBackground>,
                                ICommandHandler<AddSkillProficiency>,
                                ICommandHandler<RemoveSkillProficiency>,
                                ICommandHandler<AddSavingThrowProficiency>,
                                ICommandHandler<RemoveSavingThrowProficiency>,
                                ICommandHandler<AddFeat>,
                                ICommandHandler<RemoveFeat>,
                                ICommandHandler<PrepareSpell>,
                                ICommandHandler<UnprepareSpell>,
                                ICommandHandler<UseClassFeature>,
                                ICommandHandler<RechargeFeature>,
                                ICommandHandler<AttuneItem>,
                                ICommandHandler<UnattuneItem>,
                                ICommandHandler<AddResistance>,
                                ICommandHandler<RemoveResistance>,
                                ICommandHandler<AddVulnerability>,
                                ICommandHandler<RemoveVulnerability>,
                                ICommandHandler<AddImmunity>,
                                ICommandHandler<RemoveImmunity>,
                                ICommandHandler<ReviveCharacter>,
                                ICommandHandler<ResetDeathSavingThrows>,
                                ICommandHandler<UseSpellSlot>,
                                ICommandHandler<RestoreAllSpellSlots>
{
    public async Task Handle(CreateCharacter command, CancellationToken cancellationToken)
    {
        var aggregate = new CharacterAggregate(command.CharacterId, command.Name, command.MaxHitPoints);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UpdateCharacter command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.Update(command.Name, command.MaxHitPoints);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(DealDamage command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.TakeDamage(command.Amount, command.DamageType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(HealCharacter command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.Heal(command.Amount);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(SetTemporaryHitPoints command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.SetTemporaryHitPoints(command.Amount);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(GainExperience command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.GainExperience(command.ExperiencePoints);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(LevelUpCharacter command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.LevelUp(command.NewLevel);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(SetAbilityScore command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.SetAbilityScore(command.Ability, command.Score);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddGold command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddGold(command.Amount);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(SpendGold command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.SpendGold(command.Amount);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(SetClassFeatureMaxUsesCommand command, CancellationToken ct)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, ct);
        aggregate.SetClassFeatureMaxUses(command.FeatureId, command.MaxUses);
        await SaveCharacterAsync(aggregate, ct);
    }

    public async Task Handle(SetGoldCommand command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.SetGold(command.Amount);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(ClearAllConditionsCommand command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.ClearAllConditions();
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddSpell command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddSpell(command.SpellId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveSpell command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveSpell(command.SpellId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(CastSpell command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.CastSpell(command.SpellId, command.SpellSlotLevel);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(TakeShortRest command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.TakeShortRest(command.HitDiceSpent);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(TakeLongRest command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.TakeLongRest();
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(ApplyCondition command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.ApplyCondition(command.ConditionType, command.DurationRounds);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveCondition command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveCondition(command.ConditionType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(MakeSavingThrow command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.MakeSavingThrow(command.AbilityType, command.DifficultyClass, command.RollResult);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(DeathSavingThrow command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.MakeDeathSavingThrow(command.RollResult);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(StabilizeCharacter command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.Stabilize();
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UpdateTemporaryHitPoints command, CancellationToken cancellationToken)
    {
        // Оставлено для обратной совместимости, дублирует SetTemporaryHitPoints
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.SetTemporaryHitPoints(command.Amount);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UpdateArmorClass command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.UpdateArmorClass(command.NewArmorClass);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UpdateSpeed command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.UpdateSpeed(command.NewSpeed);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UpdateProficiencyBonus command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.SetProficiencyBonus(command.Bonus);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddInventoryItem command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddInventoryItem(command.ItemId, command.ItemName, command.Quantity);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveInventoryItem command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveInventoryItem(command.ItemId, command.Quantity);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(EquipItem command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.EquipItem(command.ItemId, command.Slot, command.ItemName, command.ArmorBonus, command.DamageBonus);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UnequipItem command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.UnequipItem(command.ItemId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(ChooseRace command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.ChooseRace(command.RaceId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(ChooseClass command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.ChooseClass(command.ClassId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(ChooseBackground command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.ChooseBackground(command.BackgroundId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddSkillProficiency command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddSkillProficiency(command.SkillName);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveSkillProficiency command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveSkillProficiency(command.SkillName);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddSavingThrowProficiency command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddSavingThrowProficiency(command.Ability);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveSavingThrowProficiency command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveSavingThrowProficiency(command.Ability);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddFeat command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddFeat(command.FeatId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveFeat command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveFeat(command.FeatId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(PrepareSpell command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.PrepareSpell(command.SpellId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UnprepareSpell command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.UnprepareSpell(command.SpellId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UseClassFeature command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.UseClassFeature(command.FeatureId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RechargeFeature command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RechargeFeature(command.FeatureId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AttuneItem command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AttuneItem(command.ItemId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UnattuneItem command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.UnattuneItem(command.ItemId);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddResistance command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddResistance(command.DamageType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveResistance command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveResistance(command.DamageType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddVulnerability command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddVulnerability(command.DamageType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveVulnerability command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveVulnerability(command.DamageType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(AddImmunity command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.AddImmunity(command.DamageType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RemoveImmunity command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RemoveImmunity(command.DamageType);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(ReviveCharacter command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.Revive(command.HitPointsAfterRevive);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(ResetDeathSavingThrows command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.ResetDeathSavingThrows();
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(UseSpellSlot command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.UseSpellSlot(command.SlotLevel);
        await SaveCharacterAsync(aggregate, cancellationToken);
    }

    public async Task Handle(RestoreAllSpellSlots command, CancellationToken cancellationToken)
    {
        var aggregate = await GetCharacterAsync(command.CharacterId, cancellationToken);
        aggregate.RestoreAllSpellSlots();
        await SaveCharacterAsync(aggregate, cancellationToken);
    }
}