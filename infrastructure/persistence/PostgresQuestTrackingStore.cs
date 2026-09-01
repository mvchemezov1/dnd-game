#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.interfaces;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresQuestTrackingStore : PostgresRepositoryBase, IQuestTrackingStore
    {
        public PostgresQuestTrackingStore(string connectionString, ILogger<PostgresQuestTrackingStore> logger)
            : base(connectionString, logger) { }

        public async Task AddParticipantAsync(Guid questId, Guid characterId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO quest_participants (quest_id, character_id)
                VALUES (@questId, @charId)
                ON CONFLICT DO NOTHING";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("questId", questId);
                cmd.Parameters.AddWithValue("charId", characterId);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IEnumerable<Guid>> GetQuestsForCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT quest_id FROM quest_participants WHERE character_id = @charId";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("charId", characterId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var questIds = new List<Guid>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                questIds.Add(reader.GetGuid(0));
            }
            return questIds;
        }

        public async Task<IEnumerable<Guid>> GetQuestsForItemAsync(string itemId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT quest_id FROM quest_required_items WHERE item_id = @itemId";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("itemId", itemId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var questIds = new List<Guid>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                questIds.Add(reader.GetGuid(0));
            }
            return questIds;
        }

        public async Task RemoveQuestAsync(Guid questId, CancellationToken cancellationToken = default)
        {
            const string sql1 = "DELETE FROM quest_participants WHERE quest_id = @questId";
            const string sql2 = "DELETE FROM quest_required_items WHERE quest_id = @questId";
            const string sql3 = "DELETE FROM quest_campaigns WHERE quest_id = @questId";

            await ExecuteNonQueryAsync(sql1, cmd => cmd.Parameters.AddWithValue("questId", questId), cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(sql2, cmd => cmd.Parameters.AddWithValue("questId", questId), cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(sql3, cmd => cmd.Parameters.AddWithValue("questId", questId), cancellationToken).ConfigureAwait(false);
        }

        public async Task AddRequiredItemAsync(Guid questId, string itemId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO quest_required_items (quest_id, item_id)
                VALUES (@questId, @itemId)
                ON CONFLICT DO NOTHING";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("questId", questId);
                cmd.Parameters.AddWithValue("itemId", itemId);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task SetCampaignAsync(Guid questId, Guid campaignId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO quest_campaigns (quest_id, campaign_id)
                VALUES (@questId, @campaignId)
                ON CONFLICT (quest_id) DO UPDATE SET campaign_id = @campaignId";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("questId", questId);
                cmd.Parameters.AddWithValue("campaignId", campaignId);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<Guid?> GetCampaignAsync(Guid questId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT campaign_id FROM quest_campaigns WHERE quest_id = @questId";
            var result = await ExecuteScalarAsync(sql, cmd => cmd.Parameters.AddWithValue("questId", questId),
                cancellationToken).ConfigureAwait(false);

            return result is Guid campaignId ? campaignId : null;
        }
    }
}