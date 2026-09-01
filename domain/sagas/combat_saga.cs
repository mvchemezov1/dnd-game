#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.commands;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.domain.sagas
{
    /// <summary>
    /// Сага, управляющая жизненным циклом боевой сцены в DnD:
    /// сбор инициативы, смена раундов и ходов, завершение боя при выполнении условий.
    /// Один экземпляр CombatSaga соответствует одному бою: SagaId = CombatId.
    /// </summary>
    public class CombatSaga : ISaga, ICommandingSaga
    {
        private ICommandBus _commandBus;
        private CombatSagaState _state;

        /// <summary>
        /// Создаёт экземпляр саги боя. Идентификатор саги совпадает с идентификатором боя.
        /// </summary>
        /// <param name="combatId">Идентификатор боя.</param>
        /// <param name="commandBus">Шина команд для отправки управляющих команд.</param>
        public CombatSaga(Guid combatId, ICommandBus commandBus)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _state = new CombatSagaState
            {
                SagaId = combatId,
                CorrelationId = combatId,
                CombatId = combatId,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <inheritdoc/>
        public void LoadState(ISagaState state)
        {
            _state = state as CombatSagaState
                     ?? throw new ArgumentException("Неверный тип состояния саги", nameof(state));
        }

        /// <inheritdoc/>
        public Guid SagaId => _state.SagaId;

        /// <inheritdoc/>
        public ISagaState State => _state;

        /// <inheritdoc/>
        public async Task Handle(IDomainEvent @event, CancellationToken cancellationToken = default)
        {
            switch (@event)
            {
                case CombatStarted combatStarted:
                    await OnCombatStarted(combatStarted, cancellationToken);
                    break;

                case InitiativeRolled initiativeRolled:
                    await OnInitiativeRolled(initiativeRolled, cancellationToken);
                    break;

                case CombatRoundStarted roundStarted:
                    await OnRoundStarted(roundStarted, cancellationToken);
                    break;

                case CombatTurnEnded turnEnded:
                    await OnTurnEnded(turnEnded, cancellationToken);
                    break;

                case CharacterDied characterDied:
                    await OnCharacterDied(characterDied, cancellationToken);
                    break;

                case ParticipantRemovedFromCombat participantRemoved:
                    await OnParticipantRemoved(participantRemoved, cancellationToken);
                    break;

                // Другие события при необходимости можно добавить
                default:
                    break;
            }
        }

        /// <inheritdoc/>
        public async Task Complete(bool success, string? reason = null, CancellationToken cancellationToken = default)
        {
            if (!_state.IsActive)
                return;

            _state.IsActive = false;
            _state.Status = success ? SagaStatus.Completed : SagaStatus.Failed;
            _state.CompletionReason = reason;

            // Отправляем команду завершения боя
            await SendCommand(new EndCombat(_state.CombatId), cancellationToken);
        }

        /// <inheritdoc/>
        public async Task SendCommand(ICommand command, CancellationToken cancellationToken = default)
        {
            await _commandBus.SendAsync(command, new CommandContext { CancellationToken = cancellationToken });
        }

        /// <inheritdoc/>
        public void SetCommandBus(ICommandBus commandBus)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        }

        // ---------- Приватные методы-реакции на события ----------

        private async Task OnCombatStarted(CombatStarted e, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state.CombatId = e.CombatId;
            _state.Participants = e.Participants.ToDictionary(
                id => id,
                id => new CombatSagaParticipant { CharacterId = id });
            _state.PlayerCharacterIds = e.PlayerCharacterIds?.ToHashSet() ?? [];
            _state.IsActive = true;
            _state.Status = SagaStatus.Started;

            await Task.CompletedTask;
        }

        private async Task OnInitiativeRolled(InitiativeRolled e, CancellationToken cancellationToken)
        {
            if (_state.CombatId != e.CombatId)
                return;

            if (_state.Participants.TryGetValue(e.CharacterId, out var participant))
            {
                participant.Initiative = e.Initiative;
                participant.DexterityModifier = e.DexterityModifier;
                participant.HasRolledInitiative = true;
            }

            // Проверяем, все ли бросили инициативу
            if (_state.Participants.Values.All(p => p.HasRolledInitiative))
            {
                await SendCommand(new StartRound(_state.CombatId), cancellationToken);
            }
        }

        private async Task OnRoundStarted(CombatRoundStarted e, CancellationToken cancellationToken)
        {
            if (_state.CombatId != e.CombatId)
                return;

            _state.Round = e.Round;
            _state.CurrentTurnIndex = 0;

            // Сортируем участников по убыванию инициативы, затем по модификатору ловкости
            var sorted = _state.Participants.Values
                .OrderByDescending(p => p.Initiative)
                .ThenByDescending(p => p.DexterityModifier)
                .Select(p => p.CharacterId)
                .ToList();
            _state.TurnOrder = sorted;

            // Начинаем первый ход
            if (sorted.Count > 0)
                await SendCommand(new NextTurn(_state.CombatId), cancellationToken);
        }

        private async Task OnTurnEnded(CombatTurnEnded e, CancellationToken cancellationToken)
        {
            if (_state.CombatId != e.CombatId)
                return;

            int nextIndex = _state.CurrentTurnIndex + 1;
            if (nextIndex < _state.TurnOrder.Count)
            {
                _state.CurrentTurnIndex = nextIndex;
                await SendCommand(new NextTurn(_state.CombatId), cancellationToken);
            }
            else
            {
                // Конец раунда
                await SendCommand(new EndRound(_state.CombatId), cancellationToken);
                // Если бой ещё активен, начинаем следующий раунд
                if (_state.IsActive)
                    await SendCommand(new StartRound(_state.CombatId), cancellationToken);
            }
        }

        private async Task OnCharacterDied(CharacterDied e, CancellationToken cancellationToken)
        {
            if (_state.Participants.ContainsKey(e.CharacterId))
            {
                await SendCommand(new RemoveParticipantFromCombat(_state.CombatId, e.CharacterId), cancellationToken);
            }
        }

        private async Task OnParticipantRemoved(ParticipantRemovedFromCombat e, CancellationToken cancellationToken)
        {
            if (_state.CombatId != e.CombatId)
                return;

            _state.Participants.Remove(e.CharacterId);

            // Проверяем условие завершения боя
            if (IsCombatOver())
            {
                await Complete(true, "Все противники побеждены", cancellationToken);
            }
            else if (_state.Participants.Count == 0)
            {
                await Complete(true, "Не осталось участников", cancellationToken);
            }
        }

        /// <summary>
        /// Проверяет, завершён ли бой: все оставшиеся участники принадлежат одной стороне.
        /// </summary>
        private bool IsCombatOver()
        {
            if (_state.Participants.Count == 0)
                return true;

            bool hasPlayers = _state.Participants.Values.Any(p => _state.PlayerCharacterIds.Contains(p.CharacterId));
            bool hasEnemies = _state.Participants.Values.Any(p => !_state.PlayerCharacterIds.Contains(p.CharacterId));

            // Бой окончен, если не осталось игроков или не осталось врагов
            return !hasPlayers || !hasEnemies;
        }

        // ---------- Внутренние классы состояния ----------

        private class CombatSagaState : ISagaState
        {
            public Guid SagaId { get; set; }
            public Guid CorrelationId { get; set; }
            public SagaStatus Status { get; set; }
            public int Version { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }

            public Guid CombatId { get; set; }
            public bool IsActive { get; set; }
            public int Round { get; set; }
            public int CurrentTurnIndex { get; set; }
            public List<Guid> TurnOrder { get; set; } = [];
            public Dictionary<Guid, CombatSagaParticipant> Participants { get; set; } = [];
            public HashSet<Guid> PlayerCharacterIds { get; set; } = [];
            public string? CompletionReason { get; set; }
        }

        private class CombatSagaParticipant
        {
            public Guid CharacterId { get; set; }
            public int Initiative { get; set; }
            public int DexterityModifier { get; set; }
            public bool HasRolledInitiative { get; set; }
        }
    }
}