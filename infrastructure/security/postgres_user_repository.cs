#nullable enable
using dnd_game.application.security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Реализация <see cref="IUserRepository"/> на базе PostgreSQL.
    /// Хранит учётные записи пользователей, включая роли в кампаниях (в формате JSONB).
    /// </summary>
    public class PostgresUserRepository : IUserRepository
    {
        private readonly string _connectionString;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Lazy<Task> _initialization;
        private readonly ILogger<PostgresUserRepository> _logger;

        public PostgresUserRepository(
            string connectionString,
            ILogger<PostgresUserRepository>? logger = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? NullLogger<PostgresUserRepository>.Instance;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Ленивая инициализация: создание таблицы при первом обращении
            _initialization = new Lazy<Task>(InitializeDatabaseAsync);
        }

        /// <summary>
        /// Создаёт таблицу users, если она ещё не существует.
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE IF NOT EXISTS users (
                        id UUID PRIMARY KEY,
                        username TEXT UNIQUE NOT NULL,
                        email TEXT UNIQUE NOT NULL,
                        password_hash TEXT NOT NULL,
                        global_role TEXT NOT NULL,
                        created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                        is_active BOOLEAN NOT NULL DEFAULT TRUE,
                        campaign_roles JSONB DEFAULT '{}'::jsonb
                    );
                ", conn);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                _logger.LogInformation("Таблица пользователей инициализирована.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось инициализировать таблицу пользователей.");
                throw;
            }
        }

        /// <summary>
        /// Ожидает завершения инициализации (создание таблицы при первом использовании).
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            await _initialization.Value.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                SELECT id, username, email, password_hash, global_role, created_at, is_active, campaign_roles
                FROM users WHERE id = @id
            ", conn);
            cmd.Parameters.AddWithValue("id", userId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapUser(reader);
            }
            return null;
        }

        public async Task<List<UserAccount>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
        SELECT id, username, email, password_hash, global_role, created_at, is_active, campaign_roles
        FROM users ORDER BY username", conn);

            var users = new List<UserAccount>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                users.Add(MapUser(reader));
            }
            return users;
        }

        /// <inheritdoc />
        public async Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Имя пользователя не может быть пустым.", nameof(username));
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                SELECT id, username, email, password_hash, global_role, created_at, is_active, campaign_roles
                FROM users WHERE username = @username
            ", conn);
            cmd.Parameters.AddWithValue("username", username);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapUser(reader);
            }
            return null;
        }

        /// <inheritdoc />
        public async Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email не может быть пустым.", nameof(email));
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                SELECT id, username, email, password_hash, global_role, created_at, is_active, campaign_roles
                FROM users WHERE email = @email
            ", conn);
            cmd.Parameters.AddWithValue("email", email);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapUser(reader);
            }
            return null;
        }

        /// <inheritdoc />
        public async Task AddAsync(UserAccount user, CancellationToken cancellationToken = default)
        {
            ValidateUser(user);
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO users (id, username, email, password_hash, global_role, created_at, is_active, campaign_roles)
                VALUES (@id, @username, @email, @password_hash, @global_role, @created_at, @is_active, @campaign_roles::jsonb)
            ", conn);

            cmd.Parameters.AddWithValue("id", user.Id);
            cmd.Parameters.AddWithValue("username", user.Username);
            cmd.Parameters.AddWithValue("email", user.Email);
            cmd.Parameters.AddWithValue("password_hash", user.PasswordHash);
            cmd.Parameters.AddWithValue("global_role", user.GlobalRole.ToString());
            cmd.Parameters.AddWithValue("created_at", user.CreatedAt);
            cmd.Parameters.AddWithValue("is_active", user.IsActive);
            cmd.Parameters.AddWithValue("campaign_roles", NpgsqlDbType.Jsonb, SerializeCampaignRoles(user.CampaignRoles));

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Пользователь {UserId} добавлен.", user.Id);
        }

        /// <inheritdoc />
        public async Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default)
        {
            ValidateUser(user);
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                UPDATE users
                SET username = @username,
                    email = @email,
                    password_hash = @password_hash,
                    global_role = @global_role,
                    is_active = @is_active,
                    campaign_roles = @campaign_roles::jsonb
                WHERE id = @id
            ", conn);

            cmd.Parameters.AddWithValue("id", user.Id);
            cmd.Parameters.AddWithValue("username", user.Username);
            cmd.Parameters.AddWithValue("email", user.Email);
            cmd.Parameters.AddWithValue("password_hash", user.PasswordHash);
            cmd.Parameters.AddWithValue("global_role", user.GlobalRole.ToString());
            cmd.Parameters.AddWithValue("is_active", user.IsActive);
            cmd.Parameters.AddWithValue("campaign_roles", NpgsqlDbType.Jsonb, SerializeCampaignRoles(user.CampaignRoles));

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
                _logger.LogWarning("Пользователь {UserId} не найден при обновлении.", user.Id);
            else
                _logger.LogDebug("Пользователь {UserId} обновлён.", user.Id);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", userId);

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
                _logger.LogWarning("Пользователь {UserId} не найден при удалении.", userId);
            else
                _logger.LogDebug("Пользователь {UserId} удалён.", userId);
        }

        // ---------- Вспомогательные методы ----------

        /// <summary>
        /// Преобразует строку таблицы в объект <see cref="UserAccount"/>.
        /// </summary>
        private UserAccount MapUser(NpgsqlDataReader reader)
        {
            var id = reader.GetGuid(0);
            var username = reader.GetString(1);
            var email = reader.GetString(2);
            var passwordHash = reader.GetString(3);
            var globalRole = Enum.Parse<UserRole>(reader.GetString(4));
            var createdAt = reader.GetDateTime(5);
            var isActive = reader.GetBoolean(6);

            Dictionary<Guid, CampaignRole> campaignRoles;
            if (!reader.IsDBNull(7))
            {
                var json = reader.GetString(7);
                campaignRoles = JsonSerializer.Deserialize<Dictionary<Guid, CampaignRole>>(json, _jsonOptions)
                                ?? [];
            }
            else
            {
                campaignRoles = [];
            }

            return new UserAccount
            {
                Id = id,
                Username = username,
                Email = email,
                PasswordHash = passwordHash,
                GlobalRole = globalRole,
                CreatedAt = createdAt,
                IsActive = isActive,
                CampaignRoles = campaignRoles
            };
        }

        /// <summary>
        /// Сериализует словарь ролей в JSON для хранения в JSONB.
        /// </summary>
        private string SerializeCampaignRoles(Dictionary<Guid, CampaignRole> roles)
        {
            return JsonSerializer.Serialize(roles ?? [], _jsonOptions);
        }

        /// <summary>
        /// Проверяет корректность объекта пользователя перед операциями Add/Update.
        /// </summary>
        private static void ValidateUser(UserAccount user)
        {
            ArgumentNullException.ThrowIfNull(user, nameof(user));
            if (user.Id == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(user));
            if (string.IsNullOrWhiteSpace(user.Username))
                throw new ArgumentException("Имя пользователя не может быть пустым.", nameof(user));
            if (string.IsNullOrWhiteSpace(user.Email))
                throw new ArgumentException("Email не может быть пустым.", nameof(user));
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                throw new ArgumentException("Хэш пароля не может быть пустым.", nameof(user));
        }
    }
}