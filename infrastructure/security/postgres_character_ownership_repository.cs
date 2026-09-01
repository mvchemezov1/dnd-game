#nullable enable
using dnd_game.application.security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Реализация <see cref="ICharacterOwnershipRepository"/> на базе PostgreSQL.
    /// Хранит связи «персонаж → игрок-владелец», «персонаж → кампания» и признак NPC
    /// в таблице character_ownership (см. миграцию 010_AddCharacterOwnership.sql).
    ///
    /// Персонажи ведутся через event sourcing и переживают перезапуск сервера, а
    /// раньше владение хранилось только в памяти (ConcurrentDictionary) и
    /// терялось при каждом рестарте — из-за чего у обычных игроков (не GM)
    /// список персонажей после перезапуска становился пустым, хотя сами
    /// персонажи никуда не девались. Эта реализация делает владение таким же
    /// персистентным, как и всё остальное.
    /// </summary>
    public class PostgresCharacterOwnershipRepository : ICharacterOwnershipRepository
    {
        private readonly string _connectionString;
        private readonly Lazy<Task> _initialization;
        private readonly ILogger<PostgresCharacterOwnershipRepository> _logger;

        public PostgresCharacterOwnershipRepository(
            string connectionString,
            ILogger<PostgresCharacterOwnershipRepository>? logger = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? NullLogger<PostgresCharacterOwnershipRepository>.Instance;

            // Ленивая инициализация: таблица уже создаётся миграцией
            // 010_AddCharacterOwnership.sql, но CREATE TABLE IF NOT EXISTS
            // здесь дублируется на случай запуска без прогона миграций
            // (например, в тестовом/дев-окружении) — как и в
            // PostgresUserRepository.
            _initialization = new Lazy<Task>(InitializeDatabaseAsync);
        }

        private async Task InitializeDatabaseAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE IF NOT EXISTS character_ownership (
                        character_id UUID PRIMARY KEY,
                        owner_user_id UUID NOT NULL,
                        campaign_id UUID NULL,
                        is_npc BOOLEAN NOT NULL DEFAULT FALSE,
                        created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                        updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                    CREATE INDEX IF NOT EXISTS idx_character_ownership_owner ON character_ownership(owner_user_id);
                    CREATE INDEX IF NOT EXISTS idx_character_ownership_campaign ON character_ownership(campaign_id);
                ", conn);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                _logger.LogInformation("Таблица character_ownership инициализирована.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось инициализировать таблицу character_ownership.");
                throw;
            }
        }

        private async Task EnsureInitializedAsync()
        {
            await _initialization.Value.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Guid?> GetOwnerIdAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "SELECT owner_user_id FROM character_ownership WHERE character_id = @character_id", conn);
            cmd.Parameters.AddWithValue("character_id", characterId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            // owner_user_id = Guid.Empty используется как «владельца нет»
            // (например, для NPC, привязанных только к кампании через
            // SetCampaignAsync/MarkAsNpcAsync) — трактуем это как null,
            // как и старая in-memory реализация.
            return result is Guid ownerId && ownerId != Guid.Empty ? ownerId : null;
        }

        /// <inheritdoc />
        public async Task<Guid?> GetCampaignIdAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "SELECT campaign_id FROM character_ownership WHERE character_id = @character_id", conn);
            cmd.Parameters.AddWithValue("character_id", characterId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is Guid campaignId ? campaignId : null;
        }

        /// <inheritdoc />
        public async Task<bool> IsNonPlayerCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "SELECT is_npc FROM character_ownership WHERE character_id = @character_id", conn);
            cmd.Parameters.AddWithValue("character_id", characterId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is bool isNpc && isNpc;
        }

        /// <inheritdoc />
        public async Task<List<Guid>> GetOwnedCharacterIdsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(userId, nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "SELECT character_id FROM character_ownership WHERE owner_user_id = @owner_user_id", conn);
            cmd.Parameters.AddWithValue("owner_user_id", userId);

            var result = new List<Guid>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(reader.GetGuid(0));
            }
            return result;
        }

        /// <inheritdoc />
        public async Task AssignOwnerAsync(Guid characterId, Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateGuid(userId, nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO character_ownership (character_id, owner_user_id, updated_at)
                VALUES (@character_id, @owner_user_id, NOW())
                ON CONFLICT (character_id)
                DO UPDATE SET owner_user_id = EXCLUDED.owner_user_id, updated_at = NOW()
            ", conn);
            cmd.Parameters.AddWithValue("character_id", characterId);
            cmd.Parameters.AddWithValue("owner_user_id", userId);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Персонаж {CharacterId} привязан к игроку {UserId}", characterId, userId);
        }

        /// <summary>
        /// Привязывает персонажа к кампании (не входит в интерфейс, но нужна
        /// для будущей интеграции — см. аналогичный метод в старой
        /// in-memory реализации).
        /// </summary>
        public async Task SetCampaignAsync(Guid characterId, Guid campaignId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateGuid(campaignId, nameof(campaignId));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO character_ownership (character_id, owner_user_id, campaign_id, updated_at)
                VALUES (@character_id, @owner_user_id, @campaign_id, NOW())
                ON CONFLICT (character_id)
                DO UPDATE SET campaign_id = EXCLUDED.campaign_id, updated_at = NOW()
            ", conn);
            cmd.Parameters.AddWithValue("character_id", characterId);
            // Если строки ещё нет, столбец owner_user_id (NOT NULL) заполняется
            // как Guid.Empty — это трактуется GetOwnerIdAsync как «владельца нет».
            // Если персонаж уже имел владельца, ON CONFLICT его не затронет.
            cmd.Parameters.AddWithValue("owner_user_id", Guid.Empty);
            cmd.Parameters.AddWithValue("campaign_id", campaignId);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Персонаж {CharacterId} привязан к кампании {CampaignId}", characterId, campaignId);
        }

        /// <summary>
        /// Помечает персонажа как NPC (не входит в интерфейс).
        /// </summary>
        public async Task MarkAsNpcAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureInitializedAsync().ConfigureAwait(false);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO character_ownership (character_id, owner_user_id, is_npc, updated_at)
                VALUES (@character_id, @owner_user_id, TRUE, NOW())
                ON CONFLICT (character_id)
                DO UPDATE SET is_npc = TRUE, updated_at = NOW()
            ", conn);
            cmd.Parameters.AddWithValue("character_id", characterId);
            cmd.Parameters.AddWithValue("owner_user_id", Guid.Empty);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Персонаж {CharacterId} помечен как NPC", characterId);
        }

        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не может быть пустым: {paramName}", paramName);
        }
    }
}
