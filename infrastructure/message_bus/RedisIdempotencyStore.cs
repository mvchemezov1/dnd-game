#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace dnd_game.infrastructure.message_bus
{
    public class RedisIdempotencyStore : IIdempotencyStore
    {
        private readonly IDatabase _db;

        public RedisIdempotencyStore(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
        }

        public async Task<bool> TryAddAsync(Guid key, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            var redisKey = $"idempotency:{key}";
            return await _db.StringSetAsync(redisKey, "processed", lifetime, When.NotExists);
        }

        public async Task<bool> ContainsAsync(Guid key, CancellationToken cancellationToken = default)
        {
            var redisKey = $"idempotency:{key}";
            return await _db.KeyExistsAsync(redisKey);
        }
    }
}