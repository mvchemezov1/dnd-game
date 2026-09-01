#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using dnd_game.domain.events;
using dnd_game.domain.exceptions;

namespace dnd_game.infrastructure.event_store
{
    /// <summary>
    /// Представляет поток событий одного агрегата, включая метаданные и версионирование.
    /// Поддерживает добавление событий с проверкой версий, загрузку из истории
    /// и формирование снимков для ускорения восстановления.
    /// </summary>
    public class EventStream
    {
        /// <summary>Идентификатор агрегата, которому принадлежит поток.</summary>
        public Guid AggregateId { get; set; }

        /// <summary>Текущая версия агрегата (количество применённых событий).</summary>
        public int Version { get; set; }

        /// <summary>Тип агрегата (например, "CharacterAggregate", "CombatAggregate").</summary>
        public string AggregateType { get; set; } = string.Empty;

        /// <summary>Список записей событий, каждая с событием и метаданными.</summary>
        public List<StoredEvent> Events { get; set; } = [];

        /// <summary>Временная метка создания потока (UTC).</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Временная метка последнего изменения (UTC).</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ---------- Добавление событий ----------

        /// <summary>
        /// Добавляет одно доменное событие с метаданными в конец потока.
        /// Автоматически увеличивает версию и устанавливает её в метаданных.
        /// </summary>
        /// <param name="domainEvent">Доменное событие (не может быть null).</param>
        /// <param name="metadata">Метаданные события (версия будет перезаписана).</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="domainEvent"/> или <paramref name="metadata"/> равны null.</exception>
        public void Append(IDomainEvent domainEvent, EventMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(domainEvent, nameof(domainEvent));
            ArgumentNullException.ThrowIfNull(metadata, nameof(metadata));

            Version++;
            metadata.Version = Version;
            metadata.AggregateId = AggregateId;
            metadata.EventType = domainEvent.GetType().Name;
            metadata.Timestamp = DateTime.UtcNow;

            Events.Add(new StoredEvent
            {
                DomainEvent = domainEvent,
                Metadata = metadata
            });

            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Добавляет несколько событий пакетом. Версия увеличивается последовательно.
        /// Для каждого события создаются новые метаданные на основе шаблона.
        /// </summary>
        /// <param name="domainEvents">Коллекция доменных событий (не может быть null).</param>
        /// <param name="metadataTemplate">Шаблон метаданных для каждого события (не может быть null).</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="domainEvents"/> или <paramref name="metadataTemplate"/> равны null.</exception>
        public void AppendRange(IEnumerable<IDomainEvent> domainEvents, EventMetadata metadataTemplate)
        {
            ArgumentNullException.ThrowIfNull(domainEvents, nameof(domainEvents));
            ArgumentNullException.ThrowIfNull(metadataTemplate, nameof(metadataTemplate));

            foreach (var e in domainEvents)
            {
                var meta = new EventMetadata
                {
                    EventId = Guid.NewGuid(),
                    EventType = e.GetType().Name,
                    AggregateId = AggregateId,
                    UserId = metadataTemplate.UserId,
                    GameSessionId = metadataTemplate.GameSessionId,
                    CustomHeaders = metadataTemplate.CustomHeaders == null
                        ? null
                        : new Dictionary<string, string>(metadataTemplate.CustomHeaders)
                };
                Append(e, meta);
            }
        }

        // ---------- Проверка версий (оптимистическая блокировка) ----------

        /// <summary>
        /// Проверяет, что ожидаемая версия совпадает с текущей.
        /// Бросает <see cref="StateConflictException"/> при несовпадении.
        /// </summary>
        /// <param name="expectedVersion">Ожидаемая версия.</param>
        /// <exception cref="StateConflictException">Если версии не совпадают.</exception>
        public void AssertExpectedVersion(int expectedVersion)
        {
            if (expectedVersion != Version)
                throw new StateConflictException(AggregateId, expectedVersion, Version);
        }

        // ---------- Получение событий ----------

        /// <summary>
        /// Возвращает все доменные события (без метаданных) для восстановления агрегата.
        /// </summary>
        public IEnumerable<IDomainEvent> GetDomainEvents()
        {
            return Events.Select(e => e.DomainEvent);
        }

        /// <summary>
        /// Возвращает события, начиная с указанной версии (для догрузки).
        /// Версии нумеруются с 1; события с версией ≤ <paramref name="fromVersion"/> пропускаются.
        /// </summary>
        /// <param name="fromVersion">Версия, после которой начинается выборка.</param>
        /// <returns>Коллекция записей событий.</returns>
        public IEnumerable<StoredEvent> GetEventsFromVersion(int fromVersion)
        {
            return Events.Where(e => e.Metadata.Version > fromVersion);
        }

        // ---------- Снимки ----------

        /// <summary>
        /// Создаёт снимок текущего состояния агрегата (сериализованное состояние).
        /// Вызывается внешним кодом, который знает, как сериализовать агрегат.
        /// </summary>
        /// <param name="serializedAggregateState">Сериализованное состояние агрегата.</param>
        /// <returns>Объект снимка.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="serializedAggregateState"/> равен null.</exception>
        public Snapshot CreateSnapshot(byte[] serializedAggregateState)
        {
            ArgumentNullException.ThrowIfNull(serializedAggregateState, nameof(serializedAggregateState));

            return new Snapshot
            {
                AggregateId = AggregateId,
                Version = Version,
                Data = serializedAggregateState,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Применяет снимок: устанавливает версию и временную метку создания, если снимок новее.
        /// </summary>
        /// <param name="snapshot">Снимок, принадлежащий этому агрегату.</param>
        /// <exception cref="ArgumentException">Если снимок не принадлежит этому потоку.</exception>
        public void ApplySnapshot(Snapshot snapshot)
        {
            if (snapshot.AggregateId != AggregateId)
                throw new ArgumentException("Снимок не принадлежит этому потоку.", nameof(snapshot));

            Version = snapshot.Version;
            UpdatedAt = snapshot.CreatedAt;
            // События не очищаются, так как снимок не удаляет историю.
        }

        // ---------- Вспомогательные методы ----------

        /// <summary>
        /// Возвращает последнее событие потока или <c>null</c>, если поток пуст.
        /// </summary>
        public StoredEvent? GetLastEvent()
        {
            return Events.LastOrDefault();
        }

        /// <summary>Количество событий в потоке.</summary>
        public int EventCount => Events.Count;

        /// <summary>
        /// Создаёт новый пустой поток для агрегата.
        /// </summary>
        /// <param name="aggregateId">Идентификатор агрегата (не должен быть пустым).</param>
        /// <param name="aggregateType">Тип агрегата.</param>
        /// <returns>Новый экземпляр <see cref="EventStream"/>.</returns>
        /// <exception cref="ArgumentException">Если <paramref name="aggregateId"/> пуст или <paramref name="aggregateType"/> пуст.</exception>
        public static EventStream New(Guid aggregateId, string aggregateType)
        {
            if (aggregateId == Guid.Empty)
                throw new ArgumentException("Идентификатор агрегата не может быть пустым.", nameof(aggregateId));
            if (string.IsNullOrWhiteSpace(aggregateType))
                throw new ArgumentException("Тип агрегата не может быть пустым.", nameof(aggregateType));

            return new EventStream
            {
                AggregateId = aggregateId,
                AggregateType = aggregateType,
                Version = 0,
                CreatedAt = DateTime.UtcNow
            };
        }

        public override string ToString()
        {
            return $"EventStream({AggregateType}:{AggregateId} v{Version}, событий: {EventCount})";
        }
    }
}