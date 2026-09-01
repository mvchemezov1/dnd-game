namespace dnd_game.application.projections.materialized_views
{
    /// <summary>
    /// Полное состояние боя для отображения в пользовательском интерфейсе.
    /// </summary>
    public class CombatStatusView
    {
        /// <summary>Идентификатор боя.</summary>
        public Guid CombatId { get; set; }

        /// <summary>Признак того, что бой активен в данный момент.</summary>
        public bool Active { get; set; }

        /// <summary>Текущий раунд (начиная с 1).</summary>
        public int Round { get; set; }

        /// <summary>Индекс активного участника в списке <see cref="Participants"/>.</summary>
        public int CurrentTurnIndex { get; set; }

        /// <summary>Имя персонажа, чей сейчас ход, либо «Нет», если ход отсутствует.</summary>
        public string CurrentTurnCharacterName { get; set; } = string.Empty;

        /// <summary>Список всех участников боя с подробной информацией.</summary>
        public List<CombatParticipantView> Participants { get; set; } = [];
    }

    /// <summary>
    /// Представление участника боя для пользовательского интерфейса.
    /// </summary>
    public class CombatParticipantView
    {
        /// <summary>Идентификатор персонажа.</summary>
        public Guid CharacterId { get; set; }

        /// <summary>Имя персонажа.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Значение инициативы (чем выше, тем раньше ходит).</summary>
        public int Initiative { get; set; }

        /// <summary>Текущее количество хитов.</summary>
        public int CurrentHitPoints { get; set; }

        /// <summary>Максимальное количество хитов.</summary>
        public int MaxHitPoints { get; set; }

        /// <summary>Количество временных хитов.</summary>
        public int TemporaryHitPoints { get; set; }

        /// <summary>Класс брони (AC).</summary>
        public int ArmorClass { get; set; }

        /// <summary>Оставшаяся скорость передвижения в футах.</summary>
        public int MovementRemaining { get; set; }

        /// <summary>Доступно ли основное действие.</summary>
        public bool HasAction { get; set; }

        /// <summary>Доступно ли бонусное действие.</summary>
        public bool HasBonusAction { get; set; }

        /// <summary>Доступна ли реакция.</summary>
        public bool HasReaction { get; set; }

        /// <summary>Активные состояния (оглох, ослеплён и т. п.).</summary>
        public List<string> Conditions { get; set; } = [];

        /// <summary>Поддерживает ли концентрацию на заклинании.</summary>
        public bool Concentrating { get; set; }

        /// <summary>Текущий статус жизни/смерти.</summary>
        public DeathStatus DeathStatus { get; set; }

        /// <summary>Количество успешных спасбросков от смерти.</summary>
        public int DeathSaveSuccesses { get; set; }

        /// <summary>Количество проваленных спасбросков от смерти.</summary>
        public int DeathSaveFailures { get; set; }
    }

    /// <summary>
    /// Перечисление возможных состояний жизни и смерти персонажа.
    /// </summary>
    public enum DeathStatus
    {
        /// <summary>Жив.</summary>
        Alive,

        /// <summary>При смерти (умирает).</summary>
        Dying,

        /// <summary>Стабилен (не теряет хиты, но без сознания).</summary>
        Stable,

        /// <summary>Мёртв.</summary>
        Dead
    }
}