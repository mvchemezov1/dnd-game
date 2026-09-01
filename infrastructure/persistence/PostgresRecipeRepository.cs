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
    public class PostgresRecipeRepository : PostgresRepositoryBase, IRecipeRepository
    {
        public PostgresRecipeRepository(string connectionString, ILogger<PostgresRecipeRepository> logger)
            : base(connectionString, logger) { }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<CraftingRecipe?> GetByIdAsync(Guid recipeId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT recipe_id, name, description, item_id, item_name, gold_cost,
                       crafting_time_hours, required_tool, required_proficiency_level,
                       is_magical, required_spell_id, difficulty_class, associated_skill,
                       components
                FROM crafting_recipes
                WHERE recipe_id = @id";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", recipeId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapRecipe(reader);
            }
            return null;
        }

        public async Task<List<CraftingRecipe>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM crafting_recipes";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var recipes = new List<CraftingRecipe>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                recipes.Add(MapRecipe(reader));
            }
            return recipes;
        }

        public async Task AddAsync(CraftingRecipe recipe, CancellationToken cancellationToken = default)
        {
            const string sql = @"
        INSERT INTO crafting_recipes (recipe_id, name, description, item_id, item_name,
            gold_cost, crafting_time_hours, required_tool, required_proficiency_level,
            is_magical, required_spell_id, difficulty_class, associated_skill, components)
        VALUES (@id, @name, @description, @itemId, @itemName, @goldCost, @timeHours,
            @tool, @profLevel, @isMagical, @spellId, @dc, @skill, @components::jsonb)
        ON CONFLICT (recipe_id) DO UPDATE SET
            name = EXCLUDED.name,
            description = EXCLUDED.description,
            item_id = EXCLUDED.item_id,
            item_name = EXCLUDED.item_name,
            gold_cost = EXCLUDED.gold_cost,
            crafting_time_hours = EXCLUDED.crafting_time_hours,
            required_tool = EXCLUDED.required_tool,
            required_proficiency_level = EXCLUDED.required_proficiency_level,
            is_magical = EXCLUDED.is_magical,
            required_spell_id = EXCLUDED.required_spell_id,
            difficulty_class = EXCLUDED.difficulty_class,
            associated_skill = EXCLUDED.associated_skill,
            components = EXCLUDED.components";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("id", recipe.RecipeId);
                cmd.Parameters.AddWithValue("name", recipe.Name);
                cmd.Parameters.AddWithValue("description", recipe.Description);
                cmd.Parameters.AddWithValue("itemId", recipe.ItemId);
                cmd.Parameters.AddWithValue("itemName", recipe.ItemName);
                cmd.Parameters.AddWithValue("goldCost", recipe.GoldCost);
                cmd.Parameters.AddWithValue("timeHours", recipe.CraftingTimeHours);
                cmd.Parameters.AddWithValue("tool", recipe.RequiredTool);
                cmd.Parameters.AddWithValue("profLevel", recipe.RequiredProficiencyLevel);
                cmd.Parameters.AddWithValue("isMagical", recipe.IsMagical);
                cmd.Parameters.AddWithValue("spellId", (object?)recipe.RequiredSpellId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("dc", recipe.DifficultyClass);
                cmd.Parameters.AddWithValue("skill", (object?)recipe.AssociatedSkill ?? DBNull.Value);
                cmd.Parameters.AddWithValue("components", JsonSerializer.Serialize(recipe.Components, JsonOptions));
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<CraftingRecipe>> GetByToolAsync(string toolName, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM crafting_recipes WHERE required_tool = @tool";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("tool", toolName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var recipes = new List<CraftingRecipe>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                recipes.Add(MapRecipe(reader));
            }
            return recipes;
        }

        public async Task<List<CraftingRecipe>> GetBySpellAsync(string spellId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM crafting_recipes WHERE required_spell_id = @spell";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("spell", spellId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var recipes = new List<CraftingRecipe>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                recipes.Add(MapRecipe(reader));
            }
            return recipes;
        }
        private static CraftingRecipe MapRecipe(NpgsqlDataReader reader)
        {
            var componentsJson = reader.GetString(13);
            var components = JsonSerializer.Deserialize<List<CraftingComponent>>(componentsJson, JsonOptions)
                             ?? new List<CraftingComponent>();

            return new CraftingRecipe
            {
                RecipeId = reader.GetGuid(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                ItemId = reader.GetString(3),
                ItemName = reader.GetString(4),
                GoldCost = reader.GetInt32(5),
                CraftingTimeHours = reader.GetInt32(6),
                RequiredTool = reader.GetString(7),
                RequiredProficiencyLevel = reader.GetInt32(8),
                IsMagical = reader.GetBoolean(9),
                RequiredSpellId = reader.IsDBNull(10) ? null : reader.GetString(10),
                DifficultyClass = reader.GetInt32(11),
                AssociatedSkill = reader.IsDBNull(12) ? null : reader.GetString(12),
                Components = components
            };
        }
    }
}