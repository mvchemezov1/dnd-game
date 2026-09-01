#nullable enable
using System;

namespace dnd_game.infrastructure.network
{
    /// <summary>
    /// Сообщение, содержащее запрос (Query) к серверу.
    /// Включает информацию о типе запроса, его JSON-представлении,
    /// а также идентификаторы пользователя и игровой сессии для авторизации.
    /// </summary>
    public class QueryNetworkMessage : INetworkMessage
    {
        /// <inheritdoc />
        public MessageType Type => MessageType.Query;

        /// <summary>
        /// Полное имя типа запроса (AssemblyQualifiedName).
        /// </summary>
        public string QueryTypeName { get; set; } = string.Empty;

        /// <summary>
        /// JSON-сериализованный объект запроса.
        /// </summary>
        public string QueryJson { get; set; } = string.Empty;

        /// <summary>
        /// Идентификатор пользователя, отправившего запрос.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Идентификатор игровой сессии (кампании), в рамках которой выполняется запрос.
        /// </summary>
        public Guid SessionId { get; set; }

        /// <inheritdoc />
        public string? CorrelationId { get; set; }
    }
}