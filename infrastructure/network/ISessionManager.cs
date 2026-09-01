#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.security;

namespace dnd_game.infrastructure.network
{
    /// <summary>
    /// Управляет игровыми сессиями: создание, подключение/отключение игроков,
    /// связывание сетевых соединений с сессиями.
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>Создаёт новую сессию для кампании. Требует прав мастера.</summary>
        Task<Guid> CreateSession(Guid userId, string campaignId);

        /// <summary>Присоединяет пользователя к сессии.</summary>
        Task JoinSession(Guid sessionId, Guid userId);

        /// <summary>Отключает пользователя от сессии.</summary>
        Task LeaveSession(Guid sessionId, Guid userId);

        /// <summary>Проверяет, состоит ли пользователь в указанной сессии.</summary>
        Task<bool> IsUserInSession(Guid userId, Guid sessionId);

        /// <summary>Возвращает список пользователей сессии.</summary>
        Task<IEnumerable<Guid>> GetSessionUsers(Guid sessionId);

        /// <summary>Возвращает роль пользователя в сессии или null, если он не участник.</summary>
        Task<CampaignRole?> GetUserRole(Guid userId, Guid sessionId);

        /// <summary>Связывает сетевое соединение с пользователем и сессией.</summary>
        Task AssociateConnection(Guid userId, Guid sessionId, Guid connectionId, CancellationToken cancellationToken);

        /// <summary>Удаляет связь соединения с сессией.</summary>
        void RemoveConnection(Guid connectionId);
    }
}