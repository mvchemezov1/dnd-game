#nullable enable
using dnd_game.domain.aggregates;
using dnd_game.domain.events;
using dnd_game.domain.exceptions;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.monitoring;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.event_store
{
    /// <summary>
    /// Реализация <see cref="IEventStore"/> на базе PostgreSQL.
    /// Хранит события в таблице events, поддерживает снимки (snapshots) и публикацию событий в шину.
    /// </summary>
    public class PostgresEventStore : IEventStore
    {
        private readonly string _connectionString;
        private readonly ISnapshotStore _snapshotStore;
        private readonly IConsistencyManager _consistencyManager;
        private readonly ILogger<PostgresEventStore> _logger;
        private readonly IMetricsCollector _metrics;
        private readonly IEventBus _eventBus;
        private readonly ConcurrentBag<Func<StoredEvent, CancellationToken, Task>> _subscribers = new();

        public PostgresEventStore(
            string connectionString,
            ISnapshotStore snapshotStore,
            IConsistencyManager consistencyManager,
            ILogger<PostgresEventStore> logger,
            IMetricsCollector metrics,
            IEventBus eventBus)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
            _consistencyManager = consistencyManager ?? throw new ArgumentNullException(nameof(consistencyManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        // ==================== Сохранение ====================

        /// <inheritdoc />
        public async Task Save<T>(T aggregate, CancellationToken cancellationToken = default) where T : AggregateRoot, new()
        {
            var metadata = new EventMetadata
            {
                UserId = Guid.Empty,
                GameSessionId = Guid.Empty
            };
            await SaveWithMetadata(aggregate, metadata, cancellationToken);
        }

        /// <inheritdoc />
        public async Task SaveWithMetadata<T>(
            T aggregate,
            EventMetadata metadataTemplate,
            CancellationToken cancellationToken = default) where T : AggregateRoot, new()
        {
            ArgumentNullException.ThrowIfNull(aggregate);
            ArgumentNullException.ThrowIfNull(metadataTemplate);

            const int maxRetries = 3;
            int attempt = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var consistencyResult = await _consistencyManager.EnforceConsistencyAsync(
                        aggregate,
                        aggregate.OriginalVersion,
                        metadataTemplate.UserId.ToString(),
                        cancellationToken);

                    if (consistencyResult != ConsistencyResult.Success)
                    {
                        throw consistencyResult switch
                        {
                            ConsistencyResult.VersionConflict => new StateConflictException(aggregate.Id, aggregate.OriginalVersion, aggregate.Version),
                            ConsistencyResult.LockTimeout => new InvalidOperationException("Таймаут блокировки при сохранении агрегата."),
                            ConsistencyResult.InvariantViolation => new RuleViolation("Invariant", "Нарушены инварианты агрегата."),
                            ConsistencyResult.GlobalRuleViolation => new RuleViolation("Global", "Нарушены глобальные правила."),
                            _ => new InvalidOperationException("Проверка согласованности не удалась.")
                        };
                    }

                    await SaveInternal(aggregate, metadataTemplate, cancellationToken);
                    return;
                }
                catch (StateConflictException) when (attempt < maxRetries)
                {
                    attempt++;
                    _logger.LogWarning("Конфликт версий при сохранении агрегата {AggregateId}. Попытка {Attempt}/{MaxRetries}",
                        aggregate.Id, attempt, maxRetries);

                    await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)), cancellationToken);
                    var reloaded = await Load<T>(aggregate.Id, cancellationToken)
                        ?? throw new InvalidOperationException($"Агрегат {aggregate.Id} не найден при повторной попытке.");

                    var uncommitted = aggregate.GetUncommittedEvents().ToList();
                    foreach (var @event in uncommitted)
                    {
                        reloaded.ApplyChange(@event);
                    }
                    aggregate = reloaded;
                }
                catch (Exception ex) when (attempt >= maxRetries)
                {
                    _logger.LogError(ex, "Не удалось сохранить агрегат {AggregateId} после {MaxRetries} попыток.",
                        aggregate.Id, maxRetries);
                    throw new InvalidOperationException($"Не удалось сохранить агрегат {aggregate.Id} после {maxRetries} попыток.", ex);
                }
            }
        }

        /// <summary>
        /// Внутренний метод сохранения событий в транзакции.
        /// </summary>
        private async Task SaveInternal<T>(
            T aggregate,
            EventMetadata metadataTemplate,
            CancellationToken cancellationToken) where T : AggregateRoot, new()
        {
            var events = aggregate.GetUncommittedEvents().ToList();
            if (events.Count == 0)
                return;

            var savedEvents = new List<StoredEvent>(events.Count);

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

            int nextVersion;
            await using (var lockCmd = new NpgsqlCommand(@"
        SELECT version FROM events
        WHERE aggregate_id = @aggId
        ORDER BY version DESC LIMIT 1
        FOR UPDATE
    ", conn, tx))
            {
                lockCmd.Parameters.AddWithValue("aggId", NpgsqlDbType.Uuid, aggregate.Id);
                var currentMaxVersionObj = await lockCmd.ExecuteScalarAsync(cancellationToken);
                int currentMaxVersion = currentMaxVersionObj as int? ?? 0;

                if (aggregate.OriginalVersion != currentMaxVersion)
                {
                    _logger.LogWarning(
                        "Конфликт версий в SaveInternal для агрегата {AggregateId}: ожидалась {Expected}, фактическая {Actual}",
                        aggregate.Id, aggregate.OriginalVersion, currentMaxVersion);
                    _metrics.IncrementCounter("dnd.eventstore.concurrency_conflict");
                    throw new StateConflictException(aggregate.Id, aggregate.OriginalVersion, currentMaxVersion);
                }

                nextVersion = currentMaxVersion + 1;

                foreach (var domainEvent in events)
                {
                    var metadata = new EventMetadata
                    {
                        EventId = Guid.NewGuid(),
                        EventType = domainEvent.GetType().AssemblyQualifiedName!,
                        AggregateId = aggregate.Id,
                        Version = nextVersion,
                        Timestamp = DateTime.UtcNow,
                        UserId = metadataTemplate.UserId,
                        GameSessionId = metadataTemplate.GameSessionId,
                        CustomHeaders = metadataTemplate.CustomHeaders == null
                            ? null
                            : new Dictionary<string, string>(metadataTemplate.CustomHeaders)
                    };

                    string json = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
                    object? headersJson = metadata.CustomHeaders != null
                        ? JsonSerializer.Serialize(metadata.CustomHeaders)
                        : DBNull.Value;

                    // Вставка в основную таблицу событий
                    await using (var insertCmd = new NpgsqlCommand(@"
                INSERT INTO events (event_id, aggregate_id, aggregate_type, version, event_type, data, user_id, session_id, custom_headers, timestamp)
                VALUES (@event_id, @aggId, @aggType, @ver, @type, @data::jsonb, @userId, @sessionId, @headers::jsonb, @ts)
            ", conn, tx))
                    {
                        insertCmd.Parameters.AddWithValue("event_id", NpgsqlDbType.Uuid, metadata.EventId);
                        insertCmd.Parameters.AddWithValue("aggId", NpgsqlDbType.Uuid, aggregate.Id);
                        insertCmd.Parameters.AddWithValue("aggType", typeof(T).Name);
                        insertCmd.Parameters.AddWithValue("ver", nextVersion);
                        insertCmd.Parameters.AddWithValue("type", metadata.EventType);
                        insertCmd.Parameters.AddWithValue("data", json);
                        insertCmd.Parameters.AddWithValue("userId", NpgsqlDbType.Uuid, metadata.UserId);
                        insertCmd.Parameters.AddWithValue("sessionId", NpgsqlDbType.Uuid, metadata.GameSessionId);
                        insertCmd.Parameters.AddWithValue("headers", NpgsqlDbType.Jsonb, headersJson);
                        insertCmd.Parameters.AddWithValue("ts", metadata.Timestamp);

                        await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // ✅ Запись в outbox (в той же транзакции)
                    await using (var outboxCmd = new NpgsqlCommand(@"
                INSERT INTO outbox_events (aggregate_id, event_type, payload)
                VALUES (@aggId, @eventType, @payload::jsonb)
            ", conn, tx))
                    {
                        outboxCmd.Parameters.AddWithValue("aggId", NpgsqlDbType.Uuid, aggregate.Id);
                        outboxCmd.Parameters.AddWithValue("eventType", metadata.EventType);
                        outboxCmd.Parameters.AddWithValue("payload", json);
                        await outboxCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    savedEvents.Add(new StoredEvent { DomainEvent = domainEvent, Metadata = metadata });
                    nextVersion++;
                }
            }

            await tx.CommitAsync(cancellationToken);

            // ❌ УДАЛИТЬ или закомментировать старый блок публикации в IEventBus
            // foreach (var storedEvent in savedEvents)
            // {
            //     await _eventBus.PublishAsync(storedEvent.DomainEvent, cancellationToken);
            // }

            // Обновляем состояние агрегата
            aggregate.SetVersion(nextVersion - 1);
            aggregate.ClearUncommittedEvents();

            // Создаём снимок при необходимости
            if (await _snapshotStore.ShouldCreateSnapshotAsync(aggregate.Id, aggregate.Version))
            {
                var snapshot = SnapshotStore.CreateSnapshotFromAggregate(aggregate);
                await _snapshotStore.SaveSnapshotAsync(snapshot);
            }
        }

        // ==================== Загрузка ====================

        /// <inheritdoc />
        public async Task<T?> Load<T>(Guid aggregateId, CancellationToken cancellationToken = default) where T : AggregateRoot, new()
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));

            var snapshot = await _snapshotStore.GetLatestSnapshotAsync(aggregateId, int.MaxValue);
            T? aggregate = null;

            if (snapshot != null)
            {
                aggregate = SnapshotStore.RestoreAggregateFromSnapshot<T>(snapshot);
                aggregate?.SetVersion(snapshot.Version);
            }

            int fromVersion = snapshot?.Version ?? 0;
            var stream = await GetEventStreamInternalAsync(aggregateId, new ReadStreamOptions { FromVersion = fromVersion }, cancellationToken);
            var domainEvents = stream?.Events.Select(e => e.DomainEvent) ?? [];

            if (aggregate == null)
            {
                aggregate = new T();
                aggregate.LoadFromHistory(domainEvents);
            }
            else
            {
                aggregate.LoadFromHistory(domainEvents);
            }

            return aggregate;
        }

        /// <inheritdoc />
        public Task<T?> LoadWithMetadata<T>(Guid aggregateId, CancellationToken cancellationToken = default) where T : AggregateRoot, new()
        {
            return Load<T>(aggregateId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<StoredEvent>> GetEventStreamAsync(
            Guid aggregateId,
            ReadStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var stream = await GetEventStreamInternalAsync(aggregateId, options, cancellationToken);
            return stream?.Events ?? Enumerable.Empty<StoredEvent>();
        }

        /// <summary>
        /// Внутренний метод, возвращающий поток целиком (с метаданными потока).
        /// </summary>
        private async Task<EventStream?> GetEventStreamInternalAsync(
            Guid aggregateId,
            ReadStreamOptions? options,
            CancellationToken cancellationToken)
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var where = new List<string> { "aggregate_id = @aggId" };
            var parameters = new List<NpgsqlParameter>
            {
                new("aggId", NpgsqlDbType.Uuid) { Value = aggregateId }
            };

            if (options?.FromVersion > 0)
            {
                where.Add("version > @fromVer");
                parameters.Add(new NpgsqlParameter("fromVer", options.FromVersion));
            }
            if (options?.EventTypeFilter != null)
            {
                where.Add("event_type = @typeFilter");
                parameters.Add(new NpgsqlParameter("typeFilter", options.EventTypeFilter));
            }
            if (options?.FromTimestamp != null)
            {
                where.Add("timestamp >= @fromTs");
                parameters.Add(new NpgsqlParameter("fromTs", options.FromTimestamp.Value));
            }

            string orderBy = options?.ReadBackwards == true ? "ORDER BY version DESC" : "ORDER BY version ASC";
            string limit = options?.MaxCount > 0 ? $"LIMIT {options.MaxCount.Value}" : "";

            using var cmd = new NpgsqlCommand($@"
                SELECT event_id, event_type, aggregate_id, aggregate_type, version, data,
                       user_id, session_id, custom_headers, timestamp
                FROM events
                WHERE {string.Join(" AND ", where)}
                {orderBy}
                {limit}
            ", conn);

            cmd.Parameters.AddRange(parameters.ToArray());

            var events = new List<StoredEvent>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(ReadStoredEvent(reader));
            }

            if (events.Count == 0)
                return null;

            return new EventStream
            {
                AggregateId = aggregateId,
                Version = events.Last().Metadata.Version,
                AggregateType = events.First().Metadata.AggregateType,
                Events = events,
                CreatedAt = events.First().Metadata.Timestamp,
                UpdatedAt = events.Last().Metadata.Timestamp
            };
        }

        // ==================== Снимки ====================

        /// <inheritdoc />
        public Task SaveSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return _snapshotStore.SaveSnapshotAsync(snapshot);
        }

        /// <inheritdoc />
        public Task<Snapshot?> GetLatestSnapshotAsync(Guid aggregateId, int maxVersion, CancellationToken cancellationToken = default)
        {
            return _snapshotStore.GetLatestSnapshotAsync(aggregateId, maxVersion);
        }

        // ==================== Глобальные запросы ====================

        /// <inheritdoc />
        public async Task<IEnumerable<StoredEvent>> GetEventsByTypeAsync(
            string eventType,
            DateTime? from = null,
            DateTime? to = null,
            int? maxCount = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Тип события не может быть пустым.", nameof(eventType));

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            string where = "event_type = @type";
            var parameters = new List<NpgsqlParameter> { new("type", eventType) };

            if (from.HasValue)
            {
                where += " AND timestamp >= @from";
                parameters.Add(new NpgsqlParameter("from", from.Value));
            }
            if (to.HasValue)
            {
                where += " AND timestamp <= @to";
                parameters.Add(new NpgsqlParameter("to", to.Value));
            }

            string limit = maxCount.HasValue ? $"LIMIT {maxCount.Value}" : "";

            using var cmd = new NpgsqlCommand($@"
                SELECT event_id, event_type, aggregate_id, aggregate_type, version, data,
                       user_id, session_id, custom_headers, timestamp
                FROM events
                WHERE {where}
                ORDER BY timestamp ASC
                {limit}
            ", conn);
            cmd.Parameters.AddRange(parameters.ToArray());

            var result = new List<StoredEvent>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadStoredEvent(reader));
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<StoredEvent>> GetEventsBySessionAsync(
            Guid gameSessionId,
            CancellationToken cancellationToken = default)
        {
            if (gameSessionId == Guid.Empty)
                throw new ArgumentException("Идентификатор сессии не может быть пустым.", nameof(gameSessionId));

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = new NpgsqlCommand(@"
                SELECT event_id, event_type, aggregate_id, aggregate_type, version, data,
                       user_id, session_id, custom_headers, timestamp
                FROM events WHERE session_id = @sessionId ORDER BY timestamp ASC
            ", conn);
            cmd.Parameters.AddWithValue("sessionId", NpgsqlDbType.Uuid, gameSessionId);

            var result = new List<StoredEvent>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadStoredEvent(reader));
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<int> GetCurrentVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = new NpgsqlCommand(
                "SELECT COALESCE(MAX(version),0) FROM events WHERE aggregate_id = @aggId", conn);
            cmd.Parameters.AddWithValue("aggId", NpgsqlDbType.Uuid, aggregateId);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result switch
            {
                DBNull => 0,
                null => 0,
                _ => Convert.ToInt32(result)
            };
        }

        /// <inheritdoc />
        public Task SubscribeAsync(
            Func<StoredEvent, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
                {
                    if (handler == null) throw new ArgumentNullException(nameof(handler));
                    _subscribers.Add(handler);
                    return Task.CompletedTask;
                }

        // ==================== Дополнительные методы для совместимости ====================

        /// <inheritdoc />
        public async Task<IEnumerable<object>> GetAllEvents(CancellationToken cancellationToken = default)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            using var cmd = new NpgsqlCommand(
                "SELECT event_type, data FROM events ORDER BY id", conn);
            var events = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var typeName = reader.GetString(0);
                var json = reader.GetString(1);
                var type = Type.GetType(typeName);
                if (type != null)
                {
                    var @event = JsonSerializer.Deserialize(json, type);
                    if (@event != null)
                        events.Add(@event);
                }
            }
            return events;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<object>> GetEvents(
            Guid aggregateId,
            int fromVersion = 0,
            CancellationToken cancellationToken = default)
        {
            var stream = await GetEventStreamInternalAsync(
                aggregateId,
                new ReadStreamOptions { FromVersion = fromVersion },
                cancellationToken);
            return stream?.Events.Select(e => e.DomainEvent as object) ?? [];
        }

        // ==================== Вспомогательные методы ====================

        private static StoredEvent ReadStoredEvent(NpgsqlDataReader reader)
        {
            var eventId = reader.GetGuid(0);
            var eventTypeName = reader.GetString(1);
            var aggId = reader.GetGuid(2);
            var aggregateType = reader.GetString(3);
            var version = reader.GetInt32(4);
            var json = reader.GetString(5);
            var userId = reader.GetGuid(6);
            var sessionId = reader.GetGuid(7);
            var headersJson = reader.IsDBNull(8) ? null : reader.GetString(8);
            var ts = reader.GetDateTime(9);

            var type = Type.GetType(eventTypeName)
                ?? throw new InvalidOperationException($"Неизвестный тип события: {eventTypeName}");

            var domainEvent = JsonSerializer.Deserialize(json, type) as IDomainEvent
                ?? throw new InvalidOperationException($"Не удалось десериализовать событие: {eventTypeName}");

            return new StoredEvent
            {
                DomainEvent = domainEvent,
                Metadata = new EventMetadata
                {
                    EventId = eventId,
                    EventType = eventTypeName,
                    AggregateId = aggId,
                    Version = version,
                    Timestamp = ts,
                    UserId = userId,
                    GameSessionId = sessionId,
                    CustomHeaders = headersJson != null
                        ? JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson)
                        : null,
                    AggregateType = aggregateType
                }
            };
        }
    }
}