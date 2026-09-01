#nullable enable
using System;

namespace dnd_game.infrastructure.network
{
    /// <summary>
    /// Служебное сообщение-ответ на пинг (pong).
    /// Содержит временную метку отправки и идентификатор корреляции для сопоставления с исходным пингом.
    /// </summary>
    public class PongMessage : INetworkMessage
    {
        /// <inheritdoc />
        public MessageType Type => MessageType.Pong;

        /// <inheritdoc />
        public string? CorrelationId { get; set; }

        /// <summary>
        /// Время отправки понга (UTC). Позволяет клиенту вычислить задержку (RTT).
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Создаёт экземпляр сообщения Pong.
        /// </summary>
        public PongMessage()
        {
        }
    }
}