#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using dnd_game.domain.commands;
using dnd_game.domain.events;

namespace dnd_game.infrastructure.network
{
    /// <summary>
    /// Версия сетевого протокола для обеспечения совместимости клиента и сервера.
    /// </summary>
    public enum ProtocolVersion : byte
    {
        Version1 = 1,
        Current = Version1
    }

    /// <summary>
    /// Тип сообщения в сетевом протоколе.
    /// </summary>
    public enum MessageType : byte
    {
        Command = 1,
        CommandResponse = 2,
        Event = 3,
        Query = 4,
        QueryResponse = 5,
        AuthRequest = 6,
        AuthResponse = 7,
        Error = 8,
        Ping = 9,
        Pong = 10,
        Disconnect = 11,
        UndoRequest = 12,
        RedoRequest = 13,
        UndoResponse = 14,
        RedoResponse = 15
    }

    /// <summary>
    /// Флаги сообщения (могут комбинироваться).
    /// </summary>
    [Flags]
    public enum MessageFlags : byte
    {
        None = 0,
        Compressed = 1,
        Encrypted = 2,
        Fragmented = 4,
        LastFragment = 8,
        RequiresAck = 16,
        QueryResponse = 32
    }

    /// <summary>
    /// Заголовок сообщения сетевого протокола. Первые 11 байт содержат основную информацию,
    /// оставшиеся 2 байта зарезервированы для будущего использования.
    /// </summary>
    public class MessageHeader
    {
        public const int HeaderSize = 13; // байт (11 основных + 2 зарезервированных)
        public ProtocolVersion Version { get; set; } = ProtocolVersion.Current;
        public MessageType Type { get; set; }
        public MessageFlags Flags { get; set; }
        public uint MessageId { get; set; }          // для отслеживания и подтверждений
        public uint PayloadLength { get; set; }       // длина полезной нагрузки после заголовка (до сжатия/шифрования)

        /// <summary>Сериализует заголовок в массив байт.</summary>
        public byte[] Serialize()
        {
            var buffer = new byte[HeaderSize];
            buffer[0] = (byte)Version;
            buffer[1] = (byte)Type;
            buffer[2] = (byte)Flags;
            BitConverter.GetBytes(MessageId).CopyTo(buffer, 3);
            BitConverter.GetBytes(PayloadLength).CopyTo(buffer, 7);
            // Байты 11 и 12 остаются нулевыми (зарезервированы)
            return buffer;
        }

        /// <summary>Десериализует заголовок из массива байт, начиная с указанного смещения.</summary>
        public static MessageHeader Deserialize(byte[] buffer, int offset = 0)
        {
            if (buffer.Length - offset < HeaderSize)
                throw new ArgumentException("Буфер слишком мал для заголовка.", nameof(buffer));
            return new MessageHeader
            {
                Version = (ProtocolVersion)buffer[offset],
                Type = (MessageType)buffer[offset + 1],
                Flags = (MessageFlags)buffer[offset + 2],
                MessageId = BitConverter.ToUInt32(buffer, offset + 3),
                PayloadLength = BitConverter.ToUInt32(buffer, offset + 7)
            };
        }
    }

    /// <summary>
    /// Базовый интерфейс всех сетевых сообщений.
    /// </summary>
    public interface INetworkMessage
    {
        MessageType Type { get; }
        string? CorrelationId { get; set; }
    }

    // ---------- Конкретные типы сообщений ----------

