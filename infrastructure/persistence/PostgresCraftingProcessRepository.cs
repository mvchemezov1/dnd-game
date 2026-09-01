#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.services;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresCraftingProcessRepository : PostgresRepositoryBase, ICraftingProcessRepository
    {
        public PostgresCraftingProcessRepository(string connectionString, ILogger<PostgresCraftingProcessRepository> logger)
            : base(connectionString, logger) { }

        public async Task<List<ActiveCraftingProcess>> GetActiveForCharacterAsync(
            Guid characterId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT process_id, character_id, recipe_id, started_at, total_hours,
                       elapsed_hours, estimated_completion
                FROM crafting_processes
                WHERE character_id = @charId
                ORDER BY started_at DESC";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("charId", characterId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var processes = new List<ActiveCraftingProcess>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                processes.Add(MapProcess(reader));
            }
            return processes;
        }

        public async Task<ActiveCraftingProcess?> GetByIdAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT process_id, character_id, recipe_id, started_at, total_hours,
                       elapsed_hours, estimated_completion
                FROM crafting_processes
                WHERE process_id = @id";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", processId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapProcess(reader);
            }
            return null;
        }

        public async Task AddAsync(ActiveCraftingProcess process, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO crafting_processes
                    (process_id, character_id, recipe_id, started_at, total_hours,
                     elapsed_hours, estimated_completion)
                VALUES
                    (@id, @charId, @recipeId, @startedAt, @totalHours,
                     @elapsedHours, @estimatedCompletion)";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("id", process.ProcessId);
                cmd.Parameters.AddWithValue("charId", process.CharacterId);
                cmd.Parameters.AddWithValue("recipeId", process.RecipeId);
                cmd.Parameters.AddWithValue("startedAt", process.StartedAt);
                cmd.Parameters.AddWithValue("totalHours", process.TotalHours);
                cmd.Parameters.AddWithValue("elapsedHours", process.ElapsedHours);
                cmd.Parameters.AddWithValue("estimatedCompletion", process.EstimatedCompletion);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateAsync(ActiveCraftingProcess process, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE crafting_processes
                SET elapsed_hours = @elapsed,
                    estimated_completion = @estimated
                WHERE process_id = @id";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("id", process.ProcessId);
                cmd.Parameters.AddWithValue("elapsed", process.ElapsedHours);
                cmd.Parameters.AddWithValue("estimated", process.EstimatedCompletion);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            const string sql = "DELETE FROM crafting_processes WHERE process_id = @id";
            await ExecuteNonQueryAsync(sql, cmd => cmd.Parameters.AddWithValue("id", processId),
                cancellationToken).ConfigureAwait(false);
        }

        private static ActiveCraftingProcess MapProcess(NpgsqlDataReader reader)
        {
            return new ActiveCraftingProcess
            {
                ProcessId = reader.GetGuid(0),
                CharacterId = reader.GetGuid(1),
                RecipeId = reader.GetGuid(2),
                StartedAt = reader.GetDateTime(3),
                TotalHours = reader.GetInt32(4),
                ElapsedHours = reader.GetInt32(5),
                EstimatedCompletion = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6)
            };
        }
    }
}