#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.sagas;
using Newtonsoft.Json;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresSagaStateRepository : PostgresRepositoryBase, ISagaStateRepository
    {
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            TypeNameHandling = TypeNameHandling.All,
            NullValueHandling = NullValueHandling.Ignore
        };

        public PostgresSagaStateRepository(string connectionString, ILogger<PostgresSagaStateRepository> logger)
            : base(connectionString, logger) { }

        public async Task<ISagaState?> LoadAsync(Guid sagaId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT state_json FROM saga_states WHERE saga_id = @id";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", sagaId);

            var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonConvert.DeserializeObject<ISagaState>(json, JsonSettings);
        }

        public async Task SaveAsync(ISagaState state, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO saga_states (saga_id, correlation_id, status, version, created_at, updated_at, state_json)
                VALUES (@sagaId, @correlationId, @status, @version, @createdAt, @updatedAt, @stateJson::jsonb)
                ON CONFLICT (saga_id) DO UPDATE SET
                    correlation_id = EXCLUDED.correlation_id,
                    status = EXCLUDED.status,
                    version = EXCLUDED.version,
                    updated_at = EXCLUDED.updated_at,
                    state_json = EXCLUDED.state_json";

            string json = JsonConvert.SerializeObject(state, JsonSettings);
            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("sagaId", state.SagaId);
                cmd.Parameters.AddWithValue("correlationId", state.CorrelationId);
                cmd.Parameters.AddWithValue("status", state.Status.ToString());
                cmd.Parameters.AddWithValue("version", state.Version);
                cmd.Parameters.AddWithValue("createdAt", state.CreatedAt);
                cmd.Parameters.AddWithValue("updatedAt", (object?)state.UpdatedAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("stateJson", json);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default)
        {
            const string sql = "DELETE FROM saga_states WHERE saga_id = @id";
            await ExecuteNonQueryAsync(sql, cmd => cmd.Parameters.AddWithValue("id", sagaId),
                cancellationToken).ConfigureAwait(false);
        }
    }
}