#nullable enable
using System;

namespace dnd_game.domain.exceptions
{
    // --------------------------------------------------------------------------------------------
    // Базовый класс доменного исключения
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Базовое исключение доменного слоя. Все специализированные исключения наследуются от него.
    /// </summary>
    public class DomainError : Exception
    {
        /// <summary>
        /// Создаёт экземпляр доменного исключения с указанным сообщением.
        /// </summary>
        /// <param name="message">Сообщение об ошибке.</param>
        public DomainError(string message) : base(message)
        {
        }

        /// <summary>
        /// Создаёт экземпляр доменного исключения с указанным сообщением и внутренним исключением.
        /// </summary>
        /// <param name="message">Сообщение об ошибке.</param>
        /// <param name="innerException">Внутреннее исключение.</param>
        public DomainError(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    // --------------------------------------------------------------------------------------------
    // Сущность не найдена
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, выбрасываемое при отсутствии сущности с указанным идентификатором.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение о том, что сущность указанного типа не найдена.
    /// </remarks>
    /// <param name="entityType">Тип сущности.</param>
    /// <param name="entityId">Идентификатор сущности.</param>
    public class EntityNotFoundException(string entityType, Guid entityId) : DomainError($"Сущность типа «{entityType}» с идентификатором «{entityId}» не найдена.")
    {
        /// <summary>Тип искомой сущности (например, "Персонаж", "Кампания").</summary>
        public string EntityType { get; } = entityType;

        /// <summary>Идентификатор искомой сущности.</summary>
        public Guid EntityId { get; } = entityId;
    }

    // --------------------------------------------------------------------------------------------
    // Конфликт состояния (оптимистическая блокировка)
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, сигнализирующее о конфликте версий агрегата при сохранении.
    /// Возникает, если агрегат был изменён другим процессом после его загрузки.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение о конфликте версий агрегата.
    /// </remarks>
    /// <param name="aggregateId">Идентификатор агрегата.</param>
    /// <param name="expectedVersion">Ожидаемая версия.</param>
    /// <param name="actualVersion">Фактическая версия.</param>
    public class StateConflictException(Guid aggregateId, int expectedVersion, int actualVersion) : DomainError($"Конфликт состояния для агрегата «{aggregateId}»: ожидалась версия {expectedVersion}, но обнаружена {actualVersion}.")
    {
        /// <summary>Идентификатор агрегата, с которым произошёл конфликт.</summary>
        public Guid AggregateId { get; } = aggregateId;

        /// <summary>Ожидаемая версия.</summary>
        public int ExpectedVersion { get; } = expectedVersion;

        /// <summary>Фактическая версия.</summary>
        public int ActualVersion { get; } = actualVersion;
    }

    // --------------------------------------------------------------------------------------------
    // Недостаточно ресурсов
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, выбрасываемое при нехватке ресурсов (золото, предметы, компоненты).
    /// </summary>
    /// <remarks>
    /// Создаёт исключение о нехватке ресурсов у персонажа.
    /// </remarks>
    /// <param name="characterId">Идентификатор персонажа.</param>
    /// <param name="resourceType">Тип ресурса.</param>
    /// <param name="required">Требуемое количество.</param>
    /// <param name="available">Доступное количество.</param>
    public class InsufficientResourcesException(Guid characterId, string resourceType, int required, int available) : DomainError($"Персонажу «{characterId}» требуется {required} ед. ресурса «{resourceType}», но доступно только {available}.")
    {
        /// <summary>Идентификатор персонажа, которому не хватает ресурсов.</summary>
        public Guid CharacterId { get; } = characterId;

        /// <summary>Тип ресурса (например, "золото", "компоненты").</summary>
        public string ResourceType { get; } = resourceType;

        /// <summary>Требуемое количество ресурса.</summary>
        public int Required { get; } = required;

        /// <summary>Фактически доступное количество.</summary>
        public int Available { get; } = available;
    }

    // --------------------------------------------------------------------------------------------
    // Ошибка боя
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, связанное с ошибками в боевой сцене.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение боевой сцены с указанным идентификатором боя и сообщением.
    /// </remarks>
    /// <param name="combatId">Идентификатор боя.</param>
    /// <param name="message">Сообщение об ошибке.</param>
    public class CombatException(Guid combatId, string message) : DomainError($"Бой «{combatId}»: {message}")
    {
        /// <summary>Идентификатор боя, в котором произошла ошибка.</summary>
        public Guid CombatId { get; } = combatId;
    }

    // --------------------------------------------------------------------------------------------
    // Ошибка заклинания
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, возникающее при неудачном применении заклинания.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение об ошибке применения заклинания.
    /// </remarks>
    /// <param name="casterId">Идентификатор заклинателя.</param>
    /// <param name="spellId">Идентификатор заклинания.</param>
    /// <param name="message">Сообщение об ошибке.</param>
    public class SpellFailureException(Guid casterId, string spellId, string message) : DomainError($"Ошибка заклинания: персонаж «{casterId}» не может применить «{spellId}»: {message}")
    {
        /// <summary>Идентификатор заклинателя.</summary>
        public Guid CasterId { get; } = casterId;

        /// <summary>Идентификатор (или название) заклинания.</summary>
        public string SpellId { get; } = spellId;
    }

    // --------------------------------------------------------------------------------------------
    // Ошибка передвижения
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, связанное с ошибками перемещения персонажа.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение об ошибке перемещения.
    /// </remarks>
    /// <param name="characterId">Идентификатор персонажа.</param>
    /// <param name="message">Сообщение об ошибке.</param>
    public class MovementException(Guid characterId, string message) : DomainError($"Ошибка перемещения персонажа «{characterId}»: {message}")
    {
        /// <summary>Идентификатор персонажа, с перемещением которого возникла проблема.</summary>
        public Guid CharacterId { get; } = characterId;
    }

    // --------------------------------------------------------------------------------------------
    // Ошибка отдыха
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, возникающее при ошибках, связанных с отдыхом персонажа.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение об ошибке отдыха.
    /// </remarks>
    /// <param name="characterId">Идентификатор персонажа.</param>
    /// <param name="message">Сообщение об ошибке.</param>
    public class RestException(Guid characterId, string message) : DomainError($"Ошибка отдыха персонажа «{characterId}»: {message}")
    {
        /// <summary>Идентификатор персонажа, с отдыхом которого возникла проблема.</summary>
        public Guid CharacterId { get; } = characterId;
    }

    // --------------------------------------------------------------------------------------------
    // Ошибка квеста
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, связанное с ошибками в квестах.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение об ошибке квеста.
    /// </remarks>
    /// <param name="questId">Идентификатор квеста.</param>
    /// <param name="message">Сообщение об ошибке.</param>
    public class QuestException(Guid questId, string message) : DomainError($"Квест «{questId}»: {message}")
    {
        /// <summary>Идентификатор квеста, с которым возникла проблема.</summary>
        public Guid QuestId { get; } = questId;
    }

    // --------------------------------------------------------------------------------------------
    // Неавторизованное действие
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Исключение, выбрасываемое при попытке выполнить действие без необходимых прав.
    /// </summary>
    /// <remarks>
    /// Создаёт исключение о неавторизованном действии.
    /// </remarks>
    /// <param name="userId">Идентификатор пользователя.</param>
    /// <param name="action">Название действия.</param>
    public class UnauthorizedActionException(Guid userId, string action) : DomainError($"Пользователь «{userId}» не авторизован для выполнения действия «{action}».")
    {
        /// <summary>Идентификатор пользователя, пытавшегося выполнить действие.</summary>
        public Guid UserId { get; } = userId;

        /// <summary>Название действия (операции), которое требовало авторизации.</summary>
        public string Action { get; } = action;
    }
}