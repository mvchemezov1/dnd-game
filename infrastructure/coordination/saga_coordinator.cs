#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.events;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.coordination
{
    /// <summary>
    /// Реестр фабрик саг. Связывает тип доменного события с функцией, создающей новый экземпляр саги.
    /// </summary>
    public interface ISagaRegistry
    {
        /// <summary>
        /// Регистрирует фабрику саги для указанного типа события.
        /// </summary>
        void Register<TEvent>(Func<TEvent, ISaga> factory) where TEvent : IDomainEvent;

        /// <summary>
        /// Возвращает все фабрики, реагирующие на данный тип события.
        /// </summary>
        IEnumerable<Func<IDomainEvent, ISaga>> GetFactoriesForEvent(Type eventType);
    }

    /// <summary>
    /// Реализация реестра саг на основе словаря. Не является потокобезопасной,
    /// регистрация должна выполняться до начала обработки событий.
    /// </summary>
    public class SagaRegistry : ISagaRegistry
    {
        private readonly Dictionary<Type, List<Func<IDomainEvent, ISaga>>> _factories = [];

        public void Register<TEvent>(Func<TEvent, ISaga> factory) where TEvent : IDomainEvent
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (!_factories.TryGetValue(typeof(TEvent), out var list))
            {
                list = [];
                _factories[typeof(TEvent)] = list;
            }
            list.Add(e => factory((TEvent)e));
        }

        public IEnumerable<Func<IDomainEvent, ISaga>> GetFactoriesForEvent(Type eventType)
        {
            ArgumentNullException.ThrowIfNull(eventType);
            if (_factories.TryGetValue(eventType, out var list))
                return list;
            return [];
        }
    }

    /// <summary>
    /// Координатор саг. При получении события находит соответствующие саги (существующие или новые),
    /// загружает/создаёт их состояние, обрабатывает событие и сохраняет изменения.
    /// </summary>
    public class SagaCoordinator(
        ISagaRegistry registry,
        ISagaStateRepository stateRepository,
        ICommandBus commandBus,
        IDistributedLockManager lockManager,
        ILogger<SagaCoordinator> logger) : ISagaDispatcher
    {
        private readonly ISagaRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        private readonly ISagaStateRepository _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly IDistributedLockManager _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        private readonly ILogger<SagaCoordinator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc />
        public async Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(@event);
            cancellationToken.ThrowIfCancellationRequested();

            var factories = _registry.GetFactoriesForEvent(@event.GetType());
            foreach (var factory in factories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var saga = factory(@event);
                if (saga == null)
                {
                    _logger.LogWarning("Фабрика для события {EventType} вернула null вместо саги.", @event.GetType().Name);
                    continue;
                }

                // Если сага умеет работать с командами, передаём ей шину команд
                if (saga is ICommandingSaga commandingSaga)
                {
                    commandingSaga.SetCommandBus(_commandBus);
                }

                // Пытаемся загрузить существующее состояние саги по SagaId.
                // Если сага новая, LoadState не вызывается, и она использует начальное состояние.
                var state = await _stateRepository.LoadAsync(saga.SagaId, cancellationToken);
                if (state != null)
                {
                    saga.LoadState(state);
                    _logger.LogDebug("Состояние саги {SagaId} загружено из репозитория.", saga.SagaId);
                }

                // Блокируем обработку события для данной саги, чтобы избежать гонок.
                // В качестве ключа блокировки используем CorrelationId (если есть) или SagaId.
                var correlationId = state?.CorrelationId ?? saga.SagaId;
                string lockKey = LockKeyFactory.ForSaga(correlationId);
                var lockHandle = await _lockManager.AcquireAsync(
                    lockKey,
                    LockMode.Exclusive,
                    ownerId: "saga-coordinator",
                    timeout: TimeSpan.FromSeconds(10),
                    cancellationToken);

                if (lockHandle == null)
                {
                    _logger.LogWarning("Не удалось захватить блокировку для саги {SagaId}. Событие пропущено.", saga.SagaId);
                    continue;
                }

                try
                {
                    await saga.Handle(@event, cancellationToken);
                    // Сохраняем изменённое состояние
                    saga.State.Version++; // увеличиваем версию перед сохранением (если поле доступно)
                    await _stateRepository.SaveAsync(saga.State, cancellationToken);
                    _logger.LogInformation("Сага {SagaId} успешно обработала событие {EventType}.", saga.SagaId, @event.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка обработки саги {SagaId} для события {EventType}.", saga.SagaId, @event.GetType().Name);

                    // Если сага поддерживает компенсацию, пытаемся откатить изменения
                    if (saga is ICompensatingSaga compensatingSaga)
                    {
                        try
                        {
                            _logger.LogInformation("Запуск компенсации для саги {SagaId}.", saga.SagaId);
                            await compensatingSaga.Compensate(cancellationToken);
                            saga.State.Status = SagaStatus.Compensated;
                            _logger.LogInformation("Компенсация саги {SagaId} успешно завершена.", saga.SagaId);
                        }
                        catch (Exception compEx)
                        {
                            _logger.LogError(compEx, "Ошибка компенсации саги {SagaId}.", saga.SagaId);
                            saga.State.Status = SagaStatus.Failed;
                        }
                    }
                    else
                    {
                        saga.State.Status = SagaStatus.Failed;
                    }
                    await _stateRepository.SaveAsync(saga.State, cancellationToken);
                }
                finally
                {
                    await lockHandle.DisposeAsync();
                }
            }
        }
    }
}