    /// <summary>Сообщение, содержащее команду.</summary>
    public class CommandNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.Command;
        public string CommandTypeName { get; set; } = string.Empty;
        public string CommandJson { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public Guid SessionId { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>Сообщение с событием.</summary>
    public class EventNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.Event;
        public string EventTypeName { get; set; } = string.Empty;
        public string EventJson { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
    }

    /// <summary>Ответ на команду.</summary>
    public class CommandResponseNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.CommandResponse;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResultJson { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>Запрос отмены последнего действия (undo).</summary>
    public class UndoNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.UndoRequest;
        public string? CorrelationId { get; set; }
    }

    /// <summary>Запрос повтора отменённого действия (redo).</summary>
    public class RedoNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.RedoRequest;
        public string? CorrelationId { get; set; }
    }

    /// <summary>Ответ на undo-запрос.</summary>
    public class UndoResponseNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.UndoResponse;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>Ответ на redo-запрос.</summary>
    public class RedoResponseNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.RedoResponse;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>Сообщение об ошибке.</summary>
    public class ErrorNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.Error;
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>Запрос аутентификации.</summary>
    public class AuthRequestMessage : INetworkMessage
    {
        public MessageType Type => MessageType.AuthRequest;
        public string Token { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
    }

    /// <summary>Ответ аутентификации.</summary>
    public class AuthResponseMessage : INetworkMessage
    {
        public MessageType Type => MessageType.AuthResponse;
        public bool Success { get; set; }
        public Guid? UserId { get; set; }
        public string? Error { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>Ответ на запрос (query).</summary>
    public class QueryResponseNetworkMessage : INetworkMessage
    {
        public MessageType Type => MessageType.QueryResponse;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResultJson { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>Сообщение об отключении.</summary>
    public class DisconnectMessage : INetworkMessage
    {
        public MessageType Type => MessageType.Disconnect;
        public string? Reason { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>
    /// Интерфейс кодирования/декодирования сетевых сообщений.
    /// </summary>
    public interface INetworkProtocol
    {
        byte[] Encode(INetworkMessage message);
        IReadOnlyList<INetworkMessage> Decode(byte[] data);
        bool TryDecodeHeader(byte[] data, out MessageHeader header);
    }

    /// <summary>
    /// Реализация протокола с JSON-сериализацией и опциональным GZip-сжатием.
    /// </summary>
    public class JsonNetworkProtocol : INetworkProtocol
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public JsonNetworkProtocol()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public byte[] Encode(INetworkMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            string payloadJson = JsonSerializer.Serialize(message, message.GetType(), _jsonOptions);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            bool compressed = false;

            // Сжимаем, если размер больше 512 байт
            if (payloadBytes.Length > 512)
            {
                payloadBytes = Compress(payloadBytes);
                compressed = true;
            }

            var header = new MessageHeader
            {
                Type = message.Type,
                PayloadLength = (uint)payloadBytes.Length,
                Flags = compressed ? MessageFlags.Compressed : MessageFlags.None
            };

            byte[] headerBytes = header.Serialize();
            byte[] packet = new byte[headerBytes.Length + payloadBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
            Buffer.BlockCopy(payloadBytes, 0, packet, headerBytes.Length, payloadBytes.Length);
            return packet;
        }

        public IReadOnlyList<INetworkMessage> Decode(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var messages = new List<INetworkMessage>();
            int offset = 0;
            while (offset + MessageHeader.HeaderSize <= data.Length)
            {
                var header = MessageHeader.Deserialize(data, offset);
                offset += MessageHeader.HeaderSize;
                if (offset + (int)header.PayloadLength > data.Length)
                    break; // неполный пакет, ждём остальное

                byte[] payload = new byte[header.PayloadLength];
                Buffer.BlockCopy(data, offset, payload, 0, (int)header.PayloadLength);
                offset += (int)header.PayloadLength;

                if (header.Flags.HasFlag(MessageFlags.Compressed))
                    payload = Decompress(payload);

                string json = Encoding.UTF8.GetString(payload);
                INetworkMessage? msg = DeserializeByType(header.Type, json);
                if (msg != null)
                    messages.Add(msg);
            }
            return messages;
        }

        public bool TryDecodeHeader(byte[] data, out MessageHeader header)
        {
            header = null!;
            if (data.Length < MessageHeader.HeaderSize) return false;
            header = MessageHeader.Deserialize(data);
            return true;
        }

        private INetworkMessage? DeserializeByType(MessageType type, string json)
        {
            Type targetType = type switch
            {
                MessageType.Command => typeof(CommandNetworkMessage),
                MessageType.Query => typeof(QueryNetworkMessage),
                MessageType.Event => typeof(EventNetworkMessage),
                MessageType.CommandResponse => typeof(CommandResponseNetworkMessage),
                MessageType.QueryResponse => typeof(QueryResponseNetworkMessage),
                MessageType.Error => typeof(ErrorNetworkMessage),
                MessageType.AuthRequest => typeof(AuthRequestMessage),
                MessageType.AuthResponse => typeof(AuthResponseMessage),
                MessageType.UndoRequest => typeof(UndoNetworkMessage),
                MessageType.RedoRequest => typeof(RedoNetworkMessage),
                MessageType.UndoResponse => typeof(UndoResponseNetworkMessage),
                MessageType.RedoResponse => typeof(RedoResponseNetworkMessage),
                MessageType.Ping => typeof(PingMessage),
                MessageType.Pong => typeof(PongMessage),
                MessageType.Disconnect => typeof(DisconnectMessage),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Неизвестный тип сообщения: {type}")
            };

            return (INetworkMessage?)JsonSerializer.Deserialize(json, targetType, _jsonOptions);
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
                gzip.Write(data, 0, data.Length);
            return output.ToArray();
        }

        private static byte[] Decompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }

    /// <summary>
    /// Фабрика для создания сетевых сообщений из доменных объектов.
    /// </summary>
    public static class NetworkMessageFactory
    {
        public static CommandNetworkMessage FromCommand(ICommand command, Guid userId, Guid sessionId, string? correlationId = null)
        {
            ArgumentNullException.ThrowIfNull(command);
            return new CommandNetworkMessage
            {
                CommandTypeName = command.GetType().AssemblyQualifiedName!,
                CommandJson = JsonSerializer.Serialize(command, command.GetType()),
                UserId = userId,
                SessionId = sessionId,
                CorrelationId = correlationId
            };
        }

        public static EventNetworkMessage FromEvent(IDomainEvent @event, string? correlationId = null)
        {
            ArgumentNullException.ThrowIfNull(@event);
            return new EventNetworkMessage
            {
                EventTypeName = @event.GetType().AssemblyQualifiedName!,
                EventJson = JsonSerializer.Serialize(@event, @event.GetType()),
                CorrelationId = correlationId
            };
        }

        public static ErrorNetworkMessage CreateError(string errorCode, string message, string? detail = null, string? correlationId = null)
        {
            return new ErrorNetworkMessage
            {
                ErrorCode = errorCode,
                Message = message,
                Detail = detail,
                CorrelationId = correlationId
            };
        }
    }
}