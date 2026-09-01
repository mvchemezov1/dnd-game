#nullable enable
using System;

namespace dnd_game.infrastructure.network
{
    /// <summary>
    /// Служебное сообщение для проверки живости соединения (ping).
    /// Содержит временную метку отправки, которая может использоваться
    /// для измерения задержки и контроля таймаутов.
    /// </summary>
    public class PingMessage : INetworkMessage
    {
        /// <inheritdoc />
        public MessageType Type => MessageType.Ping;

        /// <inheritdoc />
        public string? CorrelationId { get; set; }

        /// <summary>
        /// Время отправки пинга (UTC). Позволяет клиенту вычислить RTT.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Создаёт экземпляр сообщения Ping. По умолчанию временная метка
        /// устанавливается в текущее время UTC.
        /// </summary>
        public PingMessage()
        {
        }
    }
}