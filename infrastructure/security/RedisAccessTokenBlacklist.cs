#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace dnd_game.infrastructure.security
{
    public class RedisAccessTokenBlacklist : IAccessTokenBlacklist
    {
        private readonly IDatabase _db;

        public RedisAccessTokenBlacklist(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
        }

        public async Task RevokeAsync(string token, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            var key = $"revoked_token:{token}";
            await _db.StringSetAsync(key, "revoked", lifetime);
        }

        public async Task<bool> IsRevokedAsync(string token, CancellationToken cancellationToken = default)
        {
            var key = $"revoked_token:{token}";
            return await _db.KeyExistsAsync(key);
        }
    }
}