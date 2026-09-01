#nullable enable
using System;

namespace dnd_game.infrastructure.network
{
    /// <summary>
    /// Контракт для сообщений, содержащих идентификатор игровой сессии.
    /// Позволяет унифицировать обработку и маршрутизацию сообщений,
    /// связанных с конкретной сессией (кампанией).
    /// </summary>
    public interface IHasSessionId
    {
        /// <summary>
        /// Идентификатор игровой сессии (кампании).
        /// </summary>
        Guid SessionId { get; }
    }
}