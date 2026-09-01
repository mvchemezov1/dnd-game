#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.event_handlers;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresTriggerStateStore : PostgresRepositoryBase, ITriggerStateStore
    {
        public PostgresTriggerStateStore(string connectionString, ILogger<PostgresTriggerStateStore> logger)
            : base(connectionString, logger) { }

        public async Task<TriggerState?> GetAsync(Guid triggerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT has_been_triggered, last_triggered_utc, cooldown_ends_utc
                FROM trigger_states
                WHERE trigger_id = @id";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", triggerId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new TriggerState
                {
                    HasBeenTriggered = reader.GetBoolean(0),
                    LastTriggeredUtc = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                    CooldownEndsUtc = reader.IsDBNull(2) ? null : reader.GetDateTime(2)
                };
            }
            return null;
        }

        public async Task SaveAsync(Guid triggerId, TriggerState state, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO trigger_states (trigger_id, has_been_triggered, last_triggered_utc, cooldown_ends_utc)
                VALUES (@id, @hasTriggered, @lastTriggered, @cooldownEnds)
                ON CONFLICT (trigger_id) DO UPDATE SET
                    has_been_triggered = EXCLUDED.has_been_triggered,
                    last_triggered_utc = EXCLUDED.last_triggered_utc,
                    cooldown_ends_utc = EXCLUDED.cooldown_ends_utc";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("id", triggerId);
                cmd.Parameters.AddWithValue("hasTriggered", state.HasBeenTriggered);
                cmd.Parameters.AddWithValue("lastTriggered", (object?)state.LastTriggeredUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("cooldownEnds", (object?)state.CooldownEndsUtc ?? DBNull.Value);
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}