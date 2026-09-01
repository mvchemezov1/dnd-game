#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.commands;
using dnd_game.domain.queries;
using dnd_game.infrastructure.network;

namespace dnd_game.presentation.client
{
    /// <summary>Состояние подключения клиента.</summary>
    public enum ClientConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Authenticating,
        Authenticated,
        Reconnecting,
        Disconnecting
    }

    /// <summary>Конфигурация клиентского подключения.</summary>
    public sealed class ClientNetworkConfig
    {
        public string ServerUrl { get; set; } = "ws://localhost:5000/ws";
        public int ReconnectDelayMs { get; set; } = 2000;
        public int MaxReconnectAttempts { get; set; } = 5;
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>Делегат для получения сообщений от сервера.</summary>
    public delegate Task MessageReceivedHandler(INetworkMessage message);

    /// <summary>Интерфейс игрового клиента, абстрагирующий сетевое взаимодействие.</summary>
    public interface IGameClient
    {
        ClientConnectionState State { get; }
        Task ConnectAsync(string? authToken = null, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);
        Task SendCommandAsync(ICommand command, string? correlationId = null, CancellationToken cancellationToken = default);
        Task SendQueryAsync<TResult>(IQuery<TResult> query, string? correlationId = null, CancellationToken cancellationToken = default);
        Task SendMessageAsync(INetworkMessage message, CancellationToken cancellationToken = default);
        void RegisterMessageHandler(MessageReceivedHandler handler);
        void UnregisterMessageHandler(MessageReceivedHandler handler);
    }

    /// <summary>
    /// Клиентская сетевая библиотека на основе WebSocket.
    /// Обеспечивает подключение, аутентификацию, отправку команд/запросов и приём сообщений.
    /// </summary>
    public sealed class ClientNetwork : IGameClient, IDisposable
    {
        private readonly ClientNetworkConfig _config;
        private readonly ILogger<ClientNetwork>? _logger;
        private readonly INetworkProtocol _protocol;
        private readonly List<MessageReceivedHandler> _handlers = [];
        private readonly object _handlersLock = new();
        private readonly object _stateLock = new();

        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _connectionCts;
        private Task? _receiveTask;
        private TaskCompletionSource<bool>? _authTcs;
        private string? _authToken;
        private int _reconnectAttempts;
        private bool _disposed;
        private bool _isManuallyDisconnecting;
        private ClientConnectionState _state = ClientConnectionState.Disconnected;

        public ClientConnectionState State
        {
            get { lock (_stateLock) return _state; }
            private set { lock (_stateLock) _state = value; }
        }

        public Guid? UserId { get; private set; }

        public ClientNetwork(
            ClientNetworkConfig config,
            INetworkProtocol? protocol = null,
            ILogger<ClientNetwork>? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(_config.ServerUrl))
                throw new ArgumentException("ServerUrl не может быть пустым.", nameof(config));
            _protocol = protocol ?? new JsonNetworkProtocol();
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ConnectAsync(string? authToken = null, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                ObjectDisposedException.ThrowIf(_disposed, this);
            if (State == ClientConnectionState.Connected || State == ClientConnectionState.Connecting)
                throw new InvalidOperationException("Уже подключён или выполняется подключение.");

            _authToken = authToken;
            _isManuallyDisconnecting = false;
            State = ClientConnectionState.Connecting;
            _reconnectAttempts = 0;

            await EstablishConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task EstablishConnectionAsync(CancellationToken externalCancellation = default)
        {
            // Останавливаем предыдущий цикл приёма, если он есть
            await StopReceiveTaskAsync().ConfigureAwait(false);

            // Закрываем предыдущее соединение
            await CloseWebSocketAsync().ConfigureAwait(false);

            // Создаём новый токен отмены для этого соединения
            _connectionCts?.Dispose();
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);

            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_config.ServerUrl), _connectionCts.Token).ConfigureAwait(false);

                State = ClientConnectionState.Connected;
                _logger?.LogInformation("WebSocket подключён к {ServerUrl}", _config.ServerUrl);

                // Запускаем цикл приёма
                _receiveTask = Task.Run(() => ReceiveLoop(_connectionCts.Token), CancellationToken.None);

                if (!string.IsNullOrEmpty(_authToken))
                {
                    State = ClientConnectionState.Authenticating;
                    _authTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    var authRequest = new AuthRequestMessage { Token = _authToken };
                    await SendMessageAsync(authRequest, _connectionCts.Token).ConfigureAwait(false);

                    // Ожидаем ответ или таймаут
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);
                    timeoutCts.CancelAfter(_config.AuthTimeout);

                    bool authSuccess;
                    try
                    {
                        authSuccess = await _authTcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        _logger?.LogWarning("Таймаут аутентификации.");
                        authSuccess = false;
                    }

                    _authTcs = null;

                    if (!authSuccess)
                    {
                        throw new UnauthorizedAccessException("Аутентификация не удалась.");
                    }
                }

                State = ClientConnectionState.Authenticated;
            }
            catch (OperationCanceledException) when (externalCancellation.IsCancellationRequested)
            {
                State = ClientConnectionState.Disconnected;
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Не удалось подключиться к серверу.");
                State = ClientConnectionState.Disconnected;
                _authTcs?.TrySetResult(false);
                _authTcs = null;
                await TryReconnectAsync(externalCancellation).ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var (messageType, data) = await ReceiveFullMessageAsync(_webSocket, buffer, cancellationToken).ConfigureAwait(false);
                    if (messageType == WebSocketMessageType.Close)
                    {
                        _logger?.LogInformation("Сервер закрыл соединение.");
                        break;
                    }

                    if (data.Length == 0)
                        continue;

                    var messages = _protocol.Decode(data);
                    foreach (var msg in messages)
                    {
                        await DispatchMessageAsync(msg).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    _logger?.LogWarning(ex, "Ошибка WebSocket в цикле приёма.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Непредвиденная ошибка в цикле приёма.");
                }
            }

            // Соединение потеряно, если не было ручного отключения
            if (!_isManuallyDisconnecting && !_disposed)
            {
                State = ClientConnectionState.Disconnected;
                await TryReconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task<(WebSocketMessageType MessageType, byte[] Data)> ReceiveFullMessageAsync(
            ClientWebSocket socket,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return (result.MessageType, Array.Empty<byte>());
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            return (result.MessageType, ms.ToArray());
        }

        private async Task DispatchMessageAsync(INetworkMessage message)
        {
            switch (message)
            {
                case AuthResponseMessage authResponse:
                    UserId = authResponse.Success ? authResponse.UserId : null;
                    State = authResponse.Success ? ClientConnectionState.Authenticated : ClientConnectionState.Connected;
                    _authTcs?.TrySetResult(authResponse.Success);
                    break;

                case PingMessage:
                    // Отвечаем понгом
                    await SendMessageAsync(new PongMessage(), CancellationToken.None).ConfigureAwait(false);
                    break;

                case CommandResponseNetworkMessage cmdResponse:
                    _logger?.LogDebug("Ответ команды: {Success}", cmdResponse.Success);
                    break;

                case EventNetworkMessage eventMsg:
                    _logger?.LogDebug("Получено событие от сервера: {EventType}", eventMsg.EventTypeName);
                    break;
            }

            // Вызываем все зарегистрированные обработчики
            List<MessageReceivedHandler> handlersSnapshot;
            lock (_handlersLock)
            {
                handlersSnapshot = [.. _handlers];
            }

            foreach (var handler in handlersSnapshot)
            {
                try
                {
                    await handler(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Ошибка в обработчике сообщения типа {MessageType}", message.Type);
                }
            }
        }

        private async Task TryReconnectAsync(CancellationToken cancellationToken)
        {
            if (_isManuallyDisconnecting || _disposed)
                return;

            if (_reconnectAttempts >= _config.MaxReconnectAttempts)
            {
                _logger?.LogWarning("Достигнут максимум попыток переподключения.");
                return;
            }

            _reconnectAttempts++;
            State = ClientConnectionState.Reconnecting;
            int delay = _config.ReconnectDelayMs * (int)Math.Pow(2, _reconnectAttempts - 1);
            _logger?.LogInformation("Переподключение через {Delay} мс (попытка {Attempt})", delay, _reconnectAttempts);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                await EstablishConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Отменено — ничего не делаем
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            _isManuallyDisconnecting = true;
            State = ClientConnectionState.Disconnecting;

            await StopReceiveTaskAsync().ConfigureAwait(false);
            _authTcs?.TrySetResult(false);
            _authTcs = null;

            await CloseWebSocketAsync().ConfigureAwait(false);

            State = ClientConnectionState.Disconnected;
            _logger?.LogInformation("Отключено от сервера.");
        }

        /// <inheritdoc />
        public Task SendCommandAsync(ICommand command, string? correlationId = null, CancellationToken cancellationToken = default)
        {
            var msg = NetworkMessageFactory.FromCommand(command, UserId ?? Guid.Empty, Guid.Empty, correlationId);
            return SendMessageAsync(msg, cancellationToken);
        }

        /// <inheritdoc />
        public Task SendQueryAsync<TResult>(IQuery<TResult> query, string? correlationId = null, CancellationToken cancellationToken = default)
        {
            var msg = new QueryNetworkMessage
            {
                QueryTypeName = query.GetType().AssemblyQualifiedName!,
                QueryJson = JsonSerializer.Serialize(query, query.GetType()),
                CorrelationId = correlationId
            };
            return SendMessageAsync(msg, cancellationToken);
        }

        /// <inheritdoc />
        public async Task SendMessageAsync(INetworkMessage message, CancellationToken cancellationToken = default)
        {
            if (_webSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("Нет активного подключения.");

            var bytes = _protocol.Encode(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Binary,
                true,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void RegisterMessageHandler(MessageReceivedHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_handlersLock)
            {
                _handlers.Add(handler);
            }
        }

        /// <inheritdoc />
        public void UnregisterMessageHandler(MessageReceivedHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_handlersLock)
            {
                _handlers.Remove(handler);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _isManuallyDisconnecting = true;
            _authTcs?.TrySetResult(false);
            _authTcs = null;
            _connectionCts?.Cancel();
            _connectionCts?.Dispose();
            _webSocket?.Dispose();
            _receiveTask = null;
        }

        private async Task CloseWebSocketAsync()
        {
            if (_webSocket == null)
                return;

            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Закрытие клиентом",
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Ошибка при закрытии WebSocket.");
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        private async Task StopReceiveTaskAsync()
        {
            if (_receiveTask == null || _receiveTask.IsCompleted)
                return;

            _connectionCts?.Cancel();

            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ожидаемо
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Ошибка при остановке цикла приёма.");
            }
            finally
            {
                _receiveTask = null;
            }
        }
    }
}