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
    public class PostgresDialogueRepository : PostgresRepositoryBase, IDialogueRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PostgresDialogueRepository(string connectionString, ILogger<PostgresDialogueRepository> logger)
            : base(connectionString, logger) { }

        public async Task<DialogueNode?> GetRootNodeAsync(Guid dialogueId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT n.node_id, n.dialogue_id, n.npc_text, n.is_exit_node, n.options
                FROM dialogue_roots r
                JOIN dialogue_nodes n ON r.root_node_id = n.node_id
                WHERE r.dialogue_id = @dialogueId";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("dialogueId", dialogueId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapNode(reader);
            }
            return null;
        }

        public async Task<DialogueNode?> GetNodeAsync(Guid dialogueId, Guid nodeId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT node_id, dialogue_id, npc_text, is_exit_node, options
                FROM dialogue_nodes
                WHERE dialogue_id = @dialogueId AND node_id = @nodeId";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("dialogueId", dialogueId);
            command.Parameters.AddWithValue("nodeId", nodeId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapNode(reader);
            }
            return null;
        }

        private static DialogueNode MapNode(NpgsqlDataReader reader)
        {
            var optionsJson = reader.GetString(4);
            var options = JsonSerializer.Deserialize<List<DialogueOption>>(optionsJson, JsonOptions)
                          ?? new List<DialogueOption>();

            return new DialogueNode
            {
                NodeId = reader.GetGuid(0),
                NpcText = reader.GetString(2),
                IsExitNode = reader.GetBoolean(3),
                Options = options
            };
        }

        public async Task AddNodeAsync(Guid dialogueId, DialogueNode node, bool isRoot = false, CancellationToken cancellationToken = default)
        {
            if (dialogueId == Guid.Empty)
                throw new ArgumentException("Идентификатор диалога не может быть пустым.", nameof(dialogueId));
            if (node == null)
                throw new ArgumentNullException(nameof(node));
            if (node.NodeId == Guid.Empty)
                throw new ArgumentException("Идентификатор узла не может быть пустым.", nameof(node));

            const string insertNodeSql = @"
        INSERT INTO dialogue_nodes (node_id, dialogue_id, npc_text, is_exit_node, options)
        VALUES (@nodeId, @dialogueId, @npcText, @isExit, @options::jsonb)
        ON CONFLICT (node_id) DO UPDATE SET
            dialogue_id = EXCLUDED.dialogue_id,
            npc_text = EXCLUDED.npc_text,
            is_exit_node = EXCLUDED.is_exit_node,
            options = EXCLUDED.options";

            await ExecuteNonQueryAsync(insertNodeSql, cmd =>
            {
                cmd.Parameters.AddWithValue("nodeId", node.NodeId);
                cmd.Parameters.AddWithValue("dialogueId", dialogueId);
                cmd.Parameters.AddWithValue("npcText", node.NpcText);
                cmd.Parameters.AddWithValue("isExit", node.IsExitNode);
                cmd.Parameters.AddWithValue("options", JsonSerializer.Serialize(node.Options, JsonOptions));
            }, cancellationToken).ConfigureAwait(false);

            if (isRoot)
            {
                await SetRootNodeAsync(dialogueId, node.NodeId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Если корень ещё не назначен, делаем этот узел корневым
                var rootExists = await CheckRootExistsAsync(dialogueId, cancellationToken).ConfigureAwait(false);
                if (!rootExists)
                {
                    await SetRootNodeAsync(dialogueId, node.NodeId, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public async Task SetRootNodeAsync(Guid dialogueId, Guid nodeId, CancellationToken cancellationToken = default)
        {
            if (dialogueId == Guid.Empty)
                throw new ArgumentException("Идентификатор диалога не может быть пустым.", nameof(dialogueId));
            if (nodeId == Guid.Empty)
                throw new ArgumentException("Идентификатор узла не может быть пустым.", nameof(nodeId));

            // Проверяем, что узел существует в этом диалоге
            const string checkSql = "SELECT 1 FROM dialogue_nodes WHERE dialogue_id = @dialogueId AND node_id = @nodeId";
            var exists = await ExecuteScalarAsync(checkSql, cmd =>
            {
                cmd.Parameters.AddWithValue("dialogueId", dialogueId);
                cmd.Parameters.AddWithValue("nodeId", nodeId);
            }, cancellationToken).ConfigureAwait(false);

            if (exists == null)
                throw new InvalidOperationException($"Узел {nodeId} не найден в диалоге {dialogueId}.");

            const string upsertRootSql = @"
        INSERT INTO dialogue_roots (dialogue_id, root_node_id)
        VALUES (@dialogueId, @nodeId)
        ON CONFLICT (dialogue_id) DO UPDATE SET root_node_id = @nodeId";

            await ExecuteNonQueryAsync(upsertRootSql, cmd =>
            {
                cmd.Parameters.AddWithValue("dialogueId", dialogueId);
                cmd.Parameters.AddWithValue("nodeId", nodeId);
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> CheckRootExistsAsync(Guid dialogueId, CancellationToken ct)
        {
            const string sql = "SELECT 1 FROM dialogue_roots WHERE dialogue_id = @dialogueId";
            var result = await ExecuteScalarAsync(sql, cmd => cmd.Parameters.AddWithValue("dialogueId", dialogueId), ct).ConfigureAwait(false);
            return result != null;
        }
    }
}