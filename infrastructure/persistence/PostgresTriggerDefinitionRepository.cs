#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.event_handlers;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresTriggerDefinitionRepository : PostgresRepositoryBase, ITriggerDefinitionRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PostgresTriggerDefinitionRepository(string connectionString, ILogger<PostgresTriggerDefinitionRepository> logger)
            : base(connectionString, logger) { }

        public async Task<IEnumerable<TriggerDefinition>> GetByEventAsync(
            string eventName, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT trigger_id, event_name, conditions, actions, is_one_shot,
                       cooldown_seconds, delay_seconds, priority
                FROM trigger_definitions
                WHERE event_name = @event";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("event", eventName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var definitions = new List<TriggerDefinition>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                definitions.Add(MapDefinition(reader));
            }
            return definitions;
        }

        private static TriggerDefinition MapDefinition(NpgsqlDataReader reader)
        {
            var conditionsJson = reader.GetString(2);
            var actionsJson = reader.GetString(3);

            var conditions = JsonSerializer.Deserialize<List<TriggerCondition>>(conditionsJson, JsonOptions)
                             ?? new List<TriggerCondition>();
            var actions = JsonSerializer.Deserialize<List<ScriptAction>>(actionsJson, JsonOptions)
                          ?? new List<ScriptAction>();

            return new TriggerDefinition
            {
                TriggerId = reader.GetGuid(0),
                EventName = reader.GetString(1),
                Conditions = conditions,
                Actions = actions,
                IsOneShot = reader.GetBoolean(4),
                CooldownSeconds = reader.GetInt32(5),
                DelaySeconds = reader.GetInt32(6),
                Priority = reader.GetInt32(7)
            };
        }
    }
}