#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.queries;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.monitoring;
using dnd_game.infrastructure.security;

namespace dnd_game.infrastructure.network
{
    // ---------- Перечисления и контексты ----------

    /// <summary>Состояние клиентского подключения.</summary>
    public enum ConnectionState
    {
        Connecting,
        Authenticated,
        Disconnecting,
        Disconnected
    }

    /// <summary>Протокол передачи данных.</summary>
    public enum TransportProtocol
    {
        WebSocket,
        Tcp
    }

    /// <summary>Менеджер игровых сессий.</summary>
    public interface ISessionManager
    {
        Task<Guid> CreateSession(Guid userId, string campaignId);
        Task JoinSession(Guid sessionId, Guid userId);
        Task LeaveSession(Guid sessionId, Guid userId);
        Task<bool> IsUserInSession(Guid userId, Guid sessionId);
        Task<IEnumerable<Guid>> GetSessionUsers(Guid sessionId);
        Task<CampaignRole?> GetUserRole(Guid userId, Guid sessionId);
        Task AssociateConnection(Guid userId, Guid sessionId, Guid connectionId, CancellationToken cancellationToken);
        void RemoveConnection(Guid connectionId);
    }

    /// <summary>Сообщение сетевого протокола (JSON).</summary>
    public class NetworkMessage
    {
        public string Type { get; set; } = string.Empty;        // "command", "event", "auth", "error"
        public string PayloadType { get; set; } = string.Empty; // например, "CreateCharacter", "CharacterDamageTaken"
        public string Payload { get; set; } = string.Empty;     // JSON-сериализованные данные
        public string CorrelationId { get; set; } = string.Empty; // для сопоставления запрос-ответ
    }

    /// <summary>Конфигурация игрового сервера.</summary>
    public class GameServerConfiguration
    {
        public int WebSocketPort { get; set; } = 5000;
        public int TcpPort { get; set; } = 5001;
        public int MaxConnectionsPerUser { get; set; } = 3;
        public int MaxMessageSizeBytes { get; set; } = 65536;
        public bool RequireAuthentication { get; set; } = true;
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>Интерфейс клиентского подключения.</summary>
    public interface IClientConnection
    {
        Guid ConnectionId { get; }
        Guid? UserId { get; set; }
        Guid? SessionId { get; set; }
        ConnectionState State { get; set; }
        TransportProtocol Protocol { get; }
        Task SendAsync(ArraySegment<byte> data, CancellationToken cancellationToken);
        Task CloseAsync(CancellationToken cancellationToken);
        event Func<IClientConnection, byte[], Task>? MessageReceived;
    }

    // ---------- Основной сервер ----------

