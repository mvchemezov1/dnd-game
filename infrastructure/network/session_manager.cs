#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.application.security;

namespace dnd_game.infrastructure.network
{
    /// <summary>
    /// Менеджер игровых сессий. Управляет созданием, подключением и отключением игроков,
    /// а также связями между пользователями, сессиями и сетевыми подключениями.
    /// </summary>
    public class SessionManager(
        PermissionChecker permissionChecker,
        ILogger<SessionManager> logger,
        int maxPlayersPerSession = 10) : ISessionManager
    {
        // Хранилище всех сессий
        private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();
        // Текущая сессия для каждого пользователя
        private readonly ConcurrentDictionary<Guid, Guid> _userCurrentSession = new();
        // Сопоставление ConnectionId -> UserId
        private readonly ConcurrentDictionary<Guid, Guid> _connectionToUser = new();

        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        private readonly ILogger<SessionManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly int _maxPlayersPerSession = maxPlayersPerSession > 0
                ? maxPlayersPerSession
                : throw new ArgumentOutOfRangeException(nameof(maxPlayersPerSession), "Максимальное количество игроков должно быть положительным.");

        /// <inheritdoc />
        public async Task<Guid> CreateSession(Guid userId, string campaignId)
        {
            ValidateUserId(userId);
            if (string.IsNullOrWhiteSpace(campaignId))
                throw new ArgumentException("Идентификатор кампании не может быть пустым.", nameof(campaignId));
            if (!Guid.TryParse(campaignId, out var campaignGuid))
                throw new ArgumentException("Неверный формат идентификатора кампании.", nameof(campaignId));

            // Проверяем, что пользователь является Мастером этой кампании.
            // ВАЖНО: PermissionChecker работает с текущим пользователем из контекста.
            // В идеале сюда нужно передавать userId явно, но для текущей архитектуры полагаемся на контекст.
            if (!await _permissionChecker.IsGameMasterOfCampaignAsync(campaignGuid).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Только Мастер может создать сессию для этой кампании.");

            var sessionId = Guid.NewGuid();
            var session = new GameSession
            {
                SessionId = sessionId,
                CampaignId = campaignGuid,
                MasterUserId = userId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            session.Participants.TryAdd(userId, CampaignRole.GameMaster);
            _sessions[sessionId] = session;
            _userCurrentSession[userId] = sessionId;

            _logger.LogInformation("Сессия {SessionId} создана для кампании {CampaignId} мастером {UserId}",
                sessionId, campaignId, userId);

            return sessionId;
        }

        /// <inheritdoc />
        public async Task JoinSession(Guid sessionId, Guid userId)
        {
            ValidateUserId(userId);
            ValidateSessionId(sessionId);

            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new InvalidOperationException("Сессия не найдена.");
            if (!session.IsActive)
                throw new InvalidOperationException("Сессия не активна.");

            CampaignRole role;
            if (userId == session.MasterUserId)
            {
                role = CampaignRole.GameMaster;
            }
            else
            {
                // Проверяем членство пользователя в кампании.
                // Аналогично: PermissionChecker использует текущий контекст, поэтому для корректности
                // нужно передавать userId в метод, но в текущей версии метод не принимает userId.
                // В production необходимо доработать PermissionChecker.
                if (!await _permissionChecker.IsMemberOfCampaignAsync(session.CampaignId).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("Вы не являетесь участником этой кампании.");
                role = CampaignRole.Player;
            }

            if (session.Participants.Count >= _maxPlayersPerSession)
                throw new InvalidOperationException("Сессия заполнена.");

            // Добавляем участника (если уже есть, обновляем роль)
            session.Participants.AddOrUpdate(userId, role, (_, _) => role);
            _userCurrentSession[userId] = sessionId;

            _logger.LogInformation("Пользователь {UserId} присоединился к сессии {SessionId}", userId, sessionId);
        }

        /// <inheritdoc />
        public Task LeaveSession(Guid sessionId, Guid userId)
        {
            ValidateUserId(userId);
            ValidateSessionId(sessionId);

            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new InvalidOperationException("Сессия не найдена.");
            if (!session.Participants.ContainsKey(userId))
                throw new InvalidOperationException("Пользователь не состоит в этой сессии.");

            session.Participants.TryRemove(userId, out _);
            if (_userCurrentSession.TryGetValue(userId, out var current) && current == sessionId)
                _userCurrentSession.TryRemove(userId, out _);

            _logger.LogInformation("Пользователь {UserId} покинул сессию {SessionId}", userId, sessionId);

            // Если участников не осталось, деактивируем сессию
            if (session.Participants.IsEmpty)
            {
                session.IsActive = false;
                _logger.LogInformation("Сессия {SessionId} деактивирована (нет участников).", sessionId);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> IsUserInSession(Guid userId, Guid sessionId)
        {
            ValidateUserId(userId);
            ValidateSessionId(sessionId);

            if (_sessions.TryGetValue(sessionId, out var session))
                return Task.FromResult(session.Participants.ContainsKey(userId));
            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Guid>> GetSessionUsers(Guid sessionId)
        {
            ValidateSessionId(sessionId);

            if (_sessions.TryGetValue(sessionId, out var session))
                return Task.FromResult<IEnumerable<Guid>>([.. session.Participants.Keys]);
            return Task.FromResult<IEnumerable<Guid>>([]);
        }

        /// <inheritdoc />
        public Task<CampaignRole?> GetUserRole(Guid userId, Guid sessionId)
        {
            ValidateUserId(userId);
            ValidateSessionId(sessionId);

            if (_sessions.TryGetValue(sessionId, out var session) &&
                session.Participants.TryGetValue(userId, out var role))
                return Task.FromResult<CampaignRole?>(role);
            return Task.FromResult<CampaignRole?>(null);
        }

        /// <inheritdoc />
        public Task AssociateConnection(Guid userId, Guid sessionId, Guid connectionId, CancellationToken ct)
        {
            ValidateUserId(userId);
            ValidateSessionId(sessionId);
            if (connectionId == Guid.Empty)
                throw new ArgumentException("Идентификатор подключения не может быть пустым.", nameof(connectionId));

            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new InvalidOperationException("Сессия не найдена.");
            if (!session.Participants.ContainsKey(userId))
                throw new UnauthorizedAccessException("Пользователь не состоит в сессии.");

            // Запоминаем связь ConnectionId -> UserId
            _connectionToUser[connectionId] = userId;
            // Отмечаем соединение в сессии
            session.Connections.TryAdd(connectionId, 0);

            _logger.LogDebug("Подключение {ConnectionId} связано с пользователем {UserId} в сессии {SessionId}",
                connectionId, userId, sessionId);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void RemoveConnection(Guid connectionId)
        {
            if (connectionId == Guid.Empty)
                throw new ArgumentException("Идентификатор подключения не может быть пустым.", nameof(connectionId));

            if (_connectionToUser.TryRemove(connectionId, out var userId))
            {
                // Удаляем соединение из текущей сессии пользователя
                if (_userCurrentSession.TryGetValue(userId, out var sessionId) &&
                    _sessions.TryGetValue(sessionId, out var session))
                {
                    session.Connections.TryRemove(connectionId, out _);
                    _logger.LogDebug("Подключение {ConnectionId} удалено из сессии {SessionId}", connectionId, sessionId);
                }
            }
        }

        // ---------- Валидация ----------

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
    /// Внутреннее состояние игровой сессии.
    /// </summary>
    internal sealed class GameSession
    {
        public Guid SessionId { get; set; }
        public Guid CampaignId { get; set; }
        public Guid MasterUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Участники сессии и их роли.</summary>
        public ConcurrentDictionary<Guid, CampaignRole> Participants { get; set; } = new();

        /// <summary>Сетевые подключения, связанные с сессией.</summary>
        public ConcurrentDictionary<Guid, byte> Connections { get; set; } = new();
    }
}