#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Хранит идентификаторы отозванных access-токенов.
    /// </summary>
    public interface IAccessTokenBlacklist
    {
        /// <summary>Добавляет токен в чёрный список с временем жизни до истечения.</summary>
        Task RevokeAsync(string token, TimeSpan lifetime, CancellationToken cancellationToken = default);

        /// <summary>Проверяет, отозван ли токен.</summary>
        Task<bool> IsRevokedAsync(string token, CancellationToken cancellationToken = default);
    }
}