    /// <summary>
    /// Основной игровой сервер, принимающий подключения по WebSocket и TCP,
    /// обрабатывающий команды, запросы и транслирующий события клиентам.
    /// </summary>
    public class GameServer(
        GameServerConfiguration config,
        IServiceProvider serviceProvider,
        ICommandBus commandBus,
        IEventBus eventBus,
        IQueryBus queryBus,
        ISessionManager sessionManager,
        PermissionChecker permissionChecker,
        IMetricsCollector metricsCollector,
        ITracer tracer,
        IAuthProvider authProvider,
        ILogger<GameServer> logger)
    {
        private readonly GameServerConfiguration _config = config ?? throw new ArgumentNullException(nameof(config));
        private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly IEventBus _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        private readonly IQueryBus _queryBus = queryBus ?? throw new ArgumentNullException(nameof(queryBus));
        private readonly ISessionManager _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        private readonly IMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        private readonly ITracer _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        private readonly ILogger<GameServer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IAuthProvider _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
        private readonly object _connectionLock = new();

        private readonly ConcurrentDictionary<Guid, IClientConnection> _connections = new();
        private readonly ConcurrentDictionary<Guid, List<Guid>> _userConnections = new();

        private HttpListener? _webSocketListener;
        private TcpListener? _tcpListener;

        /// <summary>Запускает сервер и начинает прослушивание портов.</summary>
        public async Task Start(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Запуск игрового сервера...");

            // Подписываемся на все доменные события для рассылки клиентам
            _eventBus.Subscribe<IDomainEvent>(OnDomainEvent);

            // WebSocket
            _webSocketListener = new HttpListener();
            _webSocketListener.Prefixes.Add($"http://+:{_config.WebSocketPort}/ws/");
            _webSocketListener.Start();
            _ = Task.Run(() => AcceptWebSocketConnections(cancellationToken), cancellationToken);

            // TCP
            _tcpListener = new TcpListener(IPAddress.Any, _config.TcpPort);
            _tcpListener.Start();
            _ = Task.Run(() => AcceptTcpConnections(cancellationToken), cancellationToken);

            _logger.LogInformation("Игровой сервер запущен. WebSocket: {WsPort}, TCP: {TcpPort}",
                _config.WebSocketPort, _config.TcpPort);

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        // ---------- Приём подключений ----------

        private async Task AcceptWebSocketConnections(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _webSocketListener!.GetContextAsync();
                    if (!context.Request.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                        continue;
                    }

                    var wsContext = await context.AcceptWebSocketAsync(null);
                    var connection = new WebSocketClientConnection(wsContext.WebSocket, _config.MaxMessageSizeBytes, _logger);
                    RegisterNewConnection(connection);
                    connection.MessageReceived += OnMessageReceived;

                    _ = Task.Run(() => ProcessWebSocketConnection(connection, ct), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка приёма WebSocket подключения.");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private async Task AcceptTcpConnections(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _tcpListener!.AcceptTcpClientAsync(ct);
                    var connection = new TcpClientConnection(tcpClient, _config.MaxMessageSizeBytes, _logger);
                    RegisterNewConnection(connection);
                    connection.MessageReceived += OnMessageReceived;

                    _ = Task.Run(() => ProcessTcpConnection(connection, ct), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка приёма TCP подключения.");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private void RegisterNewConnection(IClientConnection connection)
        {
            _connections[connection.ConnectionId] = connection;
            _logger.LogDebug("Новое подключение {ConnectionId} ({Protocol})", connection.ConnectionId, connection.Protocol);
        }

        private async Task ProcessWebSocketConnection(WebSocketClientConnection connection, CancellationToken ct)
        {
            try
            {
                await connection.ReceiveLoop(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка WebSocket соединения {ConnectionId}", connection.ConnectionId);
            }
            finally
            {
                await DisconnectClient(connection, ct);
            }
        }

        private async Task ProcessTcpConnection(TcpClientConnection connection, CancellationToken ct)
        {
            try
            {
                await connection.ReceiveLoop(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка TCP соединения {ConnectionId}", connection.ConnectionId);
            }
            finally
            {
                await DisconnectClient(connection, ct);
            }
        }

        // ---------- Обработка входящих сообщений ----------

        private async Task OnMessageReceived(IClientConnection connection, byte[] rawData)
        {
            using var span = _tracer.StartSpan("GameServer.MessageReceived");
            string messageJson;
            try
            {
                messageJson = Encoding.UTF8.GetString(rawData);
            }
            catch (Exception ex)
            {
                await SendError(connection, "Некорректные данные", ex.Message);
                return;
            }

            NetworkMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<NetworkMessage>(messageJson)
                    ?? throw new InvalidOperationException("Пустое сообщение.");
            }
            catch (Exception ex)
            {
                await SendError(connection, "Некорректный формат сообщения", ex.Message);
                return;
            }

            _logger.LogDebug("Получено сообщение {Type} от {ConnectionId}", message.Type, connection.ConnectionId);

            switch (message.Type)
            {
                case "auth":
                    await HandleAuthentication(connection, message);
                    break;
                case "command":
                    await HandleIncomingCommand(connection, message);
                    break;
                case "query":
                    await HandleIncomingQuery(connection, message);
                    break;
                default:
                    await SendError(connection, "Неизвестный тип сообщения", message.Type);
                    break;
            }
        }

        private async Task HandleAuthentication(IClientConnection connection, NetworkMessage message)
        {
            var authRequest = JsonSerializer.Deserialize<AuthRequest>(message.Payload);
            if (authRequest == null || string.IsNullOrEmpty(authRequest.Token))
            {
                await SendError(connection, "Ошибка аутентификации", "Отсутствует токен");
                return;
            }

            var userContext = await _authProvider.GetUserContextFromTokenAsync(authRequest.Token);
            if (userContext == null)
            {
                await SendError(connection, "Ошибка аутентификации", "Неверный токен");
                return;
            }

            connection.UserId = userContext.UserId;
            connection.State = ConnectionState.Authenticated;

            // Проверяем лимит подключений на пользователя и регистрируем
            bool registered = TryRegisterUserConnection(userContext.UserId, connection.ConnectionId);
            if (!registered)
            {
                await SendError(connection, "Превышен лимит подключений",
                    $"Максимум подключений на пользователя: {_config.MaxConnectionsPerUser}");
                await connection.CloseAsync(CancellationToken.None);
                return;
            }

            var response = new NetworkMessage
            {
                Type = "auth_response",
                Payload = JsonSerializer.Serialize(new { Success = true, userContext.UserId }),
                CorrelationId = message.CorrelationId
            };
            await SendMessage(connection, response);
            _metricsCollector.IncrementCounter("dnd.connections.authenticated");
        }

        /// <summary>
        /// Пытается зарегистрировать связь пользователя и подключения.
        /// Возвращает false, если превышен лимит подключений.
        /// </summary>
        private bool TryRegisterUserConnection(Guid userId, Guid connectionId)
        {
            lock (_connectionLock)
            {
                if (_userConnections.TryGetValue(userId, out var list))
                {
                    if (list.Count >= _config.MaxConnectionsPerUser)
                        return false;
                    list.Add(connectionId);
                }
                else
                {
                    _userConnections[userId] = [connectionId];
                }
                return true;
            }
        }

        private async Task HandleIncomingCommand(IClientConnection connection, NetworkMessage message)
        {
            if (_config.RequireAuthentication && connection.State != ConnectionState.Authenticated)
            {
                await SendError(connection, "Требуется аутентификация", null);
                return;
            }

            var commandType = Type.GetType(message.PayloadType);
            if (commandType == null)
            {
                await SendError(connection, "Неизвестный тип команды", message.PayloadType);
                return;
            }

            ICommand command;
            try
            {
                command = JsonSerializer.Deserialize(message.Payload, commandType) as ICommand
                    ?? throw new InvalidOperationException("Не удалось десериализовать команду.");
            }
            catch (Exception ex)
            {
                await SendError(connection, "Некорректная полезная нагрузка команды", ex.Message);
                return;
            }

            var context = new CommandContext
            {
                UserId = connection.UserId ?? Guid.Empty,
                GameSessionId = connection.SessionId ?? Guid.Empty,
                CancellationToken = CancellationToken.None
            };

            try
            {
                await _commandBus.SendAsync(command, context);
                _metricsCollector.IncrementCounter("dnd.commands.network_received");
            }
            catch (Exception ex)
            {
                await SendError(connection, "Ошибка выполнения команды", ex.Message);
            }
        }

        private async Task HandleIncomingQuery(IClientConnection connection, NetworkMessage message)
        {
            if (_config.RequireAuthentication && connection.State != ConnectionState.Authenticated)
            {
                await SendError(connection, "Требуется аутентификация", null);
                return;
            }

            var queryType = Type.GetType(message.PayloadType);
            if (queryType == null)
            {
                await SendError(connection, "Неизвестный тип запроса", message.PayloadType);
                return;
            }

            object? queryObj;
            try
            {
                queryObj = JsonSerializer.Deserialize(message.Payload, queryType)
                    ?? throw new InvalidOperationException("Не удалось десериализовать запрос.");
            }
            catch (Exception ex)
            {
                await SendError(connection, "Некорректная полезная нагрузка запроса", ex.Message);
                return;
            }

            var queryInterface = queryType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (queryInterface == null)
            {
                await SendError(connection, "Некорректный запрос", $"Тип {queryType.Name} не реализует IQuery<TResult>.");
                return;
            }

            var resultType = queryInterface.GetGenericArguments()[0];
            var context = new QueryContext
            {
                UserId = connection.UserId ?? Guid.Empty,
                GameSessionId = connection.SessionId ?? Guid.Empty
            };

            try
            {
                var queryBusType = typeof(IQueryBus);
                var method = queryBusType.GetMethod("QueryAsync")!;
                var genericMethod = method.MakeGenericMethod(resultType);
                object?[] parameters = [queryObj, context, CancellationToken.None];
                var task = (Task)genericMethod.Invoke(_queryBus, parameters)!;
                await task.ConfigureAwait(false);

                var resultProperty = task.GetType().GetProperty("Result");
                var result = resultProperty?.GetValue(task);
                string? resultJson = result != null ? JsonSerializer.Serialize(result, resultType) : null;

                var response = new NetworkMessage
                {
                    Type = "query_response",
                    PayloadType = resultType.AssemblyQualifiedName!,
                    Payload = resultJson ?? "null",
                    CorrelationId = message.CorrelationId
                };
                await SendMessage(connection, response);
                _metricsCollector.IncrementCounter("dnd.queries.network_received");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения запроса {QueryType}", queryType.Name);
                await SendError(connection, "Ошибка выполнения запроса", ex.Message);
            }
        }

        // ---------- Рассылка событий ----------

        private async Task OnDomainEvent(IDomainEvent @event, CancellationToken cancellationToken)
        {
            var message = new NetworkMessage
            {
                Type = "event",
                PayloadType = @event.GetType().AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(@event, @event.GetType())
            };

            var affectedSessionIds = GetAffectedSessions(@event);
            var targetConnections = _connections.Values
                .Where(c => c.State == ConnectionState.Authenticated)
                .Where(c => affectedSessionIds == null ||
                            (c.SessionId.HasValue && affectedSessionIds.Contains(c.SessionId.Value)));

            foreach (var connection in targetConnections)
            {
                await SendMessage(connection, message);
            }
        }

        private static HashSet<Guid>? GetAffectedSessions(IDomainEvent @event)
        {
            if (@event is ISessionBoundEvent sessionEvent)
                return [sessionEvent.GameSessionId];
            return null; // широковещательная рассылка
        }

        // ---------- Вспомогательные методы ----------

        private async Task SendMessage(IClientConnection connection, NetworkMessage message)
        {
            var json = JsonSerializer.Serialize(message);
            var data = Encoding.UTF8.GetBytes(json);
            try
            {
                await connection.SendAsync(new ArraySegment<byte>(data), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить сообщение подключению {ConnectionId}", connection.ConnectionId);
            }
        }

        private async Task SendError(IClientConnection connection, string error, string? detail)
        {
            var message = new NetworkMessage
            {
                Type = "error",
                Payload = JsonSerializer.Serialize(new { Error = error, Detail = detail })
            };
            await SendMessage(connection, message);
        }

        private async Task DisconnectClient(IClientConnection connection, CancellationToken ct)
        {
            if (connection.State == ConnectionState.Disconnected)
                return;

            connection.State = ConnectionState.Disconnecting;
            _connections.TryRemove(connection.ConnectionId, out _);

            if (connection.UserId.HasValue)
            {
                lock (_connectionLock)
                {
                    if (_userConnections.TryGetValue(connection.UserId.Value, out var list))
                    {
                        list.Remove(connection.ConnectionId);
                        if (list.Count == 0)
                            _userConnections.TryRemove(connection.UserId.Value, out _);
                    }
                }
            }

            try
            {
                await connection.CloseAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Нормальная отмена
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка закрытия подключения {ConnectionId}", connection.ConnectionId);
            }
            finally
            {
                connection.State = ConnectionState.Disconnected;
                _metricsCollector.IncrementCounter("dnd.connections.disconnected");
            }
        }
    }

    // ---------- Реализации клиентских соединений ----------

    /// <summary>WebSocket-реализация IClientConnection.</summary>
    public class WebSocketClientConnection(WebSocket webSocket, int maxMessageSize, ILogger logger) : IClientConnection
    {
        private readonly WebSocket _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        private readonly int _maxMessageSize = maxMessageSize;
        private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Guid ConnectionId { get; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public Guid? SessionId { get; set; }
        public ConnectionState State { get; set; } = ConnectionState.Connecting;
        public TransportProtocol Protocol => TransportProtocol.WebSocket;

        public event Func<IClientConnection, byte[], Task>? MessageReceived;

        public async Task SendAsync(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            if (_webSocket.State == WebSocketState.Open)
                await _webSocket.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken);
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (_webSocket.State == WebSocketState.Open)
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Сервер закрывает соединение", cancellationToken);
            _webSocket.Dispose();
        }

        public async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            var buffer = new byte[_maxMessageSize];
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Клиент закрыл соединение", cancellationToken);
                        return;
                    }

                    // Принимаем как текстовые, так и бинарные сообщения
                    if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
                    {
                        ms.Write(buffer, 0, result.Count);
                        if (ms.Length > _maxMessageSize)
                        {
                            await _webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Сообщение слишком большое", cancellationToken);
                            return;
                        }
                    }
                } while (!result.EndOfMessage);

                if (ms.Length > 0)
                {
                    var data = ms.ToArray();
                    if (MessageReceived != null)
                        await MessageReceived.Invoke(this, data);
                }
            }
        }
    }

    /// <summary>TCP-реализация IClientConnection (с длиной сообщения в первых 4 байтах).</summary>
    public class TcpClientConnection(TcpClient tcpClient, int maxMessageSize, ILogger logger) : IClientConnection
    {
        private readonly TcpClient _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        private readonly NetworkStream _stream = tcpClient.GetStream();
        private readonly int _maxMessageSize = maxMessageSize;
        private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Guid ConnectionId { get; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public Guid? SessionId { get; set; }
        public ConnectionState State { get; set; } = ConnectionState.Connecting;
        public TransportProtocol Protocol => TransportProtocol.Tcp;

        public event Func<IClientConnection, byte[], Task>? MessageReceived;

        public async Task SendAsync(ArraySegment<byte> data, CancellationToken cancellationToken)
        {
            var lengthBytes = BitConverter.GetBytes(data.Count);
            await _stream.WriteAsync(lengthBytes, cancellationToken);
            await _stream.WriteAsync(data, cancellationToken);
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            _stream.Close();
            _tcpClient.Close();
            await Task.CompletedTask;
        }

        public async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            var lengthBuffer = new byte[4];
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(lengthBuffer.AsMemory(0, 4), cancellationToken);
                if (read < 4) break; // недостаточно данных для длины

                int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (messageLength <= 0 || messageLength > _maxMessageSize) break;

                var dataBuffer = new byte[messageLength];
                int bytesRead = 0;
                while (bytesRead < messageLength)
                {
                    int r = await _stream.ReadAsync(dataBuffer.AsMemory(bytesRead, messageLength - bytesRead), cancellationToken);
                    if (r == 0) break;
                    bytesRead += r;
                }
                if (bytesRead < messageLength) break;

                if (MessageReceived != null)
                    await MessageReceived.Invoke(this, dataBuffer);
            }
        }
    }

    /// <summary>Данные запроса аутентификации.</summary>
    public class AuthRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}