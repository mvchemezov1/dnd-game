using System;
using System.Collections.Generic;
using dnd_game.domain.events;

namespace dnd_game.domain.aggregates
{
    /// <summary>
    /// Базовый класс для всех агрегатов, использующих событийно-ориентированное восстановление состояния.
    /// Предоставляет механизмы версионирования, накопления несохранённых событий,
    /// проверки инвариантов и оптимистической блокировки.
    /// </summary>
    public abstract class AggregateRoot
    {
        private readonly List<IDomainEvent> _uncommittedEvents = [];

        /// <summary>Идентификатор агрегата.</summary>
        public Guid Id { get; protected set; }

        /// <summary>Текущая версия агрегата (количество применённых событий).</summary>
        public int Version { get; protected set; }

        /// <summary>
        /// Версия агрегата, с которой он был загружен из хранилища.
        /// Используется для проверки оптимистической блокировки при сохранении.
        /// </summary>
        public int OriginalVersion { get; private set; }

        /// <summary>
        /// Устанавливает версию агрегата. Вызывается при загрузке из EventStore.
        /// </summary>
        public void SetVersion(int version)
        {
            Version = version;
            OriginalVersion = version;
        }

        // --------------------------------------------------------------------------------------------
        // Применение событий
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Применяет событие к агрегату: обновляет состояние, проверяет инварианты
        /// и добавляет событие в список несохранённых.
        /// </summary>
        public void ApplyChange(IDomainEvent @event)
        {
            ApplyEvent(@event);      // изменяет состояние агрегата
            EnsureInvariants();      // проверка целостности после изменения
            _uncommittedEvents.Add(@event);
            Version++;
        }

        /// <summary>
        /// Абстрактный метод, реализующий мутацию состояния для конкретного типа события.
        /// Вызывается как при первоначальном применении, так и при восстановлении из истории.
        /// </summary>
        protected abstract void ApplyEvent(IDomainEvent @event);

        /// <summary>
        /// Проверка инвариантов агрегата (соответствие правилам DnD).
        /// По умолчанию пустая; конкретные агрегаты переопределяют для своих проверок.
        /// Например: хиты не отрицательны, уровень ≤ 20, использованные ячейки ≤ максимума.
        /// </summary>
        public virtual void EnsureInvariants()
        {
        }

        // --------------------------------------------------------------------------------------------
        // Восстановление состояния из истории событий
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Восстанавливает состояние агрегата из списка событий (при загрузке из EventStore).
        /// После восстановления проверяются инварианты.
        /// </summary>
        public void LoadFromHistory(IEnumerable<IDomainEvent> history)
        {
            foreach (var @event in history)
            {
                ApplyEvent(@event);
                Version++;
            }
            OriginalVersion = Version;
            EnsureInvariants();
        }

        // --------------------------------------------------------------------------------------------
        // Работа с несохранёнными событиями
        // --------------------------------------------------------------------------------------------

        /// <summary>Возвращает список событий, которые ещё не были сохранены в EventStore.</summary>
        public IReadOnlyCollection<IDomainEvent> GetUncommittedEvents() => _uncommittedEvents;

        /// <summary>Очищает список несохранённых событий (вызывается после успешного сохранения).</summary>
        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
            OriginalVersion = Version;
        }

        // --------------------------------------------------------------------------------------------
        // Оптимистическая блокировка
        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// Проверяет, что агрегат не был изменён с момента загрузки.
        /// Если ожидаемая версия не совпадает с исходной, возвращает true.
        /// </summary>
        public bool HasConcurrencyConflict(int expectedVersion)
        {
            return OriginalVersion != expectedVersion;
        }

        // --------------------------------------------------------------------------------------------
        // Удаление агрегата
        // --------------------------------------------------------------------------------------------

        /// <summary>Признак того, что агрегат помечен как удалённый.</summary>
        public bool IsDeleted { get; private set; }

        /// <summary>
        /// Помечает агрегат как удалённый, применяя соответствующее событие.
        /// </summary>
        protected void MarkAsDeleted()
        {
            var @event = new AggregateDeleted(Id, DateTime.UtcNow);
            ApplyChange(@event);
            IsDeleted = true;
        }

        /// <summary>
        /// Внутреннее событие, сигнализирующее об удалении агрегата.
        /// </summary>
        public class AggregateDeleted(Guid aggregateId, DateTime occurredOn) : IDomainEvent
        {
            public Guid EventId { get; } = Guid.NewGuid();
            public DateTime OccurredOn { get; } = occurredOn;
            public Guid AggregateId { get; } = aggregateId;
        }
    }
}