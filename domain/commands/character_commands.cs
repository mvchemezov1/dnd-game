#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.commands
{
    // ---------- Базовые команды персонажа ----------

    /// <summary>Создать нового персонажа.</summary>
    public record CreateCharacter(Guid CharacterId, string Name, int MaxHitPoints) : ICommand;

    /// <summary>Нанести урон персонажу.</summary>
    public record DealDamage(Guid CharacterId, int Amount, string DamageType = "bludgeoning") : ICommand;

    /// <summary>Исцелить персонажа.</summary>
    public record HealCharacter(Guid CharacterId, int Amount) : ICommand;

    /// <summary>Обновить основные данные персонажа (имя, максимальные хиты).</summary>
    public record UpdateCharacter(Guid CharacterId, string? Name, int? MaxHitPoints) : ICommand;
    public record SetClassFeatureMaxUsesCommand(Guid CharacterId, string FeatureId, int MaxUses) : ICommand;

    // ---------- Временные хиты ----------

    /// <summary>Установить временные хиты.</summary>
    public record SetTemporaryHitPoints(Guid CharacterId, int Amount) : ICommand;

    /// <summary>Обновить временные хиты (аналог SetTemporaryHitPoints).</summary>
    public record UpdateTemporaryHitPoints(Guid CharacterId, int Amount) : ICommand;

    // ---------- Опыт и уровень ----------

    /// <summary>Добавить опыт персонажу.</summary>
    public record GainExperience(Guid CharacterId, int ExperiencePoints) : ICommand;

    /// <summary>Повысить уровень персонажа.</summary>
    public record LevelUpCharacter(Guid CharacterId, int NewLevel) : ICommand;

    // ---------- Характеристики ----------

    /// <summary>Установить значение характеристики (Сила, Ловкость и т.д.).</summary>
    public record SetAbilityScore(Guid CharacterId, string Ability, int Score) : ICommand;

    // ---------- Раса, класс, предыстория ----------

    /// <summary>Выбрать расу персонажа.</summary>
    public record ChooseRace(Guid CharacterId, string RaceId) : ICommand;

    /// <summary>Выбрать класс персонажа.</summary>
    public record ChooseClass(Guid CharacterId, string ClassId) : ICommand;

    /// <summary>Выбрать предысторию персонажа.</summary>
    public record ChooseBackground(Guid CharacterId, string BackgroundId) : ICommand;

    // ---------- Владения навыками и спасбросками ----------

    /// <summary>Добавить владение навыком.</summary>
    public record AddSkillProficiency(Guid CharacterId, string SkillName) : ICommand;

    /// <summary>Убрать владение навыком.</summary>
    public record RemoveSkillProficiency(Guid CharacterId, string SkillName) : ICommand;

    /// <summary>Добавить владение спасброском.</summary>
    public record AddSavingThrowProficiency(Guid CharacterId, string Ability) : ICommand;

    /// <summary>Убрать владение спасброском.</summary>
    public record RemoveSavingThrowProficiency(Guid CharacterId, string Ability) : ICommand;

    // ---------- Черты ----------

    /// <summary>Добавить черту (feat).</summary>
    public record AddFeat(Guid CharacterId, string FeatId) : ICommand;

    /// <summary>Удалить черту.</summary>
    public record RemoveFeat(Guid CharacterId, string FeatId) : ICommand;

    // ---------- Заклинания ----------

    /// <summary>Добавить известное заклинание.</summary>
    public record AddSpell(Guid CharacterId, string SpellId) : ICommand;

    /// <summary>Удалить заклинание из списка известных.</summary>
    public record RemoveSpell(Guid CharacterId, string SpellId) : ICommand;

    /// <summary>Подготовить заклинание.</summary>
    public record PrepareSpell(Guid CharacterId, string SpellId) : ICommand;

    /// <summary>Снять подготовку с заклинания.</summary>
    public record UnprepareSpell(Guid CharacterId, string SpellId) : ICommand;

    /// <summary>Использовать ячейку заклинания указанного уровня.</summary>
    public record UseSpellSlot(Guid CharacterId, int SlotLevel) : ICommand;

    /// <summary>Восстановить все ячейки заклинаний.</summary>
    public record RestoreAllSpellSlots(Guid CharacterId) : ICommand;

    /// <summary>Установить максимальное количество ячеек по уровням.</summary>
    public record SetSpellSlots(Guid CharacterId, Dictionary<int, int> MaxSlots) : ICommand;

    // ---------- Кости хитов ----------

    /// <summary>Установить кости хитов (тип кубика → количество).</summary>
    public record SetHitDice(Guid CharacterId, Dictionary<int, int> Dice) : ICommand;

    /// <summary>Восстановить потраченные кости хитов (тип кубика → сколько восстановлено).</summary>
    public record RecoverHitDice(Guid CharacterId, Dictionary<int, int> Recovered) : ICommand;

    // ---------- Состояния ----------

    /// <summary>Наложить состояние на персонажа.</summary>
    public record ApplyCondition(Guid CharacterId, string ConditionType, int DurationRounds) : ICommand;

    /// <summary>Снять состояние с персонажа.</summary>
    public record RemoveCondition(Guid CharacterId, string ConditionType) : ICommand;

    /// <summary>Снять все активные состояния.</summary>
    public record ClearAllConditionsCommand(Guid CharacterId) : ICommand;

    // ---------- Боевые параметры ----------

    /// <summary>Обновить класс брони.</summary>
    public record UpdateArmorClass(Guid CharacterId, int NewArmorClass) : ICommand;

    /// <summary>Обновить скорость передвижения.</summary>
    public record UpdateSpeed(Guid CharacterId, int NewSpeed) : ICommand;

    /// <summary>Обновить бонус мастерства.</summary>
    public record UpdateProficiencyBonus(Guid CharacterId, int Bonus) : ICommand;

    // ---------- Защиты ----------

    /// <summary>Добавить сопротивление урону.</summary>
    public record AddResistance(Guid CharacterId, string DamageType) : ICommand;

    /// <summary>Убрать сопротивление урону.</summary>
    public record RemoveResistance(Guid CharacterId, string DamageType) : ICommand;

    /// <summary>Добавить уязвимость к урону.</summary>
    public record AddVulnerability(Guid CharacterId, string DamageType) : ICommand;

    /// <summary>Убрать уязвимость к урону.</summary>
    public record RemoveVulnerability(Guid CharacterId, string DamageType) : ICommand;

    /// <summary>Добавить иммунитет к урону.</summary>
    public record AddImmunity(Guid CharacterId, string DamageType) : ICommand;

    /// <summary>Убрать иммунитет к урону.</summary>
    public record RemoveImmunity(Guid CharacterId, string DamageType) : ICommand;

    // ---------- Экипировка и инвентарь ----------

    /// <summary>Экипировать предмет в указанный слот.</summary>
    public record EquipItem(
        Guid CharacterId,
        string ItemId,
        string Slot,
        string ItemName,
        int ArmorBonus = 0,
        int DamageBonus = 0) : ICommand;

    /// <summary>Снять экипированный предмет.</summary>
    public record UnequipItem(Guid CharacterId, string ItemId) : ICommand;

    /// <summary>Добавить предмет в инвентарь.</summary>
    public record AddInventoryItem(Guid CharacterId, string ItemId, string ItemName, int Quantity = 1) : ICommand;

    /// <summary>Удалить предмет из инвентаря.</summary>
    public record RemoveInventoryItem(Guid CharacterId, string ItemId, int Quantity = 1) : ICommand;

    // ---------- Смерть и спасброски ----------

    /// <summary>Совершить спасбросок от смерти (результат d20).</summary>
    public record DeathSavingThrow(Guid CharacterId, int RollResult) : ICommand;

    /// <summary>Стабилизировать персонажа.</summary>
    public record StabilizeCharacter(Guid CharacterId) : ICommand;

    /// <summary>Пометить персонажа как мёртвого.</summary>
    public record MarkCharacterDead(Guid CharacterId) : ICommand;

    /// <summary>Воскресить персонажа с указанным количеством хитов.</summary>
    public record ReviveCharacter(Guid CharacterId, int HitPointsAfterRevive) : ICommand;

    /// <summary>Сбросить счётчики спасбросков от смерти.</summary>
    public record ResetDeathSavingThrows(Guid CharacterId) : ICommand;

    // ---------- Концентрация ----------

    /// <summary>Начать концентрацию на заклинании.</summary>
    public record StartConcentration(Guid CharacterId, string SpellId) : ICommand;

    /// <summary>Прекратить концентрацию.</summary>
    public record EndConcentration(Guid CharacterId) : ICommand;

    // ---------- Отдых ----------

    /// <summary>Совершить короткий отдых (с возможностью потратить кости хитов).</summary>
    public record TakeShortRest(
        Guid CharacterId,
        List<(int HitDieType, int Roll, int ConstitutionModifier)>? HitDiceSpent) : ICommand;

    /// <summary>Совершить длинный отдых.</summary>
    public record TakeLongRest(Guid CharacterId) : ICommand;

    // ---------- Аттунемент магических предметов ----------

    /// <summary>Аттунить магический предмет.</summary>
    public record AttuneItem(Guid CharacterId, string ItemId) : ICommand;

    /// <summary>Разорвать аттунемент с предметом.</summary>
    public record UnattuneItem(Guid CharacterId, string ItemId) : ICommand;

    // ---------- Классовые умения ----------

    /// <summary>Использовать классовое умение.</summary>
    public record UseClassFeature(Guid CharacterId, string FeatureId) : ICommand;

    /// <summary>Перезарядить классовое умение.</summary>
    public record RechargeFeature(Guid CharacterId, string FeatureId) : ICommand;

    // ---------- Спасброски и проверки ----------

    /// <summary>Совершить спасбросок характеристики (общий).</summary>
    public record MakeSavingThrow(Guid CharacterId, string AbilityType, int DifficultyClass, int RollResult) : ICommand;

    // ---------- Заклинания (дополнительно) ----------

    /// <summary>Применить заклинание.</summary>
    public record CastSpell(
        Guid CharacterId,
        string SpellId,
        Guid? TargetId,
        int SpellSlotLevel) : ICommand;

    // ---------- Дополнительные команды, используемые сервисами/триггерами ----------

    /// <summary>Выдать предмет персонажу.</summary>
    public record GiveItemCommand(Guid CharacterId, string ItemId, string ItemName, int Quantity = 1) : ICommand;

    /// <summary>Создать монстра по шаблону в указанных координатах.</summary>
    public record SpawnMonsterCommand(string TemplateId, int X, int Y) : ICommand;

    /// <summary>Телепортировать персонажа в указанные координаты.</summary>
    public record TeleportCommand(Guid CharacterId, int DestinationX, int DestinationY) : ICommand;

    /// <summary>Установить флаг квеста.</summary>
    public record SetQuestFlagCommand(Guid CharacterId, string QuestId, string Flag, string Value) : ICommand;

    /// <summary>Начать скриптовый диалог.</summary>
    public record StartScriptedDialogueCommand(Guid InitiatorId, string DialogId) : ICommand;

    /// <summary>Воспроизвести звук.</summary>
    public record PlaySoundCommand(string SoundName, int PositionX, int PositionY) : ICommand;

    /// <summary>Начать квест для персонажа.</summary>
    public record StartQuestCommand(Guid CharacterId, Guid QuestId) : ICommand;

    /// <summary>Начать диалог.</summary>
    public record StartDialogueCommand(Guid DialogueId, Guid NpcId, Guid CharacterId) : ICommand;

    /// <summary>Завершить диалог.</summary>
    public record EndDialogueCommand(Guid DialogueId) : ICommand;

    // ---------- Золото ----------

    /// <summary>Потратить золото.</summary>
    public record SpendGold(Guid CharacterId, int Amount) : ICommand;

    /// <summary>Добавить золото.</summary>
    public record AddGold(Guid CharacterId, int Amount) : ICommand;

    /// <summary>Установить точное количество золота.</summary>
    public record SetGoldCommand(Guid CharacterId, int Amount) : ICommand;

    // ---------- Хиты и кости хитов (дополнительно) ----------

    /// <summary>Увеличить максимальное количество хитов.</summary>
    public record IncreaseMaxHitPoints(Guid CharacterId, int Amount) : ICommand;

    /// <summary>Добавить кость хита указанного типа.</summary>
    public record AddHitDie(Guid CharacterId, int HitDieType) : ICommand;
}