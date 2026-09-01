#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Хранилище refresh-токенов, отделённое от TokenService.
    /// Обеспечивает персистентное хранение, общее для нескольких экземпляров сервиса,
    /// и переживающее перезапуск процесса.
    /// </summary>
    public interface IRefreshTokenStore
    {
        /// <summary>Сохраняет или обновляет запись refresh-токена.</summary>
        Task SaveAsync(RefreshTokenEntry entry, CancellationToken cancellationToken = default);

        /// <summary>Возвращает запись токена по его хэшу.</summary>
        Task<RefreshTokenEntry?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

        /// <summary>Отзывает токен по его хэшу.</summary>
        Task RevokeAsync(string tokenHash, CancellationToken cancellationToken = default);

        /// <summary>Отзывает все refresh-токены пользователя.</summary>
        Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Удаляет истёкшие токены. Возвращает количество удалённых записей.</summary>
        Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Реализация <see cref="IRefreshTokenStore"/> на базе PostgreSQL.
    /// Создаёт таблицу при первом обращении и предоставляет потокобезопасные операции.
    /// </summary>
    public class PostgresRefreshTokenStore : IRefreshTokenStore
    {
        private readonly string _connectionString;
        private readonly ILogger<PostgresRefreshTokenStore> _logger;
        private readonly Lazy<Task> _initialization;

        public PostgresRefreshTokenStore(
            string connectionString,
            ILogger<PostgresRefreshTokenStore>? logger = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? NullLogger<PostgresRefreshTokenStore>.Instance;
            _initialization = new Lazy<Task>(InitializeDatabaseAsync);
        }

        /// <summary>
        /// Создаёт таблицу refresh_tokens и индексы, если они ещё не существуют.
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE IF NOT EXISTS refresh_tokens (
                        token_hash TEXT PRIMARY KEY,
                        user_id UUID NOT NULL,
                        device_info TEXT NULL,
                        created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                        expires_at TIMESTAMPTZ NOT NULL,
                        is_revoked BOOLEAN NOT NULL DEFAULT FALSE
                    );
                    CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON refresh_tokens(user_id);
                    CREATE INDEX IF NOT EXISTS idx_refresh_tokens_expires_at ON refresh_tokens(expires_at);
                ", conn);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                _logger.LogInformation("Таблица refresh-токенов инициализирована.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось инициализировать таблицу refresh-токенов.");
                throw;
            }
        }

        /// <summary>
        /// Ожидает завершения инициализации (создание таблицы при первом использовании).
        /// </summary>
        private Task EnsureInitializedAsync() => _initialization.Value;

        /// <inheritdoc />
        public async Task SaveAsync(RefreshTokenEntry entry, CancellationToken cancellationToken = default)
        {
            ValidateEntry(entry);
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO refresh_tokens (token_hash, user_id, device_info, expires_at, is_revoked)
                VALUES (@token_hash, @user_id, @device_info, @expires_at, @is_revoked)
                ON CONFLICT (token_hash) DO UPDATE
                SET user_id = EXCLUDED.user_id,
                    device_info = EXCLUDED.device_info,
                    expires_at = EXCLUDED.expires_at,
                    is_revoked = EXCLUDED.is_revoked
            ", conn);

            cmd.Parameters.AddWithValue("token_hash", entry.TokenHash);
            cmd.Parameters.AddWithValue("user_id", entry.UserId);
            cmd.Parameters.AddWithValue("device_info", (object?)entry.DeviceInfo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("expires_at", entry.ExpiresAt);
            cmd.Parameters.AddWithValue("is_revoked", entry.IsRevoked);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Refresh-токен сохранён для пользователя {UserId}.", entry.UserId);
        }

        /// <inheritdoc />
        public async Task<RefreshTokenEntry?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tokenHash))
                throw new ArgumentException("Хэш токена не может быть пустым.", nameof(tokenHash));
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                SELECT token_hash, user_id, device_info, expires_at, is_revoked
                FROM refresh_tokens
                WHERE token_hash = @token_hash
            ", conn);
            cmd.Parameters.AddWithValue("token_hash", tokenHash);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new RefreshTokenEntry
                {
                    TokenHash = reader.GetString(0),
                    UserId = reader.GetGuid(1),
                    DeviceInfo = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ExpiresAt = reader.GetDateTime(3),
                    IsRevoked = reader.GetBoolean(4)
                };
            }
            return null;
        }

        /// <inheritdoc />
        public async Task RevokeAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tokenHash))
                throw new ArgumentException("Хэш токена не может быть пустым.", nameof(tokenHash));
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "UPDATE refresh_tokens SET is_revoked = TRUE WHERE token_hash = @token_hash",
                conn);
            cmd.Parameters.AddWithValue("token_hash", tokenHash);

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
                _logger.LogWarning("Refresh-токен с хэшем {TokenHash} не найден при отзыве.", tokenHash);
            else
                _logger.LogDebug("Refresh-токен отозван.");
        }

        /// <inheritdoc />
        public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "UPDATE refresh_tokens SET is_revoked = TRUE WHERE user_id = @user_id",
                conn);
            cmd.Parameters.AddWithValue("user_id", userId);

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Отозвано refresh-токенов для пользователя {UserId}: {Count}.", userId, affected);
        }

        /// <inheritdoc />
        public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM refresh_tokens WHERE expires_at < NOW()",
                conn);

            var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
                _logger.LogInformation("Удалено истёкших refresh-токенов: {Count}.", deleted);
            return deleted;
        }

        /// <summary>
        /// Проверяет корректность записи перед сохранением.
        /// </summary>
        private static void ValidateEntry(RefreshTokenEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry, nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.TokenHash))
                throw new ArgumentException("Хэш токена не может быть пустым.", nameof(entry));
            if (entry.UserId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(entry));
            if (entry.ExpiresAt == default)
                throw new ArgumentException("Срок действия токена должен быть задан.", nameof(entry));
        }
    }
}