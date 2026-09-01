#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.application.security;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.network;

namespace dnd_game.infrastructure.undo
{
    /// <summary>
    /// Интерфейс действия, поддерживающего отмену (Undo) и повтор (Redo).
    /// </summary>
    public interface IUndoableAction
    {
        /// <summary>Идентификатор действия.</summary>
        Guid ActionId { get; }

        /// <summary>Момент выполнения действия (UTC).</summary>
        DateTime Timestamp { get; }

        /// <summary>Идентификатор пользователя, выполнившего действие.</summary>
        Guid UserId { get; }

        /// <summary>Идентификатор игровой сессии, в которой выполнено действие.</summary>
        Guid GameSessionId { get; }

        /// <summary>Описание действия для отображения пользователю.</summary>
        string Description { get; }

        /// <summary>Проверяет, можно ли отменить действие в текущий момент.</summary>
        Task<bool> CanUndoAsync();

        /// <summary>Проверяет, можно ли повторить действие в текущий момент.</summary>
        Task<bool> CanRedoAsync();

        /// <summary>Выполняет отмену действия.</summary>
        Task UndoAsync();

        /// <summary>Выполняет повтор действия.</summary>
        Task RedoAsync();
    }

    /// <summary>
    /// Базовый абстрактный класс для действий с поддержкой отмены.
    /// Содержит общие поля и ссылку на шину команд.
    /// </summary>
    public abstract class UndoableActionBase : IUndoableAction
    {
        public Guid ActionId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public Guid UserId { get; }
        public Guid GameSessionId { get; }
        public abstract string Description { get; }

        /// <summary>Шина команд, доступная наследникам для выполнения компенсационных команд.</summary>
        protected readonly ICommandBus CommandBus;

        protected UndoableActionBase(Guid userId, Guid gameSessionId, ICommandBus commandBus)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));
            if (gameSessionId == Guid.Empty)
                throw new ArgumentException("Идентификатор сессии не может быть пустым.", nameof(gameSessionId));

            UserId = userId;
            GameSessionId = gameSessionId;
            CommandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        }

        public abstract Task<bool> CanUndoAsync();
        public abstract Task<bool> CanRedoAsync();
        public abstract Task UndoAsync();
        public abstract Task RedoAsync();
    }

    /// <summary>
    /// Менеджер Undo/Redo для игровых сессий.
    /// Хранит стеки действий для каждой сессии и проверяет права пользователей при отмене/повторе.
    /// </summary>
    public sealed class UndoManager
    {
        private sealed class SessionUndoState
        {
            public readonly object Lock = new();
            /// <summary>Стек выполненных действий (вершина — последнее действие).</summary>
            public readonly LinkedList<IUndoableAction> UndoStack = new();
            /// <summary>Стек отменённых действий (вершина — последнее отменённое).</summary>
            public readonly Stack<IUndoableAction> RedoStack = new();
        }

        private readonly ConcurrentDictionary<Guid, SessionUndoState> _sessions = new();
        private readonly ICommandBus _commandBus;
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<UndoManager> _logger;

        /// <summary>Максимальное количество хранимых действий отмены на сессию.</summary>
        public int MaxUndoSteps { get; }

        /// <summary>Действия старше этого возраста не могут быть отменены.</summary>
        public TimeSpan MaxActionAge { get; }

        public UndoManager(
            ICommandBus commandBus,
            ISessionManager sessionManager,
            ILogger<UndoManager> logger,
            int maxUndoSteps = 100,
            TimeSpan? maxActionAge = null)
        {
            _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (maxUndoSteps <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxUndoSteps), "Количество шагов отмены должно быть положительным.");

            MaxUndoSteps = maxUndoSteps;
            MaxActionAge = maxActionAge ?? TimeSpan.FromMinutes(30);
        }

        /// <summary>
        /// Регистрирует выполненное действие в стеке отмены.
        /// </summary>
        public Task RecordActionAsync(IUndoableAction action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var state = _sessions.GetOrAdd(action.GameSessionId, _ => new SessionUndoState());
            lock (state.Lock)
            {
                state.UndoStack.AddLast(action);
                state.RedoStack.Clear();

                // Удаляем самые старые действия, если превышен лимит
                while (state.UndoStack.Count > MaxUndoSteps)
                {
                    state.UndoStack.RemoveFirst();
                }
            }

            _logger.LogDebug("Зарегистрировано действие {ActionId} для сессии {SessionId}", action.ActionId, action.GameSessionId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Отменяет последнее действие в сессии, если пользователь имеет право.
        /// </summary>
        public async Task<bool> UndoAsync(Guid sessionId, Guid userId)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
                return false;

            IUndoableAction? action;
            lock (state.Lock)
            {
                action = state.UndoStack.Last?.Value;
                if (action == null) return false;
            }

            if (!await HasPermissionAsync(userId, sessionId, action.UserId).ConfigureAwait(false))
            {
                _logger.LogWarning("Отмена действия {ActionId} запрещена для пользователя {UserId}", action.ActionId, userId);
                return false;
            }

            if (DateTime.UtcNow - action.Timestamp > MaxActionAge)
            {
                _logger.LogWarning("Отмена действия {ActionId} невозможна: прошло слишком много времени ({Age})", action.ActionId, DateTime.UtcNow - action.Timestamp);
                return false;
            }

            if (!await action.CanUndoAsync().ConfigureAwait(false))
            {
                _logger.LogWarning("Действие {ActionId} в данный момент не может быть отменено", action.ActionId);
                return false;
            }

            try
            {
                await action.UndoAsync().ConfigureAwait(false);

                lock (state.Lock)
                {
                    state.UndoStack.RemoveLast();
                    state.RedoStack.Push(action);
                }

                _logger.LogInformation("Отмена успешна: действие {ActionId}, пользователь {UserId}", action.ActionId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отмене действия {ActionId}", action.ActionId);
                throw;
            }
        }

        /// <summary>
        /// Повторяет последнее отменённое действие.
        /// </summary>
        public async Task<bool> RedoAsync(Guid sessionId, Guid userId)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
                return false;

            IUndoableAction? action;
            lock (state.Lock)
            {
                if (state.RedoStack.Count == 0) return false;
                action = state.RedoStack.Peek();
            }

            if (!await HasPermissionAsync(userId, sessionId, action!.UserId).ConfigureAwait(false))
            {
                _logger.LogWarning("Повтор действия {ActionId} запрещён для пользователя {UserId}", action.ActionId, userId);
                return false;
            }

            if (!await action.CanRedoAsync().ConfigureAwait(false))
            {
                _logger.LogWarning("Действие {ActionId} в данный момент не может быть повторено", action.ActionId);
                return false;
            }

            try
            {
                await action.RedoAsync().ConfigureAwait(false);

                lock (state.Lock)
                {
                    state.RedoStack.Pop();
                    state.UndoStack.AddLast(action);
                }

                _logger.LogInformation("Повтор успешен: действие {ActionId}, пользователь {UserId}", action.ActionId, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при повторе действия {ActionId}", action.ActionId);
                throw;
            }
        }

        /// <summary>
        /// Возвращает описание последнего действия, которое можно отменить, или null.
        /// </summary>
        public string? GetLastUndoDescription(Guid sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var state))
            {
                lock (state.Lock)
                {
                    return state.UndoStack.Last?.Value.Description;
                }
            }
            return null;
        }

        /// <summary>
        /// Возвращает описание последнего действия, которое можно повторить, или null.
        /// </summary>
        public string? GetLastRedoDescription(Guid sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var state))
            {
                lock (state.Lock)
                {
                    return state.RedoStack.Count > 0 ? state.RedoStack.Peek().Description : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Очищает стеки отмены/повтора для указанной сессии.
        /// </summary>
        public void ClearSession(Guid sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
            _logger.LogInformation("Стеки отмены очищены для сессии {SessionId}", sessionId);
        }

        /// <summary>
        /// Проверяет, имеет ли пользователь право отменять/повторять действие.
        /// Пользователь может отменять свои действия, либо действия, если он Мастер в данной сессии.
        /// </summary>
        private async Task<bool> HasPermissionAsync(Guid userId, Guid sessionId, Guid actionOwnerId)
        {
            // Владелец действия всегда может его отменить
            if (userId == actionOwnerId)
                return true;

            // Проверяем роль пользователя в сессии
            var role = await _sessionManager.GetUserRole(userId, sessionId).ConfigureAwait(false);
            return role == CampaignRole.GameMaster;
        }
    }
}