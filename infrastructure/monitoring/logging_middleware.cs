#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.domain.queries;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.monitoring
{
    /// <summary>
    /// Уровни логирования для Middleware.
    /// </summary>
    public enum MiddlewareLogLevel
    {
        /// <summary>Логировать только критические ошибки.</summary>
        Minimal,

        /// <summary>Логировать команды/запросы и ошибки.</summary>
        Normal,

        /// <summary>Логировать все детали, включая полезную нагрузку.</summary>
        Verbose
    }

    /// <summary>
    /// Middleware для сквозного логирования команд, запросов и событий.
    /// Реализует <see cref="ICommandPipelineBehavior"/> и <see cref="IQueryPipelineBehavior"/>.
    /// </summary>
    /// <remarks>
    /// Создаёт экземпляр middleware логирования.
    /// </remarks>
    /// <param name="logger">Логгер.</param>
    /// <param name="logLevel">Уровень детализации логирования.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="logger"/> равен null.</exception>
    public class LoggingMiddleware(ILogger<LoggingMiddleware> logger, MiddlewareLogLevel logLevel = MiddlewareLogLevel.Normal) : ICommandPipelineBehavior, IQueryPipelineBehavior
    {
        private readonly ILogger<LoggingMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly MiddlewareLogLevel _logLevel = logLevel;

        /// <inheritdoc />
        public async Task HandleAsync<TCommand>(
            TCommand command,
            CommandContext context,
            Func<Task> next) where TCommand : ICommand
        {
            ArgumentNullException.ThrowIfNull(next, nameof(next));

            var commandType = typeof(TCommand).Name;
            var userId = context?.UserId ?? Guid.Empty;
            var sessionId = context?.GameSessionId ?? Guid.Empty;

            if (_logLevel >= MiddlewareLogLevel.Normal)
            {
                _logger.LogInformation(
                    "Команда {CommandType} начата | User={UserId} Session={SessionId}",
                    commandType, userId, sessionId);
            }

            if (_logLevel >= MiddlewareLogLevel.Verbose)
            {
                _logger.LogDebug("Полезная нагрузка команды: {@Command}", command);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await next().ConfigureAwait(false);
                stopwatch.Stop();

                if (_logLevel >= MiddlewareLogLevel.Normal)
                {
                    _logger.LogInformation(
                        "Команда {CommandType} завершена за {ElapsedMs} мс",
                        commandType, stopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "Команда {CommandType} завершилась с ошибкой после {ElapsedMs} мс | User={UserId} Session={SessionId}",
                    commandType, stopwatch.ElapsedMilliseconds, userId, sessionId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<TResult> HandleAsync<TResult>(
            IQuery<TResult> query,
            QueryContext context,
            Func<Task<TResult>> next)
        {
            ArgumentNullException.ThrowIfNull(next, nameof(next));

            var queryType = query.GetType().Name;
            var userId = context?.UserId ?? Guid.Empty;
            var sessionId = context?.GameSessionId ?? Guid.Empty;

            if (_logLevel >= MiddlewareLogLevel.Normal)
            {
                _logger.LogInformation(
                    "Запрос {QueryType} начат | User={UserId} Session={SessionId}",
                    queryType, userId, sessionId);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await next().ConfigureAwait(false);
                stopwatch.Stop();

                if (_logLevel >= MiddlewareLogLevel.Normal)
                {
                    _logger.LogInformation(
                        "Запрос {QueryType} завершён за {ElapsedMs} мс",
                        queryType, stopwatch.ElapsedMilliseconds);
                }
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "Запрос {QueryType} завершился с ошибкой после {ElapsedMs} мс | User={UserId} Session={SessionId}",
                    queryType, stopwatch.ElapsedMilliseconds, userId, sessionId);
                throw;
            }
        }

        /// <summary>
        /// Обрабатывает доменное событие для логирования.
        /// Может быть зарегистрирован как обработчик событий.
        /// </summary>
        public Task HandleEvent(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(@event);
            cancellationToken.ThrowIfCancellationRequested();

            if (_logLevel >= MiddlewareLogLevel.Verbose)
            {
                _logger.LogDebug("Событие {EventType}: {@Event}", @event.GetType().Name, @event);
            }
            else if (_logLevel >= MiddlewareLogLevel.Normal)
            {
                // На уровне Normal логируем только важные события
                if (@event is CharacterDied or CombatStarted or QuestCompleted)
                {
                    _logger.LogInformation("Важное событие: {EventType} | Данные: {@Event}",
                        @event.GetType().Name, @event);
                }
            }
            // На уровне Minimal события не логируются
            return Task.CompletedTask;
        }
    }
}