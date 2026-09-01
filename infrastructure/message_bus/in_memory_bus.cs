#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using dnd_game.application.event_handlers;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.queries;

namespace dnd_game.infrastructure.message_bus
{
    /// <summary>
    /// Единая шина для команд, запросов и событий, работающая в памяти процесса.
    /// Поддерживает обработчики, зарегистрированные через DI, делегаты и явные экземпляры.
    /// </summary>
    public class InMemoryBus(IServiceProvider provider, ILogger<InMemoryBus> logger) : ICommandBus, IQueryBus, IEventBus
    {
        private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        private readonly ILogger<InMemoryBus> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Команды: тип -> список обработчиков (делегатов)
        private readonly ConcurrentDictionary<Type, List<Func<ICommand, CommandContext?, Task>>> _commandHandlers = new();

        // События: тип события -> список регистраций обработчиков
        private readonly ConcurrentDictionary<Type, List<EventHandlerRegistration>> _eventHandlers = new();

        private class EventHandlerRegistration
        {
            /// <summary>Тип обработчика для разрешения через DI.</summary>
            public Type? HandlerType { get; set; }

            /// <summary>Типизированный делегат обработчика.</summary>
            public Func<IDomainEvent, CancellationToken, Task>? HandlerDelegate { get; set; }
        }

        /// <summary>
        /// Если true, исключения из обработчиков событий будут пробрасываться вызывающему коду.
        /// По умолчанию false — ошибки логируются, но не прерывают обработку.
        /// </summary>
        public bool ThrowOnEventError { get; set; } = false;

        // ==================== Команды ====================

        /// <summary>Отправляет команду без дополнительного контекста.</summary>
        public Task Send<TCommand>(TCommand command) where TCommand : ICommand
            => SendAsync(command, null);

        /// <summary>Регистрирует обработчик команды через делегат.</summary>
        public void Subscribe<TCommand>(Func<TCommand, CommandContext?, Task> handler) where TCommand : ICommand
        {
            ArgumentNullException.ThrowIfNull(handler);
            var commandType = typeof(TCommand);

            _commandHandlers.AddOrUpdate(
                commandType,
                _ =>
                [
                    (cmd, ctx) => handler((TCommand)cmd, ctx)
                ],
                (_, list) =>
                {
                    list.Add((cmd, ctx) => handler((TCommand)cmd, ctx));
                    return list;
                });
        }

        /// <summary>Отправляет команду в шину.</summary>
        public async Task SendAsync(ICommand command, CommandContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(command);
            context ??= new CommandContext();
            var commandType = command.GetType();
            var ct = context.CancellationToken;

            // Получаем все поведения конвейера
            var behaviors = _provider.GetServices<ICommandPipelineBehavior>().ToArray();

            // Финальный делегат — выполняет реальную диспетчеризацию
            Func<Task> handlerAction = async () =>
            {
                // 1. Обработчики, зарегистрированные через делегаты
                if (_commandHandlers.TryGetValue(commandType, out var handlers))
                {
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            await handler(command, context).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка обработки команды {CommandType}", commandType.Name);
                            throw;
                        }
                    }
                    return;
                }

                // 2. Обработчик через DI
                var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);
                var diHandler = _provider.GetService(handlerType);
                if (diHandler != null)
                {
                    var handleMethod = handlerType.GetMethod("Handle", [commandType, typeof(CancellationToken)])
                        ?? throw new InvalidOperationException($"Метод Handle не найден для {handlerType.Name}.");
                    try
                    {
                        await ((Task)handleMethod.Invoke(diHandler, [command, ct])!).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка обработки команды {CommandType} через DI", commandType.Name);
                        throw;
                    }
                    return;
                }

                throw new InvalidOperationException($"Нет обработчика для команды типа '{commandType.Name}'.");
            };

            // Оборачиваем цепочку поведений (в обратном порядке, чтобы они выполнялись по порядку)
            foreach (var behavior in behaviors.Reverse())
            {
                var next = handlerAction;
                handlerAction = () => behavior.HandleAsync(command, context, next);
            }

            await handlerAction().ConfigureAwait(false);
        }

        // ==================== Запросы ====================

        public async Task<TResult> QueryAsync<TResult>(
            IQuery<TResult> query,
            QueryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            var queryType = query.GetType();
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(queryType, typeof(TResult));
            var handler = _provider.GetService(handlerType)
                ?? throw new InvalidOperationException($"Нет обработчика запроса '{queryType.Name}' с результатом '{typeof(TResult).Name}'.");

            var method = handlerType.GetMethod("Handle", [queryType, typeof(CancellationToken)])
                ?? throw new InvalidOperationException("Метод Handle не найден.");

            try
            {
                return await ((Task<TResult>)method.Invoke(handler, [query, cancellationToken])!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения запроса {QueryType}", queryType.Name);
                throw;
            }
        }

        // ==================== События ====================

        /// <summary>Публикует событие (без токена отмены).</summary>
        public Task Publish(IDomainEvent @event) => PublishAsync(@event, CancellationToken.None);

        /// <inheritdoc />
        public Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
            => PublishInternal(@event, cancellationToken);

        /// <inheritdoc />
        public async Task PublishAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(events);
            foreach (var e in events)
                await PublishInternal(e, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task PublishAsync(IDomainEvent @event, CommandContext context, CancellationToken cancellationToken = default)
            => PublishInternal(@event, cancellationToken);

        /// <inheritdoc />
        public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IDomainEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            var eventType = typeof(TEvent);
            var registration = new EventHandlerRegistration
            {
                HandlerDelegate = (e, ct) => handler.Handle((TEvent)e, ct)
            };
            AddEventHandler(eventType, registration);
        }

        /// <inheritdoc />
        public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            var eventType = typeof(TEvent);
            var registration = new EventHandlerRegistration
            {
                HandlerDelegate = (e, ct) => handler((TEvent)e, ct)
            };
            AddEventHandler(eventType, registration);
        }

        /// <inheritdoc />
        public void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IDomainEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            var eventType = typeof(TEvent);
            if (_eventHandlers.TryGetValue(eventType, out var list))
            {
                list.RemoveAll(r => r.HandlerDelegate != null &&
                                    r.HandlerDelegate.Target == handler); // сравниваем по объекту
                if (list.Count == 0)
                    _eventHandlers.TryRemove(eventType, out _);
            }
        }

        /// <inheritdoc />
        public void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            var eventType = typeof(TEvent);
            if (_eventHandlers.TryGetValue(eventType, out var list))
            {
                list.RemoveAll(r => r.HandlerDelegate != null &&
                                    r.HandlerDelegate.Equals(handler));
                if (list.Count == 0)
                    _eventHandlers.TryRemove(eventType, out _);
            }
        }

        /// <summary>
        /// Регистрирует обработчик типа <typeparamref name="THandler"/> для событий типа <typeparamref name="TEvent"/>.
        /// Обработчик будет разрешаться через DI при каждой публикации события.
        /// </summary>
        public void Subscribe<TEvent, THandler>()
            where TEvent : IDomainEvent
            where THandler : IEventHandler<TEvent>
        {
            var eventType = typeof(TEvent);
            var registration = new EventHandlerRegistration { HandlerType = typeof(THandler) };
            AddEventHandler(eventType, registration);
        }

        private void AddEventHandler(Type eventType, EventHandlerRegistration registration)
        {
            _eventHandlers.AddOrUpdate(
                eventType,
                _ => [registration],
                (_, list) =>
                {
                    list.Add(registration);
                    return list;
                });
        }

        /// <summary>
        /// Внутренний механизм публикации события.
        /// Вызывает всех подписчиков, зарегистрированных на точный тип события,
        /// а также на интерфейсы, которым удовлетворяет тип события.
        /// </summary>
        private async Task PublishInternal(IDomainEvent @event, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(@event);
            var eventType = @event.GetType();

            // 1. Обработчики, подписанные на точный тип
            if (_eventHandlers.TryGetValue(eventType, out var exactRegistrations))
            {
                foreach (var reg in exactRegistrations)
                    await InvokeRegistration(reg, @event, eventType, cancellationToken).ConfigureAwait(false);
            }

            // 2. Обработчики, подписанные на базовые типы/интерфейсы
            foreach (var kvp in _eventHandlers)
            {
                if (kvp.Key == eventType) continue; // уже обработано
                if (!kvp.Key.IsAssignableFrom(eventType)) continue;

                foreach (var reg in kvp.Value)
                    await InvokeRegistration(reg, @event, kvp.Key, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task InvokeRegistration(
        EventHandlerRegistration registration,
        IDomainEvent @event,
        Type handlerEventType,
        CancellationToken ct)
        {
            try
            {
                if (registration.HandlerDelegate != null)
                {
                    await registration.HandlerDelegate(@event, ct).ConfigureAwait(false);
                }
                else if (registration.HandlerType != null)
                {
                    var handler = _provider.GetService(registration.HandlerType);
                    if (handler == null)
                    {
                        _logger.LogWarning("Обработчик {HandlerType} не зарегистрирован в DI", registration.HandlerType.Name);
                        return;
                    }

                    var method = registration.HandlerType.GetMethod("Handle", [handlerEventType, typeof(CancellationToken)]);
                    if (method != null)
                        await ((Task)method.Invoke(handler, [@event, ct])!).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (ThrowOnEventError)
                    throw;

                _logger.LogError(ex,
                    "Ошибка обработки события {EventType} обработчиком {HandlerType}",
                    @event.GetType().Name,
                    registration.HandlerType?.Name ?? "делегат");
            }
        }
    }
}