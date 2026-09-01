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
        public async Task<bool> TrySaveAsync(ISagaState state, int expectedVersion, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE saga_states
                SET correlation_id = @correlationId,
                    status = @status,
                    version = @version,
                    updated_at = @updatedAt,
                    state_json = @stateJson::jsonb
                WHERE saga_id = @sagaId AND version = @expectedVersion;

                INSERT INTO saga_states (saga_id, correlation_id, status, version, created_at, updated_at, state_json)
                SELECT @sagaId, @correlationId, @status, @version, @createdAt, @updatedAt, @stateJson::jsonb
                WHERE NOT EXISTS (SELECT 1 FROM saga_states WHERE saga_id = @sagaId);
            ";

            string json = JsonConvert.SerializeObject(state, JsonSettings);

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);

            command.Parameters.AddWithValue("sagaId", state.SagaId);
            command.Parameters.AddWithValue("correlationId", state.CorrelationId);
            command.Parameters.AddWithValue("status", state.Status.ToString());
            command.Parameters.AddWithValue("version", state.Version);
            command.Parameters.AddWithValue("expectedVersion", expectedVersion);
            command.Parameters.AddWithValue("createdAt", state.CreatedAt);
            command.Parameters.AddWithValue("updatedAt", state.UpdatedAt ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("stateJson", json);

            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return affected > 0;
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