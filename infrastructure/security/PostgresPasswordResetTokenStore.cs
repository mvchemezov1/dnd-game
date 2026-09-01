#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace dnd_game.infrastructure.security
{
    public class PostgresPasswordResetTokenStore
    {
        private readonly string _connectionString;
        private readonly ILogger<PostgresPasswordResetTokenStore> _logger;

        public PostgresPasswordResetTokenStore(string connectionString, ILogger<PostgresPasswordResetTokenStore>? logger = null)
        {
            _connectionString = connectionString;
            _logger = logger ?? NullLogger<PostgresPasswordResetTokenStore>.Instance;
        }

        public async Task<string> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken ct = default)
        {
            // Генерируем случайный токен
            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = HashToken(rawToken);
            var expiresAt = DateTime.UtcNow.Add(lifetime);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO password_reset_tokens (token_hash, user_id, expires_at)
                VALUES (@hash, @userId, @expires)", conn);
            cmd.Parameters.AddWithValue("hash", hash);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("expires", expiresAt);
            await cmd.ExecuteNonQueryAsync(ct);

            return rawToken;
        }

        public async Task<Guid?> ValidateAsync(string rawToken, CancellationToken ct = default)
        {
            var hash = HashToken(rawToken);
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(@"
                SELECT user_id FROM password_reset_tokens
                WHERE token_hash = @hash AND used = FALSE AND expires_at > NOW()
                LIMIT 1", conn);
            cmd.Parameters.AddWithValue("hash", hash);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is Guid userId ? userId : null;
        }

        public async Task MarkUsedAsync(string rawToken, CancellationToken ct = default)
        {
            var hash = HashToken(rawToken);
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "UPDATE password_reset_tokens SET used = TRUE WHERE token_hash = @hash", conn);
            cmd.Parameters.AddWithValue("hash", hash);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(bytes);
        }
    }
}