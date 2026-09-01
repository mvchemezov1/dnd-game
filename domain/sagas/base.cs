#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.domain.sagas
{
    /// <summary>
    /// Состояние саги, используемое для сохранения прогресса длительного процесса.
    /// </summary>
    public interface ISagaState
    {
        /// <summary>Идентификатор экземпляра саги.</summary>
        Guid SagaId { get; }

        /// <summary>Идентификатор корреляции (например, идентификатор боя или торговой сделки).</summary>
        Guid CorrelationId { get; }

        /// <summary>Текущий статус саги.</summary>
        SagaStatus Status { get; set; }

        /// <summary>Версия состояния для оптимистической блокировки.</summary>
        int Version { get; set; }

        /// <summary>Дата и время создания саги.</summary>
        DateTime CreatedAt { get; }

        /// <summary>Дата и время последнего изменения.</summary>
        DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Статус жизненного цикла саги.
    /// </summary>
    public enum SagaStatus
    {
        Started,
        InProgress,
        Completed,
        Failed,
        Compensating,
        Compensated,
        Cancelled
    }

    /// <summary>
    /// Интерфейс саги, способной обрабатывать доменные события и управлять состоянием.
    /// </summary>
    public interface ISaga
    {
        /// <summary>Идентификатор экземпляра саги.</summary>
        Guid SagaId { get; }

        /// <summary>Текущее состояние саги.</summary>
        ISagaState State { get; }

        /// <summary>Загружает состояние саги из хранилища.</summary>
        void LoadState(ISagaState state);

        /// <summary>Обрабатывает доменное событие, продвигая сагу вперёд.</summary>
        Task Handle(IDomainEvent @event, CancellationToken cancellationToken = default);

        /// <summary>Завершает сагу с указанием успешности и причины (для провала).</summary>
        Task Complete(bool success, string? reason = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Сага, способная отправлять команды для выполнения шагов процесса.
    /// </summary>
    public interface ICommandingSaga : ISaga
    {
        /// <summary>Отправляет команду через шину команд.</summary>
        Task SendCommand(ICommand command, CancellationToken cancellationToken = default);

        /// <summary>Внедряет шину команд для использования в саге.</summary>
        void SetCommandBus(ICommandBus commandBus);
    }

    /// <summary>
    /// Сага, поддерживающая компенсационные действия (откат) в случае сбоя.
    /// </summary>
    public interface ICompensatingSaga : ISaga
    {
        /// <summary>Запускает процесс компенсации (отката).</summary>
        Task Compensate(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Хранилище состояний саг (персистентность).
    /// </summary>
    public interface ISagaStateRepository
    {
        /// <summary>Загружает состояние саги по идентификатору.</summary>
        Task<ISagaState?> LoadAsync(Guid sagaId, CancellationToken cancellationToken = default);

        /// <summary>Сохраняет состояние саги.</summary>
        Task SaveAsync(ISagaState state, CancellationToken cancellationToken = default);

        /// <summary>Удаляет состояние саги.</summary>
        Task DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default);
        /// <summary>
        /// Пытается сохранить состояние саги с проверкой ожидаемой версии.
        /// Возвращает false, если версия в хранилище не совпадает с expectedVersion.
        /// </summary>
        Task<bool> TrySaveAsync(ISagaState state, int expectedVersion, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Диспетчер саг: связывает доменные события с соответствующими экземплярами саг.
    /// </summary>
    public interface ISagaDispatcher
    {
        /// <summary>Направляет событие всем заинтересованным сагам.</summary>
        Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
    }
}