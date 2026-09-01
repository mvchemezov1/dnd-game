#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace dnd_game.infrastructure.seeding
{
    /// <summary>
    /// Заполняет хранилище рецептов начальными данными, если оно пусто.
    /// </summary>
    public class RecipeSeeder
    {
        private readonly IRecipeRepository _recipeRepository;
        private readonly ILogger<RecipeSeeder> _logger;

        public RecipeSeeder(IRecipeRepository recipeRepository, ILogger<RecipeSeeder>? logger = null)
        {
            _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
            _logger = logger ?? NullLogger<RecipeSeeder>.Instance;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            var existing = await _recipeRepository.GetAllAsync(cancellationToken);
            if (existing.Count > 0)
            {
                _logger.LogInformation("Рецепты уже существуют ({Count}), сидинг пропущен.", existing.Count);
                return;
            }

            var recipes = new List<CraftingRecipe>
            {
                new CraftingRecipe
                {
                    RecipeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Простой меч",
                    Description = "Надёжный железный меч.",
                    ItemId = "iron-sword",
                    ItemName = "Железный меч",
                    GoldCost = 50,
                    CraftingTimeHours = 24,
                    RequiredTool = "SmithingTools",
                    RequiredProficiencyLevel = 1,
                    IsMagical = false,
                    DifficultyClass = 10,
                    AssociatedSkill = "Smithing",
                    Components = new List<CraftingComponent>
                    {
                        new CraftingComponent { ComponentId = "iron-ingot", Name = "Железный слиток", Quantity = 2 },
                        new CraftingComponent { ComponentId = "leather", Name = "Кожа", Quantity = 1 }
                    }
                },
                new CraftingRecipe
                {
                    RecipeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Name = "Кожаный доспех",
                    Description = "Лёгкий доспех из выделанной кожи.",
                    ItemId = "leather-armor",
                    ItemName = "Кожаный доспех",
                    GoldCost = 20,
                    CraftingTimeHours = 16,
                    RequiredTool = "LeatherworkingTools",
                    RequiredProficiencyLevel = 1,
                    IsMagical = false,
                    DifficultyClass = 10,
                    AssociatedSkill = "Leatherworking",
                    Components = new List<CraftingComponent>
                    {
                        new CraftingComponent { ComponentId = "leather", Name = "Кожа", Quantity = 3 }
                    }
                },
                new CraftingRecipe
                {
                    RecipeId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Name = "Зелье лечения",
                    Description = "Восстанавливает 2d4+2 хитов.",
                    ItemId = "potion-of-healing",
                    ItemName = "Зелье лечения",
                    GoldCost = 50,
                    CraftingTimeHours = 8,
                    RequiredTool = "AlchemistTools",
                    RequiredProficiencyLevel = 1,
                    IsMagical = true,
                    RequiredSpellId = "cure-wounds",
                    DifficultyClass = 12,
                    AssociatedSkill = "Alchemy",
                    Components = new List<CraftingComponent>
                    {
                        new CraftingComponent { ComponentId = "herb", Name = "Лечебная трава", Quantity = 2 },
                        new CraftingComponent { ComponentId = "water", Name = "Чистая вода", Quantity = 1 }
                    }
                }
            };

            foreach (var recipe in recipes)
            {
                await _recipeRepository.AddAsync(recipe, cancellationToken);
                _logger.LogInformation("Добавлен рецепт: {Name} ({Id})", recipe.Name, recipe.RecipeId);
            }

            _logger.LogInformation("Сидинг рецептов завершён. Добавлено {Count} рецептов.", recipes.Count);
        }
    }
}