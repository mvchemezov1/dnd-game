#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.aggregates;
using dnd_game.domain.events;

namespace dnd_game.infrastructure.event_store
{
    /// <summary>
    /// Метаданные события, сохраняемые вместе с событием в хранилище.
    /// </summary>
    public class EventMetadata
    {
        /// <summary>Уникальный идентификатор события.</summary>
        public Guid EventId { get; set; } = Guid.NewGuid();

        /// <summary>Тип события (полное имя класса).</summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>Идентификатор агрегата, к которому относится событие.</summary>
        public Guid AggregateId { get; set; }

        /// <summary>Версия агрегата после применения этого события.</summary>
        public int Version { get; set; }

        /// <summary>Время возникновения события (UTC).</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Идентификатор пользователя, инициировавшего событие (если применимо).</summary>
        public Guid UserId { get; set; }

        /// <summary>Идентификатор игровой сессии (кампании), в рамках которой произошло событие.</summary>
        public Guid GameSessionId { get; set; }

        /// <summary>Произвольные дополнительные заголовки.</summary>
        public Dictionary<string, string>? CustomHeaders { get; set; }

        /// <summary>Тип агрегата (полное имя класса агрегата).</summary>
        public string AggregateType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запись хранимого события: доменное событие + метаданные.
    /// </summary>
    public class StoredEvent
    {
        /// <summary>Доменное событие.</summary>
        public IDomainEvent DomainEvent { get; set; } = null!;

        /// <summary>Метаданные события.</summary>
        public EventMetadata Metadata { get; set; } = null!;
    }

    /// <summary>
    /// Снимок состояния агрегата для ускорения восстановления.
    /// </summary>
    public class Snapshot
    {
        /// <summary>Идентификатор агрегата.</summary>
        public Guid AggregateId { get; set; }

        /// <summary>Версия агрегата на момент снимка.</summary>
        public int Version { get; set; }

        /// <summary>Сериализованное состояние агрегата.</summary>
        public byte[] Data { get; set; } = [];

        /// <summary>Время создания снимка (UTC).</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Параметры чтения потока событий.
    /// </summary>
    public class ReadStreamOptions
    {
        /// <summary>Начать чтение с указанной версии (по умолчанию с начала).</summary>
        public int FromVersion { get; set; } = 0;

        /// <summary>Максимальное количество событий для чтения (null — без ограничения).</summary>
        public int? MaxCount { get; set; }

        /// <summary>Читать события в обратном порядке (сначала новые).</summary>
        public bool ReadBackwards { get; set; } = false;

        /// <summary>Фильтр по типу события (полное имя класса).</summary>
        public string? EventTypeFilter { get; set; }

        /// <summary>Фильтр по времени возникновения (включительно).</summary>
        public DateTime? FromTimestamp { get; set; }
    }

    /// <summary>
    /// Расширенный интерфейс Event Store, адаптированный к требованиям DnD.
    /// Предоставляет операции для хранения и загрузки агрегатов, событий, снимков
    /// и подписки на новые события.
    /// </summary>
    public interface IEventStore
    {
        // ---------- Базовые операции ----------

        /// <summary>
        /// Получить все события из хранилища (не рекомендуется для production,
        /// используется при восстановлении проекций или отладке).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Коллекция объектов событий (может содержать <see cref="StoredEvent"/> или <see cref="IDomainEvent"/>).</returns>
        Task<IEnumerable<object>> GetAllEvents(CancellationToken cancellationToken = default);

        /// <summary>
        /// Сохранить все несохранённые события агрегата.
        /// </summary>
        /// <typeparam name="T">Тип агрегата, производный от <see cref="AggregateRoot"/>.</typeparam>
        /// <param name="aggregate">Агрегат с накопленными событиями.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task Save<T>(T aggregate, CancellationToken cancellationToken = default) where T : AggregateRoot, new();

        /// <summary>
        /// Загрузить агрегат по его идентификатору, восстановив состояние из истории событий.
        /// </summary>
        /// <typeparam name="T">Тип агрегата.</typeparam>
        /// <param name="aggregateId">Идентификатор агрегата.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Агрегат или <c>null</c>, если не найден.</returns>
        Task<T?> Load<T>(Guid aggregateId, CancellationToken cancellationToken = default) where T : AggregateRoot, new();

        /// <summary>
        /// Получить список событий агрегата, начиная с указанной версии.
        /// </summary>
        /// <param name="aggregateId">Идентификатор агрегата.</param>
        /// <param name="fromVersion">Версия, с которой начинать чтение.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Коллекция объектов событий.</returns>
        Task<IEnumerable<object>> GetEvents(Guid aggregateId, int fromVersion = 0, CancellationToken cancellationToken = default);

        // ---------- Сохранение с метаданными ----------

        /// <summary>
        /// Сохранить несохранённые события агрегата с заданными метаданными.
        /// </summary>
        /// <typeparam name="T">Тип агрегата.</typeparam>
        /// <param name="aggregate">Агрегат.</param>
        /// <param name="metadata">Метаданные для всех сохраняемых событий.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task SaveWithMetadata<T>(T aggregate, EventMetadata metadata, CancellationToken cancellationToken = default) where T : AggregateRoot, new();

        // ---------- Чтение с метаданными и фильтрацией ----------

        /// <summary>
        /// Загрузить агрегат вместе с метаданными событий (если реализация поддерживает).
        /// </summary>
        /// <typeparam name="T">Тип агрегата.</typeparam>
        /// <param name="aggregateId">Идентификатор агрегата.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Агрегат или <c>null</c>.</returns>
        Task<T?> LoadWithMetadata<T>(Guid aggregateId, CancellationToken cancellationToken = default) where T : AggregateRoot, new();

        /// <summary>
        /// Получить поток событий агрегата с метаданными.
        /// </summary>
        /// <param name="aggregateId">Идентификатор агрегата.</param>
        /// <param name="options">Параметры чтения (фильтры, направление, количество).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Коллекция записей событий.</returns>
        Task<IEnumerable<StoredEvent>> GetEventStreamAsync(
            Guid aggregateId,
            ReadStreamOptions? options = null,
            CancellationToken cancellationToken = default);

        // ---------- Поддержка снимков ----------

        /// <summary>
        /// Сохранить снимок состояния агрегата.
        /// </summary>
        /// <param name="snapshot">Снимок.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task SaveSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// Получить последний снимок агрегата с версией не выше указанной.
        /// </summary>
        /// <param name="aggregateId">Идентификатор агрегата.</param>
        /// <param name="maxVersion">Максимально допустимая версия снимка.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Снимок или <c>null</c>, если отсутствует.</returns>
        Task<Snapshot?> GetLatestSnapshotAsync(
            Guid aggregateId,
            int maxVersion,
            CancellationToken cancellationToken = default);

        // ---------- Глобальные запросы ----------

        /// <summary>
        /// Получить все события определённого типа за заданный период.
        /// </summary>
        /// <param name="eventType">Тип события (полное имя класса).</param>
        /// <param name="from">Начало периода (UTC).</param>
        /// <param name="to">Конец периода (UTC).</param>
        /// <param name="maxCount">Максимальное количество событий.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Коллекция записей событий.</returns>
        Task<IEnumerable<StoredEvent>> GetEventsByTypeAsync(
            string eventType,
            DateTime? from = null,
            DateTime? to = null,
            int? maxCount = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Получить все события, относящиеся к указанной игровой сессии (кампании).
        /// </summary>
        /// <param name="gameSessionId">Идентификатор сессии.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Коллекция записей событий.</returns>
        Task<IEnumerable<StoredEvent>> GetEventsBySessionAsync(
            Guid gameSessionId,
            CancellationToken cancellationToken = default);

        // ---------- Управление версиями ----------

        /// <summary>
        /// Получить текущую версию агрегата (номер последнего события).
        /// </summary>
        /// <param name="aggregateId">Идентификатор агрегата.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Версия или 0, если событий нет.</returns>
        Task<int> GetCurrentVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default);

        // ---------- Потоковая подписка ----------

        /// <summary>
        /// Подписаться на все новые события, записываемые в Event Store.
        /// Обработчик вызывается для каждого события после его сохранения.
        /// </summary>
        /// <param name="handler">Асинхронный обработчик события.</param>
        /// <param name="cancellationToken">Токен отмены подписки.</param>
        Task SubscribeAsync(
            Func<StoredEvent, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default);
    }
}