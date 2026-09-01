#nullable enable
using dnd_game.application.projections;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.domain.sagas
{
    /// <summary>
    /// Сага торговой сделки. Управляет полным жизненным циклом обмена между двумя персонажами:
    /// создание, принятие, отклонение, отмена и компенсация (откат) при сбое.
    /// Один экземпляр саги соответствует одной сделке: SagaId = OfferId.
    /// </summary>
    public class TradeSaga : ISaga, ICompensatingSaga
    {
        private readonly ICommandBus _commandBus;
        private readonly IEventBus _eventBus;
        private readonly CharacterProjection _characterProjection;
        private readonly ILogger<TradeSaga> _logger;
        private TradeSagaState _state;

        /// <summary>
        /// Создаёт экземпляр саги торговли.
        /// </summary>
        /// <param name="offerId">Идентификатор торгового предложения.</param>
        /// <param name="commandBus">Шина команд.</param>
        /// <param name="eventBus">Шина событий.</param>
        /// <param name="characterProjection">Проекция персонажей для чтения данных.</param>
        /// <param name="logger">Логгер (необязательный).</param>
        public TradeSaga(
            Guid offerId,
            ICommandBus commandBus,
            IEventBus eventBus,
            CharacterProjection characterProjection,
            ILogger<TradeSaga>? logger = null)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
            _logger = logger ?? NullLogger<TradeSaga>.Instance;

            _state = new TradeSagaState
            {
                SagaId = offerId,
                CorrelationId = offerId,
                OfferId = offerId,
                Status = SagaStatus.Started,
                TradeStatus = TradeSagaStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
        }

        public Guid SagaId => _state.SagaId;
        public ISagaState State => _state;

        /// <inheritdoc/>
        public void LoadState(ISagaState state)
        {
            _state = state as TradeSagaState
                     ?? throw new ArgumentException("Неверный тип состояния саги", nameof(state));
        }

        /// <inheritdoc/>
        public async Task Handle(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            switch (@event)
            {
                case TradeOfferCreated created:
                    OnTradeOfferCreated(created);
                    break;

                case TradeOfferAccepted accepted:
                    await OnTradeOfferAccepted(accepted, cancellationToken);
                    break;

                case TradeOfferDeclined declined:
                    OnTradeOfferDeclined(declined);
                    break;

                case TradeOfferCancelled cancelled:
                    OnTradeOfferCancelled(cancelled);
                    break;

                default:
                    break;
            }
        }

        /// <inheritdoc/>
        public Task Complete(bool success, string? reason = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state.Status = success ? SagaStatus.Completed : SagaStatus.Failed;
            if (!success) _state.FailureReason = reason;
            _state.UpdatedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task Compensate(CancellationToken cancellationToken = default)
        {
            if (_state.IsCompensated)
                return;

            _logger.LogInformation("Запуск компенсации для сделки {OfferId}", _state.OfferId);
            await CompensateTradeAsync(cancellationToken);
            _state.IsCompensated = true;
            _state.Status = SagaStatus.Compensated;
            _state.UpdatedAt = DateTime.UtcNow;
        }

        // --------------------------------------------------------------------------------------------
        // Обработчики событий
        // --------------------------------------------------------------------------------------------

        private void OnTradeOfferCreated(TradeOfferCreated e)
        {
            _state.FromCharacterId = e.FromCharacterId;
            _state.ToCharacterId = e.ToCharacterId;
            _state.OfferedItems = e.OfferedItems;
            _state.OfferedGold = e.OfferedGold;
            _state.RequestedItems = e.RequestedItems;
            _state.RequestedGold = e.RequestedGold;
            _state.TradeStatus = TradeSagaStatus.Pending;
            _state.Status = SagaStatus.Started;
            _state.CreatedAt = e.OccurredOn;
            _state.UpdatedAt = DateTime.UtcNow;
        }

        private async Task OnTradeOfferAccepted(TradeOfferAccepted e, CancellationToken ct)
        {
            // Проверяем, что событие относится к той же сделке, которой управляет сага
            if (_state.OfferId != e.OfferId)
            {
                _logger.LogWarning("Сага {SagaId} получила событие для сделки {EventOfferId}, ожидалась {StateOfferId}",
                    _state.SagaId, e.OfferId, _state.OfferId);
                return;
            }

            if (_state.TradeStatus != TradeSagaStatus.Pending)
            {
                _logger.LogWarning("Попытка принять сделку {OfferId}, которая не в ожидании", _state.OfferId);
                return;
            }

            _state.TradeStatus = TradeSagaStatus.InProgress;
            _state.Status = SagaStatus.InProgress;
            _state.UpdatedAt = DateTime.UtcNow;

            try
            {
                await ValidateResourcesAsync(ct);

                // Шаг 1: списываем предложенные предметы и золото у инициатора
                await DebitAsync(_state.FromCharacterId, _state.OfferedItems, _state.OfferedGold, TradeStep.DebitedFrom, ct);

                // Шаг 2: списываем запрошенные предметы и золото у получателя
                await DebitAsync(_state.ToCharacterId, _state.RequestedItems, _state.RequestedGold, TradeStep.DebitedTo, ct);

                // Шаг 3: начисляем предложенное получателю
                await CreditAsync(_state.ToCharacterId, _state.OfferedItems, _state.OfferedGold, TradeStep.CreditedTo, ct);

                // Шаг 4: начисляем запрошенное инициатору
                await CreditAsync(_state.FromCharacterId, _state.RequestedItems, _state.RequestedGold, TradeStep.CreditedFrom, ct);

                _state.TradeStatus = TradeSagaStatus.Completed;
                _state.Status = SagaStatus.Completed;
                _state.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Сделка {OfferId} успешно завершена", _state.OfferId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Выполнение сделки {OfferId} отменено", _state.OfferId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения сделки {OfferId}. Запускаем компенсацию", _state.OfferId);

                if (!_state.IsCompensated)
                {
                    await Compensate(ct);
                }

                _state.TradeStatus = TradeSagaStatus.Failed;
                _state.Status = SagaStatus.Failed;
                _state.FailureReason = ex.Message;
                _state.UpdatedAt = DateTime.UtcNow;

                // Публикуем событие о неудаче
                await _eventBus.PublishAsync(new TradeFailed(_state.OfferId, ex.Message), ct);
            }
        }

        private void OnTradeOfferDeclined(TradeOfferDeclined e)
        {
            _state.TradeStatus = TradeSagaStatus.Declined;
            _state.Status = SagaStatus.Cancelled;
            _state.UpdatedAt = e.OccurredOn;
        }

        private void OnTradeOfferCancelled(TradeOfferCancelled e)
        {
            _state.TradeStatus = TradeSagaStatus.Cancelled;
            _state.Status = SagaStatus.Cancelled;
            _state.UpdatedAt = e.OccurredOn;
        }

        // --------------------------------------------------------------------------------------------
        // Вспомогательные методы выполнения шагов
        // --------------------------------------------------------------------------------------------

        private async Task ValidateResourcesAsync(CancellationToken ct)
        {
            var fromChar = await _characterProjection.GetById(_state.FromCharacterId, ct)
                ?? throw new InvalidOperationException("Персонаж-инициатор не найден.");
            var toChar = await _characterProjection.GetById(_state.ToCharacterId, ct)
                ?? throw new InvalidOperationException("Персонаж-получатель не найден.");

            // Проверяем ресурсы инициатора
            foreach (var item in _state.OfferedItems)
            {
                var invItem = fromChar.Inventory.FirstOrDefault(i => i.ItemId == item.ItemId);
                if (invItem == null || invItem.Quantity < item.Quantity)
                    throw new InvalidOperationException($"У инициатора недостаточно предмета «{item.ItemName}».");
            }
            if (fromChar.Gold < _state.OfferedGold)
                throw new InvalidOperationException("У инициатора недостаточно золота.");

            // Проверяем ресурсы получателя
            foreach (var item in _state.RequestedItems)
            {
                var invItem = toChar.Inventory.FirstOrDefault(i => i.ItemId == item.ItemId);
                if (invItem == null || invItem.Quantity < item.Quantity)
                    throw new InvalidOperationException($"У получателя недостаточно предмета «{item.ItemName}».");
            }
            if (toChar.Gold < _state.RequestedGold)
                throw new InvalidOperationException("У получателя недостаточно золота.");
        }

        private async Task DebitAsync(
            Guid characterId,
            List<TradeItem> items,
            int gold,
            TradeStep step,
            CancellationToken ct)
        {
            foreach (var item in items)
            {
                await _commandBus.SendAsync(
                    new RemoveInventoryItem(characterId, item.ItemId, item.Quantity),
                    new CommandContext { CancellationToken = ct });
            }
            if (gold > 0)
            {
                await _commandBus.SendAsync(
                    new SpendGold(characterId, gold),
                    new CommandContext { CancellationToken = ct });
            }

            _state.CompletedSteps.Add(step);
            _logger.LogDebug("Списывание у персонажа {CharacterId} выполнено (шаг {Step})", characterId, step);
        }

        private async Task CreditAsync(
            Guid characterId,
            List<TradeItem> items,
            int gold,
            TradeStep step,
            CancellationToken ct)
        {
            foreach (var item in items)
            {
                await _commandBus.SendAsync(
                    new AddInventoryItem(characterId, item.ItemId, item.ItemName, item.Quantity),
                    new CommandContext { CancellationToken = ct });
            }
            if (gold > 0)
            {
                await _commandBus.SendAsync(
                    new AddGold(characterId, gold),
                    new CommandContext { CancellationToken = ct });
            }

            _state.CompletedSteps.Add(step);
            _logger.LogDebug("Начисление персонажу {CharacterId} выполнено (шаг {Step})", characterId, step);
        }

        /// <summary>
        /// Выполняет компенсацию выполненных шагов в обратном порядке.
        /// Возвращает ресурсы, которые были списаны, но не переданы.
        /// </summary>
        private async Task CompensateTradeAsync(CancellationToken ct)
        {
            // Проходим шаги в обратном порядке
            var steps = _state.CompletedSteps.OrderByDescending(s => (int)s).ToList();
            foreach (var step in steps)
            {
                switch (step)
                {
                    case TradeStep.CreditedFrom:
                        // Начисление инициатору уже произошло, откатываем: списываем обратно
                        await DebitAsync(_state.FromCharacterId, _state.RequestedItems, _state.RequestedGold, TradeStep.CompensatedCreditedFrom, ct);
                        break;

                    case TradeStep.CreditedTo:
                        // Начисление получателю уже произошло, откатываем: списываем обратно
                        await DebitAsync(_state.ToCharacterId, _state.OfferedItems, _state.OfferedGold, TradeStep.CompensatedCreditedTo, ct);
                        break;

                    case TradeStep.DebitedTo:
                        // Списание у получателя произошло, возвращаем
                        await CreditAsync(_state.ToCharacterId, _state.RequestedItems, _state.RequestedGold, TradeStep.CompensatedDebitedTo, ct);
                        break;

                    case TradeStep.DebitedFrom:
                        // Списание у инициатора произошло, возвращаем
                        await CreditAsync(_state.FromCharacterId, _state.OfferedItems, _state.OfferedGold, TradeStep.CompensatedDebitedFrom, ct);
                        break;

                    // Компенсационные шаги не требуют дальнейших действий
                    case TradeStep.CompensatedDebitedFrom:
                    case TradeStep.CompensatedDebitedTo:
                    case TradeStep.CompensatedCreditedFrom:
                    case TradeStep.CompensatedCreditedTo:
                        break;
                }
            }

            _state.CompletedSteps.Clear();
            _logger.LogInformation("Компенсация сделки {OfferId} завершена", _state.OfferId);
        }

        // --------------------------------------------------------------------------------------------
        // Внутренние классы и перечисления
        // --------------------------------------------------------------------------------------------

        private enum TradeSagaStatus
        {
            Pending,
            InProgress,
            Completed,
            Failed,
            Declined,
            Cancelled
        }

        private enum TradeStep
        {
            DebitedFrom = 1,
            DebitedTo = 2,
            CreditedTo = 3,
            CreditedFrom = 4,
            // Компенсационные шаги не должны попадать в список CompletedSteps,
            // но для симметрии можно оставить
            CompensatedDebitedFrom = 101,
            CompensatedDebitedTo = 102,
            CompensatedCreditedFrom = 103,
            CompensatedCreditedTo = 104
        }

        private class TradeSagaState : ISagaState
        {
            public Guid SagaId { get; set; }
            public Guid CorrelationId { get; set; }
            public SagaStatus Status { get; set; } = SagaStatus.Started;
            public int Version { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? UpdatedAt { get; set; }

            public Guid OfferId { get; set; }
            public Guid FromCharacterId { get; set; }
            public Guid ToCharacterId { get; set; }
            public List<TradeItem> OfferedItems { get; set; } = [];
            public int OfferedGold { get; set; }
            public List<TradeItem> RequestedItems { get; set; } = [];
            public int RequestedGold { get; set; }
            public TradeSagaStatus TradeStatus { get; set; } = TradeSagaStatus.Pending;
            public string? FailureReason { get; set; }
            public bool IsCompensated { get; set; }

            /// <summary>
            /// Множество выполненных шагов сделки для корректной компенсации.
            /// </summary>
            public HashSet<TradeStep> CompletedSteps { get; set; } = [];
        }
    }
}