#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// In-memory реализация чёрного списка access-токенов.
    /// Используется, когда Redis недоступен.
    /// </summary>
    public class InMemoryAccessTokenBlacklist : IAccessTokenBlacklist
    {
        private readonly ConcurrentDictionary<string, DateTime> _revokedTokens = new();

        public Task RevokeAsync(string token, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Токен не может быть пустым.", nameof(token));
            if (lifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(lifetime), "Время жизни должно быть положительным.");

            var expiresAt = DateTime.UtcNow + lifetime;
            _revokedTokens[token] = expiresAt;
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(string token, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(token))
                return Task.FromResult(false);

            if (_revokedTokens.TryGetValue(token, out var expiresAt))
            {
                if (expiresAt > DateTime.UtcNow)
                {
                    return Task.FromResult(true);
                }
                // Удаляем просроченную запись
                _revokedTokens.TryRemove(token, out _);
            }
            return Task.FromResult(false);
        }
    }
}