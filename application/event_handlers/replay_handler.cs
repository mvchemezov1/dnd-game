using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.events;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Хранилище событий с расширенными возможностями для воспроизведения.
    /// </summary>
    public interface IReplayEventStore
    {
        Task AppendAsync(IDomainEvent @event, ReplayMetadata metadata, CancellationToken cancellationToken = default);
        Task<IEnumerable<IDomainEvent>> GetEventsAsync(Guid aggregateId, DateTime? toTimestamp, CancellationToken cancellationToken = default);
        Task<IEnumerable<IDomainEvent>> GetEventsBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
        Task<long> GetEventCountAsync(Guid aggregateId, CancellationToken cancellationToken = default);
        Task<IDomainEvent?> GetLastEventAsync(Guid aggregateId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Метаданные события для целей воспроизведения.
    /// </summary>
    public class ReplayMetadata
    {
        public Guid SessionId { get; set; }
        public DateTime Timestamp { get; set; }
        public long SequenceNumber { get; set; }
        public string? Description { get; set; } // краткое описание для журнала
    }

    /// <summary>
    /// Сервис, предоставляющий текущую игровую сессию.
    /// </summary>
    public interface ICurrentSessionProvider
    {
        Guid GetCurrentSessionId();
    }

    /// <summary>
    /// Сервис для построения текстового журнала из доменных событий.
    /// </summary>
    public interface INarrativeLogBuilder
    {
        string BuildEntry(IDomainEvent @event);
    }

    /// <summary>
    /// Обработчик воспроизведения: сохраняет каждое доменное событие с метаданными
    /// для последующего анализа, восстановления состояния или создания логов.
    /// </summary>
    public class ReplayHandler(
        IReplayEventStore eventStore,
        ICurrentSessionProvider sessionProvider,
        INarrativeLogBuilder narrativeBuilder,
        ILogger<ReplayHandler> logger) : IEventHandler<IDomainEvent>
    {
        private readonly IReplayEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        private readonly ICurrentSessionProvider _sessionProvider = sessionProvider ?? throw new ArgumentNullException(nameof(sessionProvider));
        private readonly INarrativeLogBuilder _narrativeBuilder = narrativeBuilder ?? throw new ArgumentNullException(nameof(narrativeBuilder));
        private readonly ILogger<ReplayHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private long _globalSequence = 0;

        public async Task Handle(IDomainEvent @event, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(@event);
            cancellationToken.ThrowIfCancellationRequested();

            // Получаем текущий идентификатор сессии (кампании)
            var sessionId = _sessionProvider.GetCurrentSessionId();

            // Атомарно увеличиваем глобальный счётчик событий для сквозной нумерации
            var sequenceNumber = Interlocked.Increment(ref _globalSequence);

            // Строим описание события для человеко-читаемого журнала
            var description = _narrativeBuilder.BuildEntry(@event);

            // Формируем метаданные
            var metadata = new ReplayMetadata
            {
                SessionId = sessionId,
                Timestamp = DateTime.UtcNow,
                SequenceNumber = sequenceNumber,
                Description = description
            };

            // Сохраняем событие с метаданными в хранилище
            await _eventStore.AppendAsync(@event, metadata, cancellationToken).ConfigureAwait(false);

            // Логируем факт записи для диагностики
            _logger.LogTrace("Событие воспроизведения #{Sequence}: {EventType} (сессия {SessionId})",
                sequenceNumber, @event.GetType().Name, sessionId);
        }
    }
}