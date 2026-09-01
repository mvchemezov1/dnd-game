#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.exceptions
{
    /// <summary>
    /// Исключение, сигнализирующее о нарушении одного или нескольких правил Dungeons and Dragons.
    /// Содержит подробный контекст, позволяющий обработчикам (например, UI) отобразить
    /// понятное сообщение и, при необходимости, предложить способы исправления.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение о нарушении правила с указанием названия и описания.
    /// </remarks>
    /// <param name="ruleName">Название правила.</param>
    /// <param name="message">Описание нарушения.</param>
    public class RuleViolation(string ruleName, string message) : DomainError($"Нарушено правило «{ruleName}»: {message}")
    {
        /// <summary>Краткое название правила (например, "Concentration", "ActionEconomy", "AttunementSlots").</summary>
        public string RuleName { get; } = ruleName;

        /// <summary>Идентификатор персонажа, нарушившего правило (если применимо).</summary>
        public Guid? CharacterId { get; }

        /// <summary>Идентификатор связанного объекта (предмета, заклинания, боя).</summary>
        public Guid? RelatedEntityId { get; }

        /// <summary>Человекочитаемое описание того, что именно пошло не так.</summary>
        public string ViolationDescription { get; } = message;

        /// <summary>Ссылка на соответствующий раздел правил (например, "Книга игрока, стр. 203").</summary>
        public string? RuleReference { get; }

        /// <summary>Список предлагаемых действий для устранения нарушения (может быть пустым).</summary>
        public List<string> SuggestedActions { get; } = [];

        /// <summary>
        /// Создаёт исключение с указанием названия правила, описания и ссылки на источник правил.
        /// </summary>
        public RuleViolation(string ruleName, string message, string? ruleReference)
            : this(ruleName, message)
        {
            RuleReference = ruleReference;
        }

        /// <summary>
        /// Создаёт исключение для конкретного персонажа.
        /// </summary>
        public RuleViolation(Guid characterId, string ruleName, string message)
            : this(ruleName, message)
        {
            CharacterId = characterId;
        }

        /// <summary>
        /// Создаёт исключение для конкретного персонажа с указанием ссылки на правило.
        /// </summary>
        public RuleViolation(Guid characterId, string ruleName, string message, string? ruleReference)
            : this(characterId, ruleName, message)
        {
            RuleReference = ruleReference;
        }

        /// <summary>
        /// Создаёт исключение с указанием персонажа, связанного объекта и ссылки на правило.
        /// </summary>
        public RuleViolation(Guid characterId, Guid relatedEntityId, string ruleName, string message, string? ruleReference = null)
            : this(characterId, ruleName, message, ruleReference)
        {
            RelatedEntityId = relatedEntityId;
        }

        // ---------- Статические фабричные методы для типичных нарушений ----------

        /// <summary>
        /// Попытка поддерживать концентрацию на двух заклинаниях одновременно.
        /// </summary>
        public static RuleViolation ConcentrationConflict(Guid characterId, string existingSpell, string newSpell)
            => new(characterId, "Концентрация",
                $"Невозможно сконцентрироваться на «{newSpell}», так как вы уже концентрируетесь на «{existingSpell}».",
                "Книга игрока, стр. 203");

        /// <summary>
        /// Нарушение ограничения на сотворение заклинаний бонусным действием.
        /// </summary>
        public static RuleViolation BonusActionSpellRestriction(Guid characterId)
            => new(characterId, "Сотворение заклинаний",
                "Если вы сотворили заклинание бонусным действием, в этом ходу вы можете сотворить только заговор основным действием.",
                "Книга игрока, стр. 202");

        /// <summary>
        /// Отсутствие доступных ячеек заклинаний указанного уровня.
        /// </summary>
        public static RuleViolation NoSpellSlotsAvailable(Guid characterId, int slotLevel)
            => new(characterId, "Ячейки заклинаний",
                $"Нет доступных ячеек заклинаний {slotLevel}-го уровня.",
                "Книга игрока, стр. 201");

        /// <summary>
        /// Попытка использовать больше одного основного действия за ход.
        /// </summary>
        public static RuleViolation ExtraActionNotAllowed(Guid characterId)
            => new(characterId, "Экономика действий",
                "Вы не можете совершить более одного основного действия за ход, если у вас нет особенности, позволяющей это (например, «Всплеск действий»).",
                "Книга игрока, стр. 189");

        /// <summary>
        /// Попытка надеть два предмета в один слот экипировки.
        /// </summary>
        public static RuleViolation EquipmentSlotConflict(Guid characterId, string slot, string existingItem, string newItem)
            => new(characterId, "Экипировка",
                $"Нельзя экипировать «{newItem}» в слот «{slot}», так как там уже надет «{existingItem}».",
                "Книга игрока, стр. 143");

        /// <summary>
        /// Превышение лимита аттунемента (3 магических предмета).
        /// </summary>
        public static RuleViolation AttunementLimitExceeded(Guid characterId)
            => new(characterId, "Аттунемент",
                "Персонаж может быть аттунен не более чем к трём магическим предметам одновременно.",
                "Руководство Мастера, стр. 138");

        /// <summary>
        /// Попытка получить пользу от более чем одного длинного отдыха за 24 часа.
        /// </summary>
        public static RuleViolation LongRestCooldown(Guid characterId)
            => new(characterId, "Отдых",
                "Персонаж не может получить пользу от более чем одного длинного отдыха за 24-часовой период.",
                "Книга игрока, стр. 186");

        /// <summary>
        /// Попытка потратить кость хитов, когда их не осталось.
        /// </summary>
        public static RuleViolation NoHitDiceAvailable(Guid characterId, int hitDieType)
            => new(characterId, "Кости хитов",
                $"Не осталось костей хитов типа к{hitDieType}.",
                "Книга игрока, стр. 186");

        /// <summary>
        /// Попытка надеть доспех без владения им.
        /// </summary>
        public static RuleViolation ArmorProficiencyRequired(Guid characterId, string armorName)
            => new(characterId, "Владение доспехами",
                $"Вы не владеете доспехом «{armorName}». Вы получаете помеху на атаки, спасброски и проверки характеристик.",
                "Книга игрока, стр. 144");

        /// <summary>
        /// Попытка скрытного перемещения в тяжёлом доспехе.
        /// </summary>
        public static RuleViolation StealthDisadvantageHeavyArmor(Guid characterId, string armorName)
            => new(characterId, "Скрытность",
                $"Вы получаете помеху на проверки Ловкости (Скрытность), если носите доспех «{armorName}».",
                "Книга игрока, стр. 144");

        /// <summary>
        /// Попытка сотворить заклинание без необходимых компонентов.
        /// </summary>
        public static RuleViolation MissingSpellComponent(Guid characterId, string spellId, string componentType)
            => new(characterId, "Компоненты заклинания",
                $"Невозможно сотворить «{spellId}»: отсутствует {componentType} компонент.",
                "Книга игрока, стр. 203");

        /// <summary>
        /// Превышение максимального уровня персонажа (20).
        /// </summary>
        public static RuleViolation MaximumLevelExceeded(Guid characterId)
            => new(characterId, "Предел уровня",
                "Персонаж не может превысить 20-й уровень.",
                "Книга игрока, стр. 15");

        /// <summary>
        /// Значение характеристики вне допустимого диапазона (1–30).
        /// </summary>
        public static RuleViolation AbilityScoreOutOfRange(Guid characterId, string ability, int score)
            => new(characterId, "Значения характеристик",
                $"Значение характеристики «{ability}» должно быть от 1 до 30. Указано: {score}.",
                "Книга игрока, стр. 173");

        /// <summary>
        /// Попытка подготовить больше заклинаний, чем позволяет уровень + модификатор заклинательной характеристики.
        /// </summary>
        public static RuleViolation TooManyPreparedSpells(Guid characterId, int prepared, int allowed)
            => new(characterId, "Подготовка заклинаний",
                $"Вы можете подготовить только {allowed} заклинаний, но попытались подготовить {prepared}.",
                "Книга игрока, стр. 114");

        /// <summary>
        /// Удивлённый персонаж не может использовать реакции.
        /// </summary>
        public static RuleViolation SurprisedNoReaction(Guid characterId)
            => new(characterId, "Неожиданность",
                "Удивлённое существо не может совершать реакции.",
                "Книга игрока, стр. 189");
    }
}