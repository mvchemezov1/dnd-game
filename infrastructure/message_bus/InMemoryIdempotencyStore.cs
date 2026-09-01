#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.message_bus
{
    public class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<Guid, DateTime> _keys = new();

        public Task<bool> TryAddAsync(Guid key, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expires = DateTime.UtcNow + lifetime;
            return Task.FromResult(_keys.TryAdd(key, expires));
        }

        public Task<bool> ContainsAsync(Guid key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_keys.TryGetValue(key, out var expires))
            {
                if (expires > DateTime.UtcNow)
                    return Task.FromResult(true);
                // удаляем истёкший ключ
                _keys.TryRemove(key, out _);
            }
            return Task.FromResult(false);
        }
    }
}