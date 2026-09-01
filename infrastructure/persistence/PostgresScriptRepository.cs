#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.infrastructure.ai;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresScriptRepository : PostgresRepositoryBase, IScriptRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PostgresScriptRepository(string connectionString, ILogger<PostgresScriptRepository> logger)
            : base(connectionString, logger) { }

        public async Task<ScriptDefinition?> GetByNameAsync(string scriptName, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT script_name, description, commands FROM scripts WHERE script_name = @name";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("name", scriptName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var commandsJson = reader.GetString(2);
                return new ScriptDefinition
                {
                    ScriptName = reader.GetString(0),
                    Description = reader.GetString(1),
                    Commands = JsonSerializer.Deserialize<List<ScriptCommand>>(commandsJson, JsonOptions) ?? new()
                };
            }
            return null;
        }

        public async Task AddOrUpdateAsync(ScriptDefinition script, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO scripts (script_name, description, commands)
                VALUES (@name, @description, @commands::jsonb)
                ON CONFLICT (script_name) DO UPDATE SET
                    description = EXCLUDED.description,
                    commands = EXCLUDED.commands";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("name", script.ScriptName);
                cmd.Parameters.AddWithValue("description", script.Description);
                cmd.Parameters.AddWithValue("commands",
                    JsonSerializer.Serialize(script.Commands, JsonOptions));
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<string>> GetAllScriptNamesAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT script_name FROM scripts";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var names = new List<string>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                names.Add(reader.GetString(0));
            }
            return names;
        }
    }
}