#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.undo;

namespace dnd_game.presentation.dm_tools
{
    /// <summary>
    /// Инструмент Мастера для управления отменой и повтором действий.
    /// Обёртка над <see cref="UndoManager"/> из инфраструктуры, адаптированная для DM UI.
    /// </summary>
    public sealed class DmUndoManager(
        UndoManager undoManager,
        ICommandBus commandBus,
        PermissionChecker permissionChecker,
        ILogger<DmUndoManager> logger)
    {
        private readonly UndoManager _undoManager = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        private readonly ILogger<DmUndoManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Выполняет команду и регистрирует её в стеке отмены, если для неё есть адаптер.
        /// </summary>
        /// <param name="command">Команда для выполнения.</param>
        /// <param name="userId">Идентификатор пользователя, выполнившего команду.</param>
        /// <param name="sessionId">Идентификатор игровой сессии.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task RecordAndExecuteAsync(
            ICommand command,
            Guid userId,
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            ValidateUserId(userId);
            ValidateSessionId(sessionId);
            cancellationToken.ThrowIfCancellationRequested();

            // Выполняем исходную команду
            await _commandBus.SendAsync(command, new CommandContext
            {
                UserId = userId,
                GameSessionId = sessionId,
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            // Пытаемся создать undoable action
            var undoable = CreateUndoableAction(command, userId, sessionId);
            if (undoable != null)
            {
                await _undoManager.RecordActionAsync(undoable).ConfigureAwait(false);
                _logger.LogDebug("Зарегистрировано действие для отмены: {Description}", undoable.Description);
            }
            else
            {
                _logger.LogDebug("Команда {CommandType} не поддерживает отмену; не записана в историю.",
                    command.GetType().Name);
            }
        }

        /// <summary>
        /// Отменяет последнее действие в сессии (если разрешено).
        /// </summary>
        public Task<bool> UndoAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateSessionId(sessionId);
            ValidateUserId(userId);
            cancellationToken.ThrowIfCancellationRequested();
            return _undoManager.UndoAsync(sessionId, userId);
        }

        /// <summary>
        /// Повторяет последнее отменённое действие.
        /// </summary>
        public Task<bool> RedoAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateSessionId(sessionId);
            ValidateUserId(userId);
            cancellationToken.ThrowIfCancellationRequested();
            return _undoManager.RedoAsync(sessionId, userId);
        }

        /// <summary>
        /// Возвращает описание последнего действия, доступного для отмены.
        /// </summary>
        public string? GetLastUndoDescription(Guid sessionId)
        {
            ValidateSessionId(sessionId);
            return _undoManager.GetLastUndoDescription(sessionId);
        }

        /// <summary>
        /// Возвращает описание последнего действия, доступного для повтора.
        /// </summary>
        public string? GetLastRedoDescription(Guid sessionId)
        {
            ValidateSessionId(sessionId);
            return _undoManager.GetLastRedoDescription(sessionId);
        }

        /// <summary>
        /// Очищает историю отмены/повтора для сессии.
        /// </summary>
        public void ClearSessionHistory(Guid sessionId)
        {
            ValidateSessionId(sessionId);
            _undoManager.ClearSession(sessionId);
        }

        /// <summary>
        /// Мастер принудительно отменяет последнее действие любого игрока в сессии.
        /// </summary>
        public async Task<bool> ForceUndoLastPlayerActionAsync(
            Guid sessionId,
            Guid gmUserId,
            CancellationToken cancellationToken = default)
        {
            ValidateSessionId(sessionId);
            ValidateUserId(gmUserId);
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _permissionChecker.IsGameMasterAsync(cancellationToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Только Мастер может принудительно отменить действие.");

            return await _undoManager.UndoAsync(sessionId, gmUserId).ConfigureAwait(false);
        }

        // --------------------------------------------------------------------------------
        // Приватная фабрика адаптеров отмены
        // --------------------------------------------------------------------------------

        private CommandUndoableAction? CreateUndoableAction(ICommand command, Guid userId, Guid sessionId)
        {
            return command switch
            {
                DealDamage cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Нанесение урона: {cmd.Amount} по {cmd.CharacterId}",
                    new HealCharacter(cmd.CharacterId, cmd.Amount),
                    cmd,
                    _commandBus),

                HealCharacter cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Лечение: {cmd.Amount} для {cmd.CharacterId}",
                    new DealDamage(cmd.CharacterId, cmd.Amount),
                    cmd,
                    _commandBus),

                AddGold cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Добавление золота: {cmd.Amount} для {cmd.CharacterId}",
                    new SpendGold(cmd.CharacterId, cmd.Amount),
                    cmd,
                    _commandBus),

                SpendGold cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Списание золота: {cmd.Amount} у {cmd.CharacterId}",
                    new AddGold(cmd.CharacterId, cmd.Amount),
                    cmd,
                    _commandBus),

                AddInventoryItem cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Добавление предмета {cmd.ItemId} x{cmd.Quantity} для {cmd.CharacterId}",
                    new RemoveInventoryItem(cmd.CharacterId, cmd.ItemId, cmd.Quantity),
                    cmd,
                    _commandBus),

                RemoveInventoryItem cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Удаление предмета {cmd.ItemId} x{cmd.Quantity} у {cmd.CharacterId}",
                    new AddInventoryItem(cmd.CharacterId, cmd.ItemId, cmd.ItemId, cmd.Quantity),
                    cmd,
                    _commandBus),

                ApplyCondition cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Наложение состояния {cmd.ConditionType} на {cmd.CharacterId}",
                    new RemoveCondition(cmd.CharacterId, cmd.ConditionType),
                    cmd,
                    _commandBus),

                RemoveCondition cmd => new CommandUndoableAction(
                    userId, sessionId,
                    $"Снятие состояния {cmd.ConditionType} с {cmd.CharacterId}",
                    new ApplyCondition(cmd.CharacterId, cmd.ConditionType, 1), // возвращаем состояние на 1 раунд
                    cmd,
                    _commandBus),

                _ => null
            };
        }

        private static void ValidateUserId(Guid userId)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));
        }

        private static void ValidateSessionId(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Идентификатор сессии не может быть пустым.", nameof(sessionId));
        }
    }

    /// <summary>
    /// Адаптер команды к <see cref="IUndoableAction"/>.
    /// Хранит обратную команду и команду повтора.
    /// </summary>
    internal sealed class CommandUndoableAction(
        Guid userId,
        Guid gameSessionId,
        string description,
        ICommand undoCommand,
        ICommand redoCommand,
        ICommandBus commandBus) : IUndoableAction
    {
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly ICommand _undoCommand = undoCommand ?? throw new ArgumentNullException(nameof(undoCommand));
        private readonly ICommand _redoCommand = redoCommand ?? throw new ArgumentNullException(nameof(redoCommand));

        public Guid ActionId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public Guid UserId { get; } = userId;
        public Guid GameSessionId { get; } = gameSessionId;
        public string Description { get; } = description ?? throw new ArgumentNullException(nameof(description));

        public Task<bool> CanUndoAsync() => Task.FromResult(true);
        public Task<bool> CanRedoAsync() => Task.FromResult(true);

        public async Task UndoAsync()
        {
            await _commandBus.SendAsync(_undoCommand,
                new CommandContext { UserId = UserId, GameSessionId = GameSessionId }).ConfigureAwait(false);
        }

        public async Task RedoAsync()
        {
            await _commandBus.SendAsync(_redoCommand,
                new CommandContext { UserId = UserId, GameSessionId = GameSessionId }).ConfigureAwait(false);
        }
    }
}