using System;
using System.Collections.Generic;

namespace dnd_game.application.projections.materialized_views
{
    /// <summary>
    /// Обзорная информация об игроке и его персонажах.
    /// </summary>
    public class PlayerOverview
    {
        /// <summary>
        /// Идентификатор игрока.
        /// </summary>
        public Guid PlayerId { get; set; }

        /// <summary>
        /// Отображаемое имя игрока.
        /// </summary>
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>
        /// Сводка по каждому персонажу, принадлежащему игроку.
        /// </summary>
        public List<CharacterSummary> Characters { get; set; } = [];
    }

    /// <summary>
    /// Краткая сводка о персонаже для списка персонажей игрока.
    /// </summary>
    public class CharacterSummary
    {
        /// <summary>
        /// Идентификатор персонажа.
        /// </summary>
        public Guid CharacterId { get; set; }

        /// <summary>
        /// Имя персонажа.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Текущий уровень персонажа.
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Класс персонажа (например, воин, маг).
        /// </summary>
        public string Class { get; set; } = string.Empty;

        /// <summary>
        /// Раса персонажа (например, эльф, дварф).
        /// </summary>
        public string Race { get; set; } = string.Empty;

        /// <summary>
        /// Предыстория (background) персонажа.
        /// </summary>
        public string Background { get; set; } = string.Empty;

        /// <summary>
        /// Текущее количество хитов.
        /// </summary>
        public int CurrentHitPoints { get; set; }

        /// <summary>
        /// Максимальное количество хитов.
        /// </summary>
        public int MaxHitPoints { get; set; }

        /// <summary>
        /// Количество временных хитов.
        /// </summary>
        public int TemporaryHitPoints { get; set; }

        /// <summary>
        /// Признак того, что персонаж жив.
        /// </summary>
        public bool IsAlive { get; set; }

        /// <summary>
        /// Класс брони (Armor Class).
        /// </summary>
        public int ArmorClass { get; set; }

        /// <summary>
        /// Скорость передвижения в футах.
        /// </summary>
        public int Speed { get; set; }

        /// <summary>
        /// Накопленный опыт.
        /// </summary>
        public int ExperiencePoints { get; set; }

        /// <summary>
        /// Опыт, необходимый для достижения следующего уровня.
        /// </summary>
        public int ExperienceToNextLevel { get; set; }

        /// <summary>
        /// Бонус мастерства.
        /// </summary>
        public int ProficiencyBonus { get; set; }

        /// <summary>
        /// Краткое описание активных состояний (например, "отравлен, оглушён").
        /// </summary>
        public string ConditionsSummary { get; set; } = string.Empty;

        /// <summary>
        /// Количество успешных спасбросков от смерти.
        /// </summary>
        public int DeathSaveSuccesses { get; set; }

        /// <summary>
        /// Количество проваленных спасбросков от смерти.
        /// </summary>
        public int DeathSaveFailures { get; set; }

        /// <summary>
        /// Стабилизирован ли персонаж (при 0 хитов и без угрозы смерти).
        /// </summary>
        public bool IsStable { get; set; }

        /// <summary>
        /// Список названий активных квестов персонажа.
        /// </summary>
        public List<string> ActiveQuestNames { get; set; } = [];

        /// <summary>
        /// Идентификатор текущей кампании, если персонаж участвует в ней.
        /// </summary>
        public Guid? CampaignId { get; set; }
    }
}