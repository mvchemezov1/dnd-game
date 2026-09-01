#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using dnd_game.application.event_handlers;
using dnd_game.domain.commands;
using dnd_game.domain.events;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Реализация шины команд и событий на базе RabbitMQ.
    /// Обеспечивает надёжную доставку с подтверждениями, обработку ошибок и повторные попытки.
    /// </summary>
    public class RabbitMqBus(
        string connectionString,
        IServiceProvider serviceProvider,
        ILogger<RabbitMqBus> logger) : ICommandBus, IEventBus, IAsyncDisposable
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        private readonly ILogger<RabbitMqBus> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private IConnection? _connection;
        private IChannel? _channel;

        private const string CommandExchange = "dnd.commands";
        private const string EventExchange = "dnd.events";
        private const string DeadLetterExchange = "dnd.dead_letter";
        private const string CommandQueue = "dnd.commands.queue";

        /// <summary>
        /// Информация о подписке на определённый тип события.
        /// </summary>
        private sealed class EventTypeSubscription
        {
            public string QueueName { get; init; } = string.Empty;
            public string ConsumerTag { get; init; } = string.Empty;
            public List<Func<IDomainEvent, CancellationToken, Task>> Handlers { get; } = [];
            public bool IsBroadSubscription { get; init; }
        }

        private readonly ConcurrentDictionary<Type, EventTypeSubscription> _eventSubscriptions = new();
        private readonly ConcurrentDictionary<Type, List<Func<ICommand, CommandContext?, Task>>> _commandHandlers = new();

        /// <summary>
        /// Асинхронно инициализирует подключение, каналы и начинает потребление команд.
        /// Должен вызываться один раз при старте приложения.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            await InitializeExchangesAsync(cancellationToken).ConfigureAwait(false);
            await StartCommandConsumptionAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("RabbitMQ шина инициализирована.");
        }

        private async Task InitializeExchangesAsync(CancellationToken ct)
        {
            if (_channel is null)
                throw new InvalidOperationException("Канал не инициализирован.");

            await _channel.ExchangeDeclareAsync(CommandExchange, ExchangeType.Direct, durable: true, cancellationToken: ct).ConfigureAwait(false);
            await _channel.ExchangeDeclareAsync(EventExchange, ExchangeType.Topic, durable: true, cancellationToken: ct).ConfigureAwait(false);
            await _channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: ct).ConfigureAwait(false);

            var deadLetterQueue = "dnd.dead_letter_queue";
            await _channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);
            await _channel.QueueBindAsync(deadLetterQueue, DeadLetterExchange, "#", cancellationToken: ct).ConfigureAwait(false);
        }

        private async Task StartCommandConsumptionAsync(CancellationToken ct)
        {
            if (_channel is null)
                throw new InvalidOperationException("Канал не инициализирован.");

            await _channel.QueueDeclareAsync(
                queue: CommandQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", DeadLetterExchange },
                    { "x-dead-letter-routing-key", "command" }
                },
                cancellationToken: ct).ConfigureAwait(false);

            await _channel.QueueBindAsync(CommandQueue, CommandExchange, "#", cancellationToken: ct).ConfigureAwait(false);

            var commandConsumer = new AsyncEventingBasicConsumer(_channel);
            commandConsumer.ReceivedAsync += ProcessCommandMessageAsync;
            await _channel.BasicConsumeAsync(CommandQueue, autoAck: false, commandConsumer, cancellationToken: ct).ConfigureAwait(false);
        }

        // ==================== Команды ====================

        async Task ICommandBus.SendAsync(ICommand command, CommandContext? context)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (_channel is null)
                throw new InvalidOperationException("Шина RabbitMQ не инициализирована.");

            context ??= new CommandContext();
            var body = SerializeCommand(command, context);
            var routingKey = command.GetType().Name;
            var properties = CreateBasicProperties(command, context);

            await _channel.BasicPublishAsync(
                exchange: CommandExchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: context.CancellationToken).ConfigureAwait(false);
        }

        void ICommandBus.Subscribe<TCommand>(Func<TCommand, CommandContext?, Task> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            var commandType = typeof(TCommand);

            _commandHandlers.AddOrUpdate(
                commandType,
                _ => [(cmd, ctx) => handler((TCommand)cmd, ctx)],
                (_, list) =>
                {
                    list.Add((cmd, ctx) => handler((TCommand)cmd, ctx));
                    return list;
                });
        }

        private async Task ProcessCommandMessageAsync(object sender, BasicDeliverEventArgs args)
        {
            CommandEnvelope? envelope = null;
            try
            {
                var json = Encoding.UTF8.GetString(args.Body.Span);
                envelope = JsonSerializer.Deserialize<CommandEnvelope>(json);
                if (envelope == null || string.IsNullOrEmpty(envelope.CommandType))
                {
                    await _channel!.BasicNackAsync(args.DeliveryTag, false, false).ConfigureAwait(false);
                    return;
                }

                Type? commandType = Type.GetType(envelope.CommandType);
                if (commandType == null)
                {
                    _logger.LogWarning("Неизвестный тип команды: {CommandType}", envelope.CommandType);
                    await _channel!.BasicNackAsync(args.DeliveryTag, false, false).ConfigureAwait(false);
                    return;
                }

                var commandObj = JsonSerializer.Deserialize(envelope.CommandData, commandType);
                if (commandObj is not ICommand command)
                {
                    await _channel!.BasicNackAsync(args.DeliveryTag, false, false).ConfigureAwait(false);
                    return;
                }

                var context = new CommandContext
                {
                    UserId = envelope.UserId,
                    GameSessionId = envelope.SessionId,
                    CancellationToken = CancellationToken.None
                };

                // Получаем все поведения конвейера
                var behaviors = _serviceProvider.GetServices<ICommandPipelineBehavior>().ToArray();

                // Финальный делегат — реальная диспетчеризация
                Func<Task> handlerAction = async () =>
                {
                    // Обработчики через делегаты
                    if (_commandHandlers.TryGetValue(commandType, out var handlers))
                    {
                        foreach (var handler in handlers)
                        {
                            await handler(command, context).ConfigureAwait(false);
                        }
                        return;
                    }

                    // Обработчик через DI
                    var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);
                    var diHandler = _serviceProvider.GetService(handlerType);
                    if (diHandler != null)
                    {
                        var method = handlerType.GetMethod("Handle", [commandType, typeof(CancellationToken)]);
                        if (method != null)
                        {
                            await ((Task)method.Invoke(diHandler, [command, context.CancellationToken])!).ConfigureAwait(false);
                            return;
                        }
                    }

                    throw new InvalidOperationException($"Нет обработчика для команды {commandType.Name}");
                };

                // Оборачиваем цепочку поведений
                foreach (var behavior in behaviors.Reverse())
                {
                    var next = handlerAction;
                    handlerAction = () => behavior.HandleAsync(command, context, next);
                }

                // Выполняем с учётом конвейера
                await handlerAction().ConfigureAwait(false);

                // Подтверждаем успешную обработку
                await _channel!.BasicAckAsync(args.DeliveryTag, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки команды {CommandType}", envelope?.CommandType);
                await HandleProcessingFailureAsync(args).ConfigureAwait(false);
            }
        }

        // ==================== События ====================

        async Task IEventBus.PublishAsync(IDomainEvent @event, CancellationToken cancellationToken)
        {
            await PublishEventAsync(@event, null, cancellationToken).ConfigureAwait(false);
        }

        async Task IEventBus.PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(events);
            foreach (var e in events)
                await PublishEventAsync(e, null, cancellationToken).ConfigureAwait(false);
        }

        async Task IEventBus.PublishAsync(IDomainEvent @event, CommandContext context, CancellationToken cancellationToken)
        {
            await PublishEventAsync(@event, context, cancellationToken).ConfigureAwait(false);
        }

        void IEventBus.Subscribe<TEvent>(IEventHandler<TEvent> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            SubscribeInternal(typeof(TEvent), (e, ct) => handler.Handle((TEvent)e, ct));
        }

        void IEventBus.Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            SubscribeInternal(typeof(TEvent), (e, ct) => handler((TEvent)e, ct));
        }

        void IEventBus.Unsubscribe<TEvent>(IEventHandler<TEvent> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            UnsubscribeInternal(typeof(TEvent), registration => registration.Target == handler);
        }

        void IEventBus.Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            UnsubscribeInternal(typeof(TEvent), registration => registration.Equals(handler));
        }

        private void SubscribeInternal(Type eventType, Func<IDomainEvent, CancellationToken, Task> handler)
        {
            if (_channel is null)
                throw new InvalidOperationException("Шина RabbitMQ не инициализирована. Сначала вызовите InitializeAsync.");

            // Разрываем контекст синхронизации и блокируем поток без риска deadlock
            Task.Run(() =>
            {
                var subscription = _eventSubscriptions.GetOrAdd(eventType, type =>
                {
                    bool isBroad = type.IsInterface || type.IsAbstract;
                    string bindingKey = isBroad ? "#" : type.Name;
                    string queueName = isBroad
                        ? $"dnd.event.broadcast.{type.Name}.{Guid.NewGuid():N}"
                        : $"dnd.event.{type.Name}";

                    // Асинхронные операции с await
                    var queueDeclareTask = _channel!.QueueDeclareAsync(
                        queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: true,
                        arguments: new Dictionary<string, object?>
                        {
                    { "x-dead-letter-exchange", DeadLetterExchange },
                    { "x-dead-letter-routing-key", $"event.{type.Name}" }
                        });
                    queueDeclareTask.GetAwaiter().GetResult(); // внутри Task.Run нет контекста

                    var queueBindTask = _channel.QueueBindAsync(queueName, EventExchange, bindingKey);
                    queueBindTask.GetAwaiter().GetResult();

                    var consumer = new AsyncEventingBasicConsumer(_channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        await ProcessEventMessageAsync(type, args).ConfigureAwait(false);
                    };

                    string consumerTag = _channel.BasicConsumeAsync(queueName, autoAck: false, consumer)
                        .GetAwaiter()
                        .GetResult();

                    return new EventTypeSubscription
                    {
                        QueueName = queueName,
                        ConsumerTag = consumerTag,
                        IsBroadSubscription = isBroad
                    };
                });

                lock (subscription.Handlers)
                {
                    subscription.Handlers.Add(handler);
                }
            }).GetAwaiter().GetResult();
        }

        private void UnsubscribeInternal(
            Type eventType,
            Predicate<Func<IDomainEvent, CancellationToken, Task>> predicate)
        {
            if (_eventSubscriptions.TryGetValue(eventType, out var subscription))
            {
                lock (subscription.Handlers)
                {
                    subscription.Handlers.RemoveAll(predicate);
                    if (subscription.Handlers.Count == 0)
                    {
                        _channel?.BasicCancelAsync(subscription.ConsumerTag).GetAwaiter().GetResult();
                        _channel?.QueueDeleteAsync(subscription.QueueName, ifUnused: true, ifEmpty: true).GetAwaiter().GetResult();
                        _eventSubscriptions.TryRemove(eventType, out _);
                    }
                }
            }
        }

        private async Task PublishEventAsync(IDomainEvent @event, CommandContext? context, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(@event);
            if (_channel is null)
                throw new InvalidOperationException("Шина RabbitMQ не инициализирована.");

            var envelope = new EventEnvelope
            {
                EventType = @event.GetType().AssemblyQualifiedName!,
                EventData = JsonSerializer.Serialize(@event, @event.GetType()),
                Timestamp = DateTime.UtcNow
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
            var routingKey = @event.GetType().Name;

            var properties = new BasicProperties
            {
                Persistent = true,
                Headers = new Dictionary<string, object?>()
            };

            if (context is not null)
            {
                properties.Headers["UserId"] = context.UserId.ToString();
                properties.Headers["SessionId"] = context.GameSessionId.ToString();
            }

            await _channel.BasicPublishAsync(
                exchange: EventExchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct).ConfigureAwait(false);
        }

        private async Task ProcessEventMessageAsync(Type subscribedType, BasicDeliverEventArgs args)
        {
            try
            {
                var json = Encoding.UTF8.GetString(args.Body.Span);
                var envelope = JsonSerializer.Deserialize<EventEnvelope>(json);
                if (envelope == null || string.IsNullOrEmpty(envelope.EventType))
                    return;

                var eventType = Type.GetType(envelope.EventType);
                if (eventType == null)
                    return;

                if (JsonSerializer.Deserialize(envelope.EventData, eventType) is not IDomainEvent eventObj)
                    return;

                if (_eventSubscriptions.TryGetValue(subscribedType, out var subscription))
                {
                    Func<IDomainEvent, CancellationToken, Task>[] handlers;
                    lock (subscription.Handlers)
                    {
                        handlers = [.. subscription.Handlers];
                    }

                    foreach (var handler in handlers)
                    {
                        try
                        {
                            // Для широких подписок проверяем, что тип события действительно подходит
                            if (subscription.IsBroadSubscription && !subscribedType.IsAssignableFrom(eventObj.GetType()))
                                continue;

                            await handler(eventObj, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка обработки события {EventType} обработчиком",
                                eventObj.GetType().Name);
                            // Не прерываем остальные обработчики
                        }
                    }
                }

                await _channel!.BasicAckAsync(args.DeliveryTag, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки сообщения события");
                await HandleProcessingFailureAsync(args).ConfigureAwait(false);
            }
        }

        private async Task HandleProcessingFailureAsync(BasicDeliverEventArgs args)
        {
            int attempts = GetDeliveryAttempts(args.BasicProperties?.Headers);
            attempts++;

            if (attempts >= 3) // максимальное число попыток
            {
                _logger.LogError("Сообщение не обработано после {Attempts} попыток, отправлено в dead letter", attempts);
                await _channel!.BasicNackAsync(args.DeliveryTag, false, false).ConfigureAwait(false);
            }
            else
            {
                var originalProps = args.BasicProperties;
                var newProps = new BasicProperties
                {
                    Persistent = originalProps?.Persistent ?? false,
                    Headers = new Dictionary<string, object?>()
                };

                if (originalProps?.Headers != null)
                {
                    foreach (var kvp in originalProps.Headers)
                        newProps.Headers[kvp.Key] = kvp.Value;
                }

                // Сохраняем счётчик попыток как long
                newProps.Headers["x-delivery-count"] = BitConverter.GetBytes((long)attempts);

                await _channel!.BasicPublishAsync(
                    exchange: "",
                    routingKey: args.RoutingKey,
                    mandatory: false,
                    basicProperties: newProps,
                    body: args.Body,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                await _channel.BasicAckAsync(args.DeliveryTag, false).ConfigureAwait(false);
            }
        }

        private static int GetDeliveryAttempts(IDictionary<string, object?>? headers)
        {
            if (headers == null) return 0;
            if (headers.TryGetValue("x-delivery-count", out var value) && value is byte[] bytes)
                return (int)BitConverter.ToInt64(bytes, 0);
            return 0;
        }

        public bool IsHealthy() => _connection?.IsOpen == true && _channel?.IsOpen == true;

        public async ValueTask DisposeAsync()
        {
            if (_channel is not null)
                await _channel.CloseAsync().ConfigureAwait(false);
            if (_connection is not null)
                await _connection.CloseAsync().ConfigureAwait(false);

            // Подавляем финализацию, так как ресурсы уже освобождены.
            GC.SuppressFinalize(this);
        }

        // ==================== Вспомогательные типы ====================

        private static BasicProperties CreateBasicProperties(ICommand command, CommandContext context)
        {
            var props = new BasicProperties
            {
                Persistent = true,
                Headers = new Dictionary<string, object?>
                {
                    { "UserId", context.UserId.ToString() },
                    { "SessionId", context.GameSessionId.ToString() }
                }
            };
            if (command is IIdempotentCommand idempotent)
                props.MessageId = idempotent.IdempotencyKey.ToString();
            return props;
        }

        private static byte[] SerializeCommand(ICommand command, CommandContext context)
        {
            var envelope = new CommandEnvelope
            {
                CommandType = command.GetType().AssemblyQualifiedName!,
                CommandData = JsonSerializer.Serialize(command, command.GetType()),
                UserId = context.UserId,
                SessionId = context.GameSessionId,
                Timestamp = DateTime.UtcNow
            };
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        }

        private sealed class CommandEnvelope
        {
            public string CommandType { get; set; } = string.Empty;
            public string CommandData { get; set; } = string.Empty;
            public Guid UserId { get; set; }
            public Guid SessionId { get; set; }
            public DateTime Timestamp { get; set; }
        }

        private sealed class EventEnvelope
        {
            public string EventType { get; set; } = string.Empty;
            public string EventData { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }
    }
}