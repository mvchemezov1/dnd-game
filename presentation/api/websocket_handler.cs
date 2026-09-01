#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
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
using dnd_game.infrastructure.network;
using dnd_game.infrastructure.security;
using dnd_game.infrastructure.undo;

namespace dnd_game.presentation.api
{
    /// <summary>Состояние одного WebSocket-подключения.</summary>
    public sealed class WebSocketConnectionState
    {
        public Guid ConnectionId { get; } = Guid.NewGuid();
        public WebSocket Socket { get; set; } = null!;
        public Guid? UserId { get; set; }
        public Guid? SessionId { get; set; }
        public bool IsAuthenticated => UserId.HasValue;
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
        public bool IsClosed { get; set; }
    }

    /// <summary>
    /// Обработчик WebSocket-соединений, обслуживающий игровые взаимодействия DnD.
    /// Поддерживает аутентификацию, приём команд/запросов, рассылку событий и Undo/Redo.
    /// </summary>
    public sealed class WebSocketHandler(
        ICommandBus commandBus,
        IQueryBus queryBus,
        IEventBus eventBus,
        ISessionManager sessionManager,
        IAuthProvider authProvider,
        INetworkProtocol protocol,
        PermissionChecker permissionChecker,
        IMetricsCollector metricsCollector,
        ITracer tracer,
        IRateLimiter rateLimiter,
        ILogger<WebSocketHandler> logger,
        UndoManager undoManager,
        ICharacterOwnershipRepository ownershipRepository)
    {
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly IQueryBus _queryBus = queryBus ?? throw new ArgumentNullException(nameof(queryBus));
        private readonly IEventBus _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        private readonly ISessionManager _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        private readonly IAuthProvider _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
        private readonly INetworkProtocol _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        private readonly IMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        private readonly ITracer _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        private readonly IRateLimiter _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        private readonly ILogger<WebSocketHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly UndoManager _undoManager = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
        private readonly ICharacterOwnershipRepository _ownershipRepository = ownershipRepository ?? throw new ArgumentNullException(nameof(ownershipRepository));

        private const int MaxMessageSize = 64 * 1024; // 64 КБ
        private const int ReceiveBufferSize = 4096;
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

        private readonly ConcurrentDictionary<Guid, WebSocketConnectionState> _connections = new();
        private readonly ConcurrentDictionary<Guid, List<Action>> _eventSubscriptions = new();

        /// <summary>
        /// Преобразует IP-адрес в детерминированный Guid для использования в rate limiter.
        /// </summary>
        private static Guid IpToClientId(IPAddress? ip)
        {
            if (ip == null) return Guid.Empty;
            var hash = MD5.HashData(ip.GetAddressBytes());
            return new Guid(hash);
        }

        private IReadOnlyList<INetworkMessage> DecodeMessages(byte[] data)
        {
            return _protocol.Decode(data);
        }

        /// <summary>
        /// Основной метод обработки WebSocket-соединения.
        /// </summary>
        public async Task HandleAsync(
            WebSocket socket,
            HttpContext httpContext,
            CancellationToken cancellationToken,
            IPAddress? remoteIp = null)
        {
            var state = new WebSocketConnectionState { Socket = socket, ConnectedAt = DateTime.UtcNow };

            // Ограничение частоты подключений с одного IP
            if (!_rateLimiter.IsAllowed(IpToClientId(remoteIp), "websocket-connect"))
            {
                _logger.LogWarning("WebSocket-подключение отклонено из-за лимита для IP {RemoteIp}", remoteIp);
                await CloseConnection(state, WebSocketCloseStatus.PolicyViolation, "Слишком много попыток подключения");
                return;
            }

            _connections[state.ConnectionId] = state;
            _logger.LogInformation("WebSocket-подключение {ConnectionId} открыто", state.ConnectionId);

            try
            {
                // Аутентификация через токен в query-строке
                var token = httpContext.Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(token))
                {
                    await SendErrorMessage(state, "AUTH_REQUIRED", "Требуется аутентификация. Укажите token в query-строке.");
                    await CloseConnection(state, WebSocketCloseStatus.PolicyViolation, "Аутентификация не выполнена");
                    return;
                }

                var userContext = await _authProvider.GetUserContextFromTokenAsync(token, cancellationToken);
                if (userContext == null)
                {
                    await SendErrorMessage(state, "AUTH_FAILED", "Недействительный или истёкший токен.");
                    await CloseConnection(state, WebSocketCloseStatus.PolicyViolation, "Аутентификация не выполнена");
                    return;
                }

                state.UserId = userContext.UserId;
                _logger.LogInformation("WebSocket-подключение {ConnectionId} аутентифицировано как пользователь {UserId}",
                    state.ConnectionId, state.UserId);

                // Привязка к сессии, если указана
                var sessionIdQuery = httpContext.Request.Query["sessionId"].FirstOrDefault();
                if (Guid.TryParse(sessionIdQuery, out var sessionId))
                {
                    // Проверяем, состоит ли пользователь в указанной кампании/сессии
                    if (!await _permissionChecker.IsMemberOfCampaignAsync(sessionId, cancellationToken))
                    {
                        await SendErrorMessage(state, "FORBIDDEN", "Вы не состоите в указанной кампании.");
                        await CloseConnection(state, WebSocketCloseStatus.PolicyViolation, "Недостаточно прав для подключения к сессии");
                        return;
                    }

                    state.SessionId = sessionId;
                    await _sessionManager.AssociateConnection(state.UserId.Value, sessionId, state.ConnectionId, cancellationToken);
                }

                // Подписка на события
                SubscribeToEvents(state, cancellationToken);

                // Keep-alive
                using var keepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var keepAliveTask = Task.Run(() => KeepAliveLoopAsync(state, keepAliveCts.Token), CancellationToken.None);

                // Основной цикл приёма
                await ReceiveLoopAsync(state, cancellationToken);

                keepAliveCts.Cancel();
                try { await keepAliveTask; } catch { /* игнорируем */ }
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "Ошибка WebSocket для подключения {ConnectionId}", state.ConnectionId);
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка WebSocket-подключения {ConnectionId}", state.ConnectionId);
            }
            finally
            {
                await CloseConnection(state, WebSocketCloseStatus.NormalClosure, "Сервер закрывает соединение");
            }
        }

        private async Task SendErrorMessage(WebSocketConnectionState state, string errorCode, string message)
        {
            var errorMsg = new ErrorNetworkMessage
            {
                ErrorCode = errorCode,
                Message = message
            };
            await SendMessageAsync(state, errorMsg);
        }

        private async Task ReceiveLoopAsync(WebSocketConnectionState state, CancellationToken cancellationToken)
        {
            while (state.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                byte[] rawMessage;
                try
                {
                    (result, rawMessage) = await ReceiveFullMessageAsync(state.Socket, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                state.LastMessageAt = DateTime.UtcNow;

                var rateLimitClientId = state.UserId ?? state.ConnectionId;
                if (!_rateLimiter.IsAllowed(rateLimitClientId, "websocket-message"))
                {
                    await SendMessageAsync(state, NetworkMessageFactory.CreateError("RATE_LIMITED", "Слишком много сообщений, снизьте темп."));
                    continue;
                }

                // Декодируем все сообщения из кадра
                var networkMessages = DecodeMessages(rawMessage);
                foreach (var networkMsg in networkMessages)
                {
                    using var span = _tracer.StartSpan("WebSocket.Message");
                    _tracer.SetTag("connection.id", state.ConnectionId.ToString());
                    _tracer.SetTag("message.type", networkMsg.Type.ToString());

                    try
                    {
                        await DispatchMessageAsync(state, networkMsg, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка обработки сообщения типа {MessageType}", networkMsg.Type);
                        await SendMessageAsync(state, NetworkMessageFactory.CreateError("PROCESSING_ERROR", ex.Message));
                    }
                }
            }
        }

        private static async Task<(WebSocketReceiveResult result, byte[] data)> ReceiveFullMessageAsync(
            WebSocket socket,
            CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[ReceiveBufferSize];
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                    return (result, Array.Empty<byte>());

                ms.Write(buffer, 0, result.Count);

                if (ms.Length > MaxMessageSize)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Сообщение слишком большое",
                        cancellationToken);
                    throw new InvalidOperationException("WebSocket-сообщение превысило максимально допустимый размер.");
                }

            } while (!result.EndOfMessage);

            return (result, ms.ToArray());
        }

        private async Task SendMessageAsync(WebSocketConnectionState state, INetworkMessage message)
        {
            if (state.Socket.State != WebSocketState.Open) return;
            var bytes = _protocol.Encode(message);
            var segment = new ArraySegment<byte>(bytes);
            try
            {
                await state.Socket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить сообщение подключению {ConnectionId}", state.ConnectionId);
            }
        }

        private static T? DeserializePayload<T>(INetworkMessage message) where T : class
        {
            return message switch
            {
                CommandNetworkMessage cmdMsg => JsonSerializer.Deserialize<T>(cmdMsg.CommandJson),
                EventNetworkMessage eventMsg => JsonSerializer.Deserialize<T>(eventMsg.EventJson),
                AuthRequestMessage authReq => authReq as T,
                _ => null
            };
        }

        private async Task KeepAliveLoopAsync(WebSocketConnectionState state, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && state.Socket.State == WebSocketState.Open)
            {
                await Task.Delay(KeepAliveInterval, cancellationToken);
                if (state.Socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await SendMessageAsync(state, new PingMessage());
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        private async Task CloseConnection(WebSocketConnectionState state, WebSocketCloseStatus status, string description)
        {
            if (state.IsClosed)
                return;
            state.IsClosed = true;

            if (state.Socket.State == WebSocketState.Open)
            {
                try
                {
                    await state.Socket.CloseAsync(status, description, CancellationToken.None);
                }
                catch { /* игнорируем ошибки закрытия */ }
            }

            if (_eventSubscriptions.TryRemove(state.ConnectionId, out var unsubList))
            {
                foreach (var unsub in unsubList)
                    unsub();
            }

            _sessionManager.RemoveConnection(state.ConnectionId);
            _connections.TryRemove(state.ConnectionId, out _);
            _metricsCollector.IncrementCounter("dnd.websocket.disconnected");
            _logger.LogInformation("WebSocket-подключение {ConnectionId} закрыто ({Status})", state.ConnectionId, status);
        }

        private async Task DispatchMessageAsync(WebSocketConnectionState state, INetworkMessage networkMsg, CancellationToken cancellationToken)
        {
            switch (networkMsg)
            {
                case CommandNetworkMessage cmd:
                    await HandleCommand(state, cmd);
                    break;

                case QueryNetworkMessage query:
                    await HandleQuery(state, query, cancellationToken);
                    break;

                case PingMessage:
                    await SendMessageAsync(state, new PongMessage());
                    break;

                case UndoNetworkMessage undo:
                    await HandleUndo(state, undo);
                    break;

                case RedoNetworkMessage redo:
                    await HandleRedo(state, redo);
                    break;
                case AuthRequestMessage authReq:
                    // Аутентификация уже выполнена в HandleAsync по токену из query-строки.
                    // Здесь можно просто отправить подтверждение (AuthResponse).
                    await SendMessageAsync(state, new AuthResponseMessage
                    {
                        Success = true,
                        UserId = state.UserId,
                        CorrelationId = authReq.CorrelationId
                    });
                    break;

                default:
                    await SendMessageAsync(state, new ErrorNetworkMessage
                    {
                        ErrorCode = "UNKNOWN_TYPE",
                        Message = $"Неподдерживаемый тип сообщения: {networkMsg.Type}"
                    });
                    break;
            }
        }

        private async Task HandleUndo(WebSocketConnectionState state, UndoNetworkMessage msg)
        {
            if (!state.UserId.HasValue || !state.SessionId.HasValue)
            {
                await SendMessageAsync(state, new UndoResponseNetworkMessage
                {
                    Success = false,
                    ErrorMessage = "Не аутентифицирован или нет активной сессии.",
                    CorrelationId = msg.CorrelationId
                });
                return;
            }

            try
            {
                var success = await _undoManager.UndoAsync(state.SessionId.Value, state.UserId.Value);
                await SendMessageAsync(state, new UndoResponseNetworkMessage
                {
                    Success = success,
                    CorrelationId = msg.CorrelationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отмены действия в сессии {SessionId}", state.SessionId);
                await SendMessageAsync(state, new UndoResponseNetworkMessage
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    CorrelationId = msg.CorrelationId
                });
            }
        }

        private async Task HandleRedo(WebSocketConnectionState state, RedoNetworkMessage msg)
        {
            if (!state.UserId.HasValue || !state.SessionId.HasValue)
            {
                await SendMessageAsync(state, new RedoResponseNetworkMessage
                {
                    Success = false,
                    ErrorMessage = "Не аутентифицирован или нет активной сессии.",
                    CorrelationId = msg.CorrelationId
                });
                return;
            }

            try
            {
                var success = await _undoManager.RedoAsync(state.SessionId.Value, state.UserId.Value);
                await SendMessageAsync(state, new RedoResponseNetworkMessage
                {
                    Success = success,
                    CorrelationId = msg.CorrelationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка повтора действия в сессии {SessionId}", state.SessionId);
                await SendMessageAsync(state, new RedoResponseNetworkMessage
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    CorrelationId = msg.CorrelationId
                });
            }
        }

        private async Task HandleCommand(WebSocketConnectionState state, CommandNetworkMessage msg)
        {
            var commandType = Type.GetType(msg.CommandTypeName);
            if (commandType == null)
            {
                await SendMessageAsync(state, NetworkMessageFactory.CreateError("UNKNOWN_COMMAND", "Тип команды не найден."));
                return;
            }

            if (JsonSerializer.Deserialize(msg.CommandJson, commandType) is not ICommand commandObj)
            {
                await SendMessageAsync(state, NetworkMessageFactory.CreateError("DESERIALIZE_ERROR", "Некорректная полезная нагрузка команды."));
                return;
            }

            var context = new CommandContext
            {
                UserId = state.UserId ?? Guid.Empty,
                GameSessionId = state.SessionId ?? Guid.Empty,
                CancellationToken = CancellationToken.None
            };

            try
            {
                await _commandBus.SendAsync(commandObj, context);
                await SendMessageAsync(state, new CommandResponseNetworkMessage
                {
                    Success = true,
                    CorrelationId = msg.CorrelationId
                });
            }
            catch (Exception ex)
            {
                await SendMessageAsync(state, NetworkMessageFactory.CreateError("COMMAND_FAILED", ex.Message, correlationId: msg.CorrelationId));
            }
        }

        private async Task HandleQuery(WebSocketConnectionState state, QueryNetworkMessage msg, CancellationToken cancellationToken)
        {
            try
            {
                var queryType = Type.GetType(msg.QueryTypeName);
                if (queryType == null)
                {
                    await SendQueryError(state, msg.CorrelationId, $"Неизвестный тип запроса: {msg.QueryTypeName}");
                    return;
                }

                var queryObj = JsonSerializer.Deserialize(msg.QueryJson, queryType);
                if (queryObj == null)
                {
                    await SendQueryError(state, msg.CorrelationId, "Не удалось десериализовать запрос.");
                    return;
                }

                var queryInterface = queryType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(domain.queries.IQuery<>));
                if (queryInterface == null)
                {
                    await SendQueryError(state, msg.CorrelationId, $"Тип {queryType.Name} не реализует IQuery<TResult>.");
                    return;
                }

                var resultType = queryInterface.GetGenericArguments()[0];
                var method = typeof(IQueryBus).GetMethod("QueryAsync");
                if (method == null)
                {
                    await SendQueryError(state, msg.CorrelationId, "Метод QueryAsync не найден.");
                    return;
                }

                var genericMethod = method.MakeGenericMethod(resultType);
                var context = new QueryContext
                {
                    UserId = state.UserId ?? Guid.Empty,
                    GameSessionId = state.SessionId ?? Guid.Empty
                };
                object?[] parameters = [queryObj, context, cancellationToken];

                var task = (Task)genericMethod.Invoke(_queryBus, parameters)!;
                await task.ConfigureAwait(false);

                var resultProperty = task.GetType().GetProperty("Result");
                var result = resultProperty?.GetValue(task);
                string? resultJson = result != null ? JsonSerializer.Serialize(result, resultType) : null;

                await SendMessageAsync(state, new QueryResponseNetworkMessage
                {
                    Success = true,
                    ResultJson = resultJson,
                    CorrelationId = msg.CorrelationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки запроса {QueryTypeName}", msg.QueryTypeName);
                await SendQueryError(state, msg.CorrelationId, $"Внутренняя ошибка: {ex.Message}");
            }
        }

        private async Task SendQueryError(WebSocketConnectionState state, string? correlationId, string message)
        {
            await SendMessageAsync(state, new QueryResponseNetworkMessage
            {
                Success = false,
                ErrorMessage = message,
                CorrelationId = correlationId
            });
        }

        private void SubscribeToEvents(WebSocketConnectionState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.SessionId.HasValue) return;

            async Task EventHandler(IDomainEvent @event, CancellationToken ct)
            {
                if (state.Socket.State != WebSocketState.Open) return;
                if (!await ShouldSendEventToSessionAsync(@event, state.SessionId.Value, state.UserId, ct))
                    return;

                var eventMsg = NetworkMessageFactory.FromEvent(@event);
                await SendMessageAsync(state, eventMsg);
            }

            _eventBus.Subscribe<IDomainEvent>(EventHandler);

            _eventSubscriptions.AddOrUpdate(
                state.ConnectionId,
                _ => [() => _eventBus.Unsubscribe<IDomainEvent>(EventHandler)],
                (_, list) =>
                {
                    list.Add(() => _eventBus.Unsubscribe<IDomainEvent>(EventHandler));
                    return list;
                });
        }

        private async Task<bool> ShouldSendEventToSessionAsync(
            IDomainEvent @event,
            Guid sessionId,
            Guid? userId,
            CancellationToken cancellationToken)
        {
            // События, явно привязанные к игровой сессии
            if (@event is ISessionBoundEvent sessionEvent)
                return sessionEvent.GameSessionId == sessionId;

            // События кампании
            if (@event is ICampaignEvent campaignEvent)
                return campaignEvent.CampaignId == sessionId;

            // События персонажа
            if (@event is ICharacterEvent characterEvent)
            {
                if (!userId.HasValue)
                    return false;

                // Владелец персонажа всегда получает события
                var ownerId = await _ownershipRepository.GetOwnerIdAsync(characterEvent.CharacterId, cancellationToken);
                if (ownerId == userId.Value)
                    return true;

                // Для NPC проверяем, что персонаж относится к той же кампании, что и пользователь
                if (await _ownershipRepository.IsNonPlayerCharacterAsync(characterEvent.CharacterId, cancellationToken))
                {
                    var npcCampaignId = await _ownershipRepository.GetCampaignIdAsync(characterEvent.CharacterId, cancellationToken);
                    return npcCampaignId == sessionId;
                }

                return false;
            }

            // Глобальные события не рассылаем
            return false;
        }
    }
}