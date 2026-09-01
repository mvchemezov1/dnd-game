#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.services;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresDialogueStateRepository : PostgresRepositoryBase, IDialogueStateRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PostgresDialogueStateRepository(string connectionString, ILogger<PostgresDialogueStateRepository> logger)
            : base(connectionString, logger) { }

        public async Task<DialogueState?> GetAsync(Guid dialogueId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT dialogue_id, npc_id, character_id, current_node_id, is_active,
                       visited_node_ids, pending_option_id
                FROM dialogue_states
                WHERE dialogue_id = @id";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", dialogueId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapState(reader);
            }
            return null;
        }

        public async Task SaveAsync(DialogueState state, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO dialogue_states
                    (dialogue_id, npc_id, character_id, current_node_id, is_active,
                     visited_node_ids, pending_option_id)
                VALUES
                    (@id, @npcId, @charId, @currentNodeId, @isActive,
                     @visitedNodeIds::jsonb, @pendingOptionId)
                ON CONFLICT (dialogue_id) DO UPDATE SET
                    npc_id = EXCLUDED.npc_id,
                    character_id = EXCLUDED.character_id,
                    current_node_id = EXCLUDED.current_node_id,
                    is_active = EXCLUDED.is_active,
                    visited_node_ids = EXCLUDED.visited_node_ids,
                    pending_option_id = EXCLUDED.pending_option_id";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("id", state.DialogueId);
                cmd.Parameters.AddWithValue("npcId", state.NpcId);
                cmd.Parameters.AddWithValue("charId", state.CharacterId);
                cmd.Parameters.AddWithValue("currentNodeId", state.CurrentNodeId);
                cmd.Parameters.AddWithValue("isActive", state.IsActive);
                cmd.Parameters.AddWithValue("visitedNodeIds",
                    JsonSerializer.Serialize(state.VisitedNodeIds, JsonOptions));
                cmd.Parameters.AddWithValue("pendingOptionId",
                    (object?)state.PendingOptionId ?? DBNull.Value);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(Guid dialogueId, CancellationToken cancellationToken = default)
        {
            const string sql = "DELETE FROM dialogue_states WHERE dialogue_id = @id";
            await ExecuteNonQueryAsync(sql, cmd => cmd.Parameters.AddWithValue("id", dialogueId),
                cancellationToken).ConfigureAwait(false);
        }

        private static DialogueState MapState(NpgsqlDataReader reader)
        {
            var visitedJson = reader.GetString(5);
            var visited = JsonSerializer.Deserialize<List<Guid>>(visitedJson, JsonOptions) ?? new();

            return new DialogueState
            {
                DialogueId = reader.GetGuid(0),
                NpcId = reader.GetGuid(1),
                CharacterId = reader.GetGuid(2),
                CurrentNodeId = reader.GetGuid(3),
                IsActive = reader.GetBoolean(4),
                VisitedNodeIds = visited,
                PendingOptionId = reader.IsDBNull(6) ? null : reader.GetGuid(6)
            };
        }
    }
}