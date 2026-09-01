#nullable enable
using System;

namespace dnd_game.domain.exceptions
{
    /// <summary>
    /// Исключение, возникающее при попытке выполнить действие, которое запрещено правилами DnD
    /// или невозможно в текущем состоянии персонажа/мира.
    /// </summary>
    public class InvalidAction : DomainError
    {
        /// <summary>
        /// Название действия (например, "Атака", "Применить заклинание", "Перемещение").
        /// </summary>
        public string ActionName { get; }

        /// <summary>
        /// Идентификатор персонажа, попытавшегося выполнить действие (если применимо).
        /// </summary>
        public Guid? CharacterId { get; }

        /// <summary>
        /// Дополнительная причина, объясняющая, почему действие недопустимо.
        /// </summary>
        public string Reason { get; }

        // ---------- Конструкторы ----------

        /// <summary>
        /// Создаёт исключение с общим сообщением (без указания конкретного действия или персонажа).
        /// </summary>
        /// <param name="message">Сообщение об ошибке.</param>
        public InvalidAction(string message)
            : base(message)
        {
            ActionName = string.Empty;
            CharacterId = null;
            Reason = message;
        }

        /// <summary>
        /// Создаёт исключение с указанием названия действия.
        /// </summary>
        /// <param name="actionName">Название действия.</param>
        /// <param name="message">Сообщение об ошибке.</param>
        public InvalidAction(string actionName, string message)
            : base($"Невозможно выполнить действие «{actionName}»: {message}")
        {
            ActionName = actionName;
            CharacterId = null;
            Reason = message;
        }

        /// <summary>
        /// Создаёт исключение для конкретного персонажа и действия.
        /// </summary>
        /// <param name="characterId">Идентификатор персонажа.</param>
        /// <param name="actionName">Название действия.</param>
        /// <param name="message">Сообщение об ошибке.</param>
        public InvalidAction(Guid characterId, string actionName, string message)
            : base($"Персонаж «{characterId}» не может выполнить действие «{actionName}»: {message}")
        {
            CharacterId = characterId;
            ActionName = actionName;
            Reason = message;
        }

        /// <summary>
        /// Создаёт исключение с полным контекстом (идентификатор персонажа, действие, причина и детальное сообщение).
        /// </summary>
        /// <param name="characterId">Идентификатор персонажа (может быть <c>null</c>).</param>
        /// <param name="actionName">Название действия.</param>
        /// <param name="reason">Краткая причина.</param>
        /// <param name="detailedMessage">Полное сообщение об ошибке.</param>
        public InvalidAction(Guid? characterId, string actionName, string reason, string detailedMessage)
            : base(detailedMessage)
        {
            CharacterId = characterId;
            ActionName = actionName;
            Reason = reason;
        }

        // ---------- Статические фабричные методы для типичных ситуаций ----------

        /// <summary>
        /// Персонаж мёртв и не может выполнять действия.
        /// </summary>
        public static InvalidAction CharacterIsDead(Guid characterId, string actionName)
            => new(characterId, actionName, "Персонаж мёртв.");

        /// <summary>
        /// Персонаж без сознания (0 хитов, не стабилизирован).
        /// </summary>
        public static InvalidAction CharacterIsUnconscious(Guid characterId, string actionName)
            => new(characterId, actionName, "Персонаж без сознания.");

        /// <summary>
        /// Персонаж ошеломлён (Stunned) и не может действовать.
        /// </summary>
        public static InvalidAction CharacterIsStunned(Guid characterId, string actionName)
            => new(characterId, actionName, "Персонаж ошеломлён.");

        /// <summary>
        /// Персонаж парализован.
        /// </summary>
        public static InvalidAction CharacterIsParalyzed(Guid characterId, string actionName)
            => new(characterId, actionName, "Персонаж парализован.");

        /// <summary>
        /// Недостаточно ресурсов (например, ячеек заклинаний).
        /// </summary>
        public static InvalidAction InsufficientResource(Guid characterId, string resourceName, int required, int available)
            => new(characterId, "Использование ресурса", $"Требуется {required} ед. «{resourceName}», но доступно только {available}.");

        /// <summary>
        /// Действие требует концентрации, но персонаж уже концентрируется на другом заклинании.
        /// </summary>
        public static InvalidAction AlreadyConcentrating(Guid characterId, string newSpellId, string currentSpellId)
            => new(characterId, "Начало концентрации", $"Уже концентрируется на «{currentSpellId}». Невозможно сконцентрироваться на «{newSpellId}».");

        /// <summary>
        /// Попытка использовать два основных действия за один ход.
        /// </summary>
        public static InvalidAction NoActionAvailable(Guid characterId)
            => new(characterId, "Основное действие", "В этом ходу больше нет доступного основного действия.");

        /// <summary>
        /// Попытка переместиться на расстояние, превышающее оставшуюся скорость.
        /// </summary>
        public static InvalidAction NotEnoughMovement(Guid characterId, int remaining, int requested)
            => new(characterId, "Перемещение", $"Осталось перемещения: {remaining} фт., запрошено: {requested} фт.");

        /// <summary>
        /// Попытка отдыха, когда он невозможен (например, в бою).
        /// </summary>
        public static InvalidAction RestNotAllowed(Guid characterId, string reason)
            => new(characterId, "Отдых", reason);

        /// <summary>
        /// Попытка изменить характеристику вне допустимых границ.
        /// </summary>
        public static InvalidAction InvalidAbilityScore(Guid characterId, string ability, int score)
            => new(characterId, "Установка характеристики", $"Значение характеристики «{ability}» не может быть равно {score}.");
    }
}