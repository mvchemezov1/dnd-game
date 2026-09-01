#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.rules
{
    /// <summary>
    /// Правила, связанные с магией и заклинаниями в DnD 5e.
    /// Содержит базу данных заклинаний, таблицы ячеек, проверки компонентов и другие расчёты.
    /// </summary>
    public static class MagicRules
    {
        /// <summary>
        /// Подробная информация о заклинании.
        /// </summary>
        /// <param name="Name">Название заклинания.</param>
        /// <param name="Level">Уровень заклинания (0 — заговор).</param>
        /// <param name="IsCantrip">Является ли заговором.</param>
        /// <param name="RequiresConcentration">Требует ли концентрации.</param>
        /// <param name="RangeFeet">Дальность в футах.</param>
        /// <param name="CastingTime">Время накладывания.</param>
        /// <param name="DurationRounds">Длительность в раундах (0 — мгновенное).</param>
        /// <param name="Components">Строка компонентов (например, "V, S, M").</param>
        /// <param name="School">Школа магии.</param>
        /// <param name="IsRitual">Можно ли сотворить как ритуал.</param>
        /// <param name="AreaOfEffect">Область воздействия (опционально).</param>
        /// <param name="MaxTargets">Максимальное количество целей (0 — неограниченно или область).</param>
        public record SpellInfo(
            string Name,
            int Level,
            bool IsCantrip,
            bool RequiresConcentration,
            int RangeFeet,
            CastingTimeType CastingTime,
            int DurationRounds,
            string Components,
            MagicSchool School,
            bool IsRitual,
            string? AreaOfEffect = null,
            int MaxTargets = 1
        );

        /// <summary>Время накладывания заклинания.</summary>
        public enum CastingTimeType
        {
            Action,
            BonusAction,
            Reaction,
            Minute1,
            Minute10,
            Hour1,
            Special
        }

        /// <summary>Школа магии.</summary>
        public enum MagicSchool
        {
            Abjuration, Conjuration, Divination, Enchantment,
            Evocation, Illusion, Necromancy, Transmutation
        }

        /// <summary>
        /// База данных известных заклинаний. В реальном проекте может загружаться из БД или файла.
        /// </summary>
        public static class SpellDatabase
        {
            private static readonly Dictionary<string, SpellInfo> Spells = new(StringComparer.OrdinalIgnoreCase)
            {
                // Примеры заклинаний с полной информацией
                ["firebolt"] = new SpellInfo(
                    "Огненный снаряд", 0, true, false,
                    120, CastingTimeType.Action, 0, "V, S",
                    MagicSchool.Evocation, false, MaxTargets: 1),

                ["magehand"] = new SpellInfo(
                    "Волшебная рука", 0, true, false,
                    30, CastingTimeType.Action, 1, "V, S",
                    MagicSchool.Conjuration, false, MaxTargets: 1),

                ["magicmissile"] = new SpellInfo(
                    "Волшебная стрела", 1, false, false,
                    120, CastingTimeType.Action, 0, "V, S",
                    MagicSchool.Evocation, false, MaxTargets: 3),

                ["shield"] = new SpellInfo(
                    "Щит", 1, false, false,
                    0, CastingTimeType.Reaction, 1, "V, S",
                    MagicSchool.Abjuration, false, MaxTargets: 1),

                ["bless"] = new SpellInfo(
                    "Благословение", 1, false, true,
                    30, CastingTimeType.Action, 10, "V, S, M",
                    MagicSchool.Enchantment, false, MaxTargets: 3),

                ["haste"] = new SpellInfo(
                    "Ускорение", 3, false, true,
                    30, CastingTimeType.Action, 10, "V, S, M",
                    MagicSchool.Transmutation, false, MaxTargets: 1),

                ["fireball"] = new SpellInfo(
                    "Огненный шар", 3, false, false,
                    150, CastingTimeType.Action, 0, "V, S, M",
                    MagicSchool.Evocation, false, AreaOfEffect: "Сфера 20 фт.", MaxTargets: 0),

                ["polymorph"] = new SpellInfo(
                    "Превращение", 4, false, true,
                    60, CastingTimeType.Action, 600, "V, S, M",
                    MagicSchool.Transmutation, false, MaxTargets: 1),

                ["wallofforce"] = new SpellInfo(
                    "Стена силы", 5, false, true,
                    120, CastingTimeType.Action, 100, "V, S, M",
                    MagicSchool.Evocation, false, AreaOfEffect: "Стена 10x10 фт.", MaxTargets: 0),
            };

            /// <summary>
            /// Получить информацию о заклинании по его идентификатору (ключу).
            /// </summary>
            /// <param name="spellId">Идентификатор заклинания (например, "fireball").</param>
            /// <returns>Информация о заклинании или <c>null</c>, если заклинание не найдено.</returns>
            public static SpellInfo? GetSpell(string spellId)
            {
                if (string.IsNullOrWhiteSpace(spellId))
                    return null;
                Spells.TryGetValue(spellId, out var spell);
                return spell;
            }

            /// <summary>
            /// Получить информацию о заклинании или выбросить исключение, если оно отсутствует.
            /// </summary>
            /// <param name="spellId">Идентификатор заклинания.</param>
            /// <returns>Информация о заклинании.</returns>
            /// <exception cref="ArgumentException">Если заклинание не найдено.</exception>
            public static SpellInfo GetRequiredSpell(string spellId)
            {
                var spell = GetSpell(spellId) ?? throw new ArgumentException($"Заклинание с идентификатором «{spellId}» не найдено.", nameof(spellId));
                return spell;
            }
        }

        // --------------------------------------------------------------------------------------------
        // Общие проверки и расчёты, не зависящие от конкретного заклинания
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Проверить, известно ли заклинание системе.
        /// </summary>
        /// <param name="spellId">Идентификатор заклинания.</param>
        /// <returns><c>true</c>, если заклинание существует в базе.</returns>
        public static bool CanCastSpell(string spellId)
        {
            return SpellDatabase.GetSpell(spellId) != null;
        }

        /// <summary>
        /// Сложность спасброска от заклинания (Spell Save DC).
        /// </summary>
        public static int SpellSaveDifficulty(int proficiencyBonus, int spellcastingAbilityModifier)
            => 8 + proficiencyBonus + spellcastingAbilityModifier;

        /// <summary>
        /// Модификатор броска атаки заклинанием (Spell Attack Modifier).
        /// </summary>
        public static int SpellAttackModifier(int proficiencyBonus, int spellcastingAbilityModifier)
            => proficiencyBonus + spellcastingAbilityModifier;

        /// <summary>
        /// Количество подготовленных заклинаний для классов, которые готовят заклинания (жрец, друид, волшебник).
        /// </summary>
        public static int PreparedSpellsCount(int level, int spellcastingAbilityModifier)
            => level + Math.Max(1, spellcastingAbilityModifier);

        // --------------------------------------------------------------------------------------------
        // Таблицы ячеек заклинаний
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Ячейки заклинаний для полных заклинателей (волшебник, жрец, друид, бард, чародей).
        /// </summary>
        public static Dictionary<int, int> FullCasterSpellSlots(int level)
        {
            ValidateCharacterLevel(level);
            return level switch
            {
                1 => new() { { 1, 2 } },
                2 => new() { { 1, 3 } },
                3 => new() { { 1, 4 }, { 2, 2 } },
                4 => new() { { 1, 4 }, { 2, 3 } },
                5 => new() { { 1, 4 }, { 2, 3 }, { 3, 2 } },
                6 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 } },
                7 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 1 } },
                8 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 2 } },
                9 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 1 } },
                10 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 } },
                11 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 }, { 6, 1 } },
                12 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 }, { 6, 1 } },
                13 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 }, { 6, 1 }, { 7, 1 } },
                14 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 }, { 6, 1 }, { 7, 1 } },
                15 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 }, { 6, 1 }, { 7, 1 }, { 8, 1 } },
                16 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 }, { 6, 1 }, { 7, 1 }, { 8, 1 } },
                17 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 }, { 6, 1 }, { 7, 1 }, { 8, 1 }, { 9, 1 } },
                18 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 3 }, { 6, 1 }, { 7, 1 }, { 8, 1 }, { 9, 1 } },
                19 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 3 }, { 6, 2 }, { 7, 1 }, { 8, 1 }, { 9, 1 } },
                20 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 3 }, { 6, 2 }, { 7, 2 }, { 8, 1 }, { 9, 1 } },
                _ => [] // недостижимо из-за валидации
            };
        }

        /// <summary>
        /// Ячейки заклинаний для половинных заклинателей (паладин, следопыт).
        /// </summary>
        public static Dictionary<int, int> HalfCasterSpellSlots(int level)
        {
            ValidateCharacterLevel(level);
            return level switch
            {
                2 => new() { { 1, 2 } },
                3 => new() { { 1, 3 } },
                4 => new() { { 1, 3 } },
                5 => new() { { 1, 4 }, { 2, 2 } },
                6 => new() { { 1, 4 }, { 2, 2 } },
                7 => new() { { 1, 4 }, { 2, 3 } },
                8 => new() { { 1, 4 }, { 2, 3 } },
                9 => new() { { 1, 4 }, { 2, 3 }, { 3, 2 } },
                10 => new() { { 1, 4 }, { 2, 3 }, { 3, 2 } },
                11 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 } },
                12 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 } },
                13 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 1 } },
                14 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 1 } },
                15 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 2 } },
                16 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 2 } },
                17 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 1 } },
                18 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 1 } },
                19 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 } },
                20 => new() { { 1, 4 }, { 2, 3 }, { 3, 3 }, { 4, 3 }, { 5, 2 } },
                _ => []
            };
        }

        // --------------------------------------------------------------------------------------------
        // Методы, зависящие от конкретного заклинания (используют базу данных)
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Требует ли заклинание концентрации.
        /// </summary>
        public static bool RequiresConcentration(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).RequiresConcentration;
        }

        /// <summary>
        /// Является ли заклинание заговором.
        /// </summary>
        public static bool IsCantrip(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).IsCantrip;
        }

        /// <summary>
        /// Дальность заклинания в футах.
        /// </summary>
        public static int GetSpellRange(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).RangeFeet;
        }

        /// <summary>
        /// Время накладывания заклинания.
        /// </summary>
        public static CastingTimeType GetCastingTime(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).CastingTime;
        }

        /// <summary>
        /// Длительность заклинания в раундах (0 — мгновенное).
        /// </summary>
        public static int GetDurationRounds(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).DurationRounds;
        }

        /// <summary>
        /// Школа магии заклинания.
        /// </summary>
        public static MagicSchool GetSchool(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).School;
        }

        /// <summary>
        /// Содержит ли заклинание вербальный компонент.
        /// </summary>
        public static bool HasVerbalComponent(string spellId)
        {
            var components = SpellDatabase.GetRequiredSpell(spellId).Components;
            return components.Contains('V', StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Содержит ли заклинание соматический компонент.
        /// </summary>
        public static bool HasSomaticComponent(string spellId)
        {
            var components = SpellDatabase.GetRequiredSpell(spellId).Components;
            return components.Contains('S', StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Содержит ли заклинание материальный компонент и возвращает его детали.
        /// </summary>
        /// <param name="spellId">Идентификатор заклинания.</param>
        /// <param name="materialName">Название материального компонента (если есть).</param>
        /// <param name="costGold">Стоимость компонента в золотых (если указана).</param>
        /// <returns><c>true</c>, если компонент требуется.</returns>
        public static bool HasMaterialComponent(string spellId, out string? materialName, out int? costGold)
        {
            var spell = SpellDatabase.GetRequiredSpell(spellId);
            if (spell.Components.Contains('M', StringComparison.OrdinalIgnoreCase))
            {
                // В текущей модели нет деталей о материале, поэтому возвращаем заглушку.
                materialName = "Неизвестный материал";
                costGold = null;
                return true;
            }
            materialName = null;
            costGold = null;
            return false;
        }

        /// <summary>
        /// Можно ли сотворить заклинание как ритуал.
        /// </summary>
        public static bool IsRitual(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).IsRitual;
        }

        /// <summary>
        /// Максимальное количество целей заклинания (0 — область воздействия).
        /// </summary>
        public static int GetMaxTargets(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).MaxTargets;
        }

        /// <summary>
        /// Радиус области воздействия (если применимо).
        /// </summary>
        public static string? GetAreaOfEffect(string spellId)
        {
            return SpellDatabase.GetRequiredSpell(spellId).AreaOfEffect;
        }

        // --------------------------------------------------------------------------------------------
        // Общие правила, не требующие конкретного заклинания
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Можно ли сотворить заклинание действием после того, как было использовано заклинание бонусным действием.
        /// </summary>
        public static bool CanCastActionSpellAfterBonusAction(string spellCastAsAction)
        {
            // Если бонусным действием наложено заклинание, то основным действием можно наложить только заговор.
            if (string.IsNullOrWhiteSpace(spellCastAsAction))
                return true; // нет ограничений, если заклинание не указано
            return IsCantrip(spellCastAsAction);
        }

        /// <summary>
        /// Сложность проверки концентрации при получении урона.
        /// </summary>
        public static int ConcentrationCheckDC(int damageTaken)
            => Math.Max(10, damageTaken / 2);

        /// <summary>
        /// Проверка, находится ли цель в пределах дальности заклинания.
        /// </summary>
        public static bool IsTargetInRange(int distanceFeet, int spellRangeFeet)
            => distanceFeet <= spellRangeFeet;

        /// <summary>
        /// Может ли персонаж использовать свиток заклинания.
        /// </summary>
        public static bool CanUseSpellScroll(int characterLevel, int spellLevel, bool spellIsOnClassList)
            => spellIsOnClassList && characterLevel >= (spellLevel * 2 - 1);

        /// <summary>
        /// Проверка возможности контрзаклинания (Counterspell).
        /// </summary>
        public static bool CanCounterSpell(int slotLevelAvailable, int targetSpellLevel, int abilityCheckBonus = 0)
        {
            if (slotLevelAvailable >= targetSpellLevel)
                return true;
            // Если ячейка ниже, требуется проверка способности против DC 10 + уровень заклинания.
            int dc = 10 + targetSpellLevel;
            return abilityCheckBonus >= dc; // упрощённо, реальная проверка требует броска
        }

        /// <summary>
        /// Сложность проверки для рассеивания заклинания (Dispel Magic).
        /// </summary>
        public static int DispelCheckDC(int targetSpellLevel)
            => 10 + targetSpellLevel;

        // --------------------------------------------------------------------------------------------
        // Вспомогательные методы
        // --------------------------------------------------------------------------------------------

        private static void ValidateCharacterLevel(int level)
        {
            if (level < 1 || level > 20)
                throw new ArgumentOutOfRangeException(nameof(level), "Уровень персонажа должен быть от 1 до 20.");
        }
    }
}