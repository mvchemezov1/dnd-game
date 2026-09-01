// tests/infrastructure/InMemoryEventStore.cs
using System.Collections.Concurrent;
using dnd_game.domain.aggregates;
using dnd_game.domain.events;
using dnd_game.infrastructure.event_store;
using dnd_game.domain.exceptions;

namespace dnd_game.tests.infrastructure;

/// <summary>
/// In-memory реализация IEventStore для интеграционных тестов.
/// </summary>
public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, List<StoredEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, Snapshot> _snapshots = new();

    // tests/integration/InMemoryEventStore.cs

    public Task<IEnumerable<object>> GetAllEvents(CancellationToken ct)
    {
        var allEvents = _events.Values.SelectMany(e => e).Select(e => e.DomainEvent).Cast<object>();
        return Task.FromResult(allEvents);
    }

    public Task<IEnumerable<object>> GetEvents(Guid aggregateId, int fromVersion = 0, CancellationToken ct = default)
    {
        if (!_events.TryGetValue(aggregateId, out var events))
            return Task.FromResult(Enumerable.Empty<object>());

        return Task.FromResult(events.Skip(fromVersion).Select(e => e.DomainEvent).Cast<object>());
    }

    public Task Save<T>(T aggregate, CancellationToken cancellationToken) where T : AggregateRoot, new()
    {
        return SaveWithMetadata(aggregate, new EventMetadata { UserId = Guid.Empty, GameSessionId = Guid.Empty }, cancellationToken);
    }

    public Task<T?> Load<T>(Guid aggregateId, CancellationToken cancellationToken) where T : AggregateRoot, new()
    {
        var aggregate = new T();
        if (!_events.TryGetValue(aggregateId, out var events))
        {
            return Task.FromResult<T?>(null);
        }

        aggregate.LoadFromHistory(events.Select(e => e.DomainEvent));
        aggregate.SetVersion(aggregate.Version); // синхронизируем OriginalVersion
        return Task.FromResult(aggregate)!;
    }

    public Task SaveWithMetadata<T>(T aggregate, EventMetadata metadata, CancellationToken cancellationToken) where T : AggregateRoot, new()
    {
        var events = aggregate.GetUncommittedEvents().ToList();
        if (!events.Any()) return Task.CompletedTask;

        var list = _events.GetOrAdd(aggregate.Id, _ => new List<StoredEvent>());
        int nextVersion = aggregate.OriginalVersion + 1;
        foreach (var domainEvent in events)
        {
            var storedEvent = new StoredEvent
            {
                DomainEvent = domainEvent,
                Metadata = new EventMetadata
                {
                    EventId = Guid.NewGuid(),
                    EventType = domainEvent.GetType().AssemblyQualifiedName!,
                    AggregateId = aggregate.Id,
                    Version = nextVersion,
                    Timestamp = DateTime.UtcNow,
                    UserId = metadata.UserId,
                    GameSessionId = metadata.GameSessionId,
                    CustomHeaders = metadata.CustomHeaders
                }
            };
            list.Add(storedEvent);
            nextVersion++;
        }

        aggregate.SetVersion(nextVersion - 1);
        aggregate.ClearUncommittedEvents();
        return Task.CompletedTask;
    }

    public Task<T?> LoadWithMetadata<T>(Guid aggregateId, CancellationToken cancellationToken) where T : AggregateRoot, new()
        => Load<T>(aggregateId, cancellationToken);

    public Task<IEnumerable<StoredEvent>> GetEventStreamAsync(Guid aggregateId, ReadStreamOptions? options = null, CancellationToken ct = default)
    {
        if (!_events.TryGetValue(aggregateId, out var events))
            return Task.FromResult(Enumerable.Empty<StoredEvent>());

        var query = events.AsEnumerable();
        if (options?.FromVersion > 0)
            query = query.Where(e => e.Metadata.Version > options.FromVersion);
        if (options?.EventTypeFilter != null)
            query = query.Where(e => e.Metadata.EventType == options.EventTypeFilter);
        if (options?.FromTimestamp != null)
            query = query.Where(e => e.Metadata.Timestamp >= options.FromTimestamp.Value);
        if (options?.ReadBackwards == true)
            query = query.OrderByDescending(e => e.Metadata.Version);
        if (options?.MaxCount > 0)
            query = query.Take(options.MaxCount.Value);

        return Task.FromResult(query);
    }

    public Task SaveSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        _snapshots[snapshot.AggregateId] = snapshot;
        return Task.CompletedTask;
    }

    public Task<Snapshot?> GetLatestSnapshotAsync(Guid aggregateId, int maxVersion, CancellationToken cancellationToken)
    {
        if (_snapshots.TryGetValue(aggregateId, out var snapshot) && snapshot.Version <= maxVersion)
            return Task.FromResult<Snapshot?>(snapshot);
        return Task.FromResult<Snapshot?>(null);
    }

    public Task<IEnumerable<StoredEvent>> GetEventsByTypeAsync(string eventType, DateTime? from = null, DateTime? to = null, int? maxCount = null, CancellationToken ct = default)
    {
        var query = _events.Values.SelectMany(e => e)
            .Where(e => e.Metadata.EventType == eventType);
        if (from.HasValue)
            query = query.Where(e => e.Metadata.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.Metadata.Timestamp <= to.Value);
        if (maxCount.HasValue)
            query = query.Take(maxCount.Value);
        return Task.FromResult(query);
    }

    public Task<IEnumerable<StoredEvent>> GetEventsBySessionAsync(Guid gameSessionId, CancellationToken cancellationToken)
    {
        var query = _events.Values.SelectMany(e => e)
            .Where(e => e.Metadata.GameSessionId == gameSessionId);
        return Task.FromResult(query);
    }

    public Task<int> GetCurrentVersionAsync(Guid aggregateId, CancellationToken cancellationToken)
    {
        if (_events.TryGetValue(aggregateId, out var events) && events.Any())
            return Task.FromResult(events.Last().Metadata.Version);
        return Task.FromResult(0);
    }

    public Task SubscribeAsync(Func<StoredEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        // Для тестов не требуется
        return Task.CompletedTask;
    }
}