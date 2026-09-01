#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.caching
{
    /// <summary>
    /// Реализация кэш-провайдера, которая намеренно ничего не сохраняет и не возвращает.
    /// Используется в сценариях, где кэширование отключено (например, при разработке,
    /// тестировании или когда данные не должны кэшироваться).
    /// </summary>
    public class NoOpCacheProvider : ICacheProvider
    {
        /// <inheritdoc />
        /// <remarks>Всегда возвращает <c>null</c> — записи не сохраняются.</remarks>
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<T?>(null);
        }

        /// <inheritdoc />
        /// <remarks>Игнорирует сохранение — операция завершается успешно без каких-либо действий.</remarks>
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>Удаление не требуется, так как записи никогда не сохраняются.</remarks>
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>Всегда возвращает <c>false</c>, так как кэш не используется.</remarks>
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public void RemoveSync(string key)
        {
            // Ничего не делаем
        }
    }
}