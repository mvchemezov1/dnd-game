using dnd_game.application.projections; // для CharacterProjection
using dnd_game.application.security;   // PermissionChecker, PolicyEnforcer
using dnd_game.domain.commands;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.application.services
{
    /// <summary>
    /// Рецепт изготовления предмета (обычного, магического, зелья, свитка).
    /// </summary>
    public class CraftingRecipe
    {
        public Guid RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int GoldCost { get; set; }
        public List<CraftingComponent> Components { get; set; } = [];
        public int CraftingTimeHours { get; set; }
        public string RequiredTool { get; set; } = string.Empty;
        public int RequiredProficiencyLevel { get; set; } = 0;
        public bool IsMagical { get; set; }
        public string? RequiredSpellId { get; set; }
        public int DifficultyClass { get; set; } = 10;
        public string? AssociatedSkill { get; set; }
    }

    /// <summary>
    /// Компонент, необходимый для изготовления предмета.
    /// </summary>
    public class CraftingComponent
    {
        public string ComponentId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Активный процесс изготовления предмета.
    /// </summary>
    public class ActiveCraftingProcess
    {
        public Guid ProcessId { get; set; }
        public Guid CharacterId { get; set; }
        public Guid RecipeId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EstimatedCompletion { get; set; }
        public int TotalHours { get; set; }
        public int ElapsedHours { get; set; }
    }

    /// <summary>
    /// Репозиторий рецептов.
    /// </summary>
    public interface IRecipeRepository
    {
        Task<CraftingRecipe?> GetByIdAsync(Guid recipeId, CancellationToken cancellationToken = default);
        Task<List<CraftingRecipe>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<List<CraftingRecipe>> GetByToolAsync(string toolName, CancellationToken cancellationToken = default);
        Task<List<CraftingRecipe>> GetBySpellAsync(string spellId, CancellationToken cancellationToken = default);
        /// <summary>Добавляет новый рецепт. Если рецепт с таким ID уже существует, обновляет его.</summary>
        Task AddAsync(CraftingRecipe recipe, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Репозиторий процессов изготовления.
    /// </summary>
    public interface ICraftingProcessRepository
    {
        Task<List<ActiveCraftingProcess>> GetActiveForCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);
        Task<ActiveCraftingProcess?> GetByIdAsync(Guid processId, CancellationToken cancellationToken = default);
        Task AddAsync(ActiveCraftingProcess process, CancellationToken cancellationToken = default);
        Task RemoveAsync(Guid processId, CancellationToken cancellationToken = default);
        Task UpdateAsync(ActiveCraftingProcess process, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Сервис изготовления предметов.
    /// Содержит бизнес-логику проверки доступности рецептов, начала, продвижения,
    /// завершения и отмены процессов крафта.
    /// </summary>
    public class CraftingService(
        ICommandBus commandBus,
        CharacterProjection characterProjection,
        IRecipeRepository recipeRepository,
        ICraftingProcessRepository processRepository,
        PermissionChecker permissionChecker,
        ILogger<CraftingService>? logger = null)
    {
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly CharacterProjection _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
        private readonly IRecipeRepository _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
        private readonly ICraftingProcessRepository _processRepository = processRepository ?? throw new ArgumentNullException(nameof(processRepository));
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));
        private readonly ILogger<CraftingService> _logger = logger ?? NullLogger<CraftingService>.Instance;

        /// <summary>
        /// Проверяет, что идентификатор не пустой.
        /// </summary>
        private static void ValidateGuid(Guid id, string paramName)
        {
            if (id == Guid.Empty)
                throw new ArgumentException($"Идентификатор не должен быть пустым: {paramName}", paramName);
        }

        /// <summary>
        /// Получает персонажа и проверяет право на его просмотр.
        /// </summary>
        private async Task<CharacterDto> GetViewableCharacterAsync(Guid characterId, CancellationToken ct)
        {
            if (!await _permissionChecker.CanViewCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для просмотра этого персонажа.");

            var character = await _characterProjection.GetById(characterId, ct)
                            ?? throw new InvalidOperationException("Персонаж не найден.");
            return character;
        }

        /// <summary>
        /// Продвигает время крафта на указанное количество часов.
        /// Если крафт завершается и рецепт требует проверку навыка, необходимо передать её результат.
        /// </summary>
        /// <param name="processId">Идентификатор процесса крафта.</param>
        /// <param name="hours">Количество часов для продвижения.</param>
        /// <param name="skillCheckSuccess">
        /// Результат проверки навыка (true — успех, false — провал).
        /// Обязателен, если рецепт имеет DifficultyClass > 0 и крафт достигает завершения.
        /// </param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task AdvanceCraftingTimeAsync(
            Guid processId,
            int hours,
            bool? skillCheckSuccess = null,
            CancellationToken cancellationToken = default)
        {
            ValidateGuid(processId, nameof(processId));
            if (hours <= 0)
                throw new ArgumentOutOfRangeException(nameof(hours), "Количество часов должно быть положительным.");
            cancellationToken.ThrowIfCancellationRequested();

            var process = await _processRepository.GetByIdAsync(processId, cancellationToken)
                          ?? throw new InvalidOperationException("Процесс крафта не найден.");

            process.ElapsedHours += hours;

            if (process.ElapsedHours >= process.TotalHours)
            {
                await CompleteCraftingAsync(process, skillCheckSuccess, cancellationToken);
            }
            else
            {
                await _processRepository.UpdateAsync(process, cancellationToken);
            }
        }

        /// <summary>
        /// Завершает крафт, выполняя (при необходимости) проверку навыка.
        /// </summary>
        private async Task CompleteCraftingAsync(
            ActiveCraftingProcess process,
            bool? skillCheckSuccess,
            CancellationToken cancellationToken)
        {
            var recipe = await _recipeRepository.GetByIdAsync(process.RecipeId, cancellationToken)
                         ?? throw new InvalidOperationException("Рецепт не найден.");

            // Если рецепт требует проверку навыка
            if (recipe.DifficultyClass > 0)
            {
                if (skillCheckSuccess == null)
                {
                    throw new InvalidOperationException(
                        "Для завершения крафта требуется результат проверки навыка. " +
                        "Используйте AdvanceCraftingTimeAsync с параметром skillCheckSuccess.");
                }

                if (!skillCheckSuccess.Value)
                {
                    // Провал проверки: крафт не удался, предмет не выдаётся, процесс удаляется
                    await _processRepository.RemoveAsync(process.ProcessId, cancellationToken);
                    _logger.LogWarning(
                        "Процесс крафта {ProcessId} провален из-за неудачной проверки навыка.",
                        process.ProcessId);
                    throw new InvalidOperationException("Проверка навыка не пройдена — крафт завершился неудачей.");
                }
            }

            // Успех (проверка пройдена или не требовалась) — выдаём готовый предмет
            await _commandBus.SendAsync(
                new AddInventoryItem(process.CharacterId, recipe.ItemId, recipe.ItemName, 1),
                cancellationToken);

            // Удаляем процесс
            await _processRepository.RemoveAsync(process.ProcessId, cancellationToken);

            _logger.LogInformation(
                "Процесс крафта {ProcessId} завершён, предмет {ItemId} добавлен персонажу {CharacterId}",
                process.ProcessId, recipe.ItemId, process.CharacterId);
        }

        /// <summary>
        /// Получает персонажа и проверяет право на его редактирование (для крафта).
        /// </summary>
        private async Task<CharacterDto> GetEditableCharacterAsync(Guid characterId, CancellationToken ct)
        {
            if (!await _permissionChecker.CanEditCharacterAsync(characterId, ct))
                throw new UnauthorizedAccessException("У вас нет прав для редактирования этого персонажа.");

            var character = await _characterProjection.GetById(characterId, ct)
                            ?? throw new InvalidOperationException("Персонаж не найден.");
            return character;
        }

        /// <summary>
        /// Проверяет, соответствует ли персонаж требованиям рецепта (без повторной проверки прав).
        /// </summary>
        private static bool IsRecipeAvailableForCharacter(CraftingRecipe recipe, CharacterDto character)
        {
            // Проверка требуемого инструмента (владение навыком/инструментом)
            if (!string.IsNullOrEmpty(recipe.RequiredTool))
            {
                if (!character.SkillProficiencies.ContainsKey(recipe.RequiredTool))
                    return false;
            }

            // Проверка минимального уровня
            if (recipe.RequiredProficiencyLevel > 0 && character.Level < recipe.RequiredProficiencyLevel)
                return false;

            // Проверка наличия заклинания
            if (!string.IsNullOrEmpty(recipe.RequiredSpellId))
            {
                if (!character.KnownSpells.Contains(recipe.RequiredSpellId))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Получить все рецепты, доступные персонажу с учётом навыков, инструментов и заклинаний.
        /// </summary>
        public async Task<List<CraftingRecipe>> GetAvailableRecipesAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            var character = await GetViewableCharacterAsync(characterId, cancellationToken);
            var allRecipes = await _recipeRepository.GetAllAsync(cancellationToken);

            var available = allRecipes
                .Where(recipe => IsRecipeAvailableForCharacter(recipe, character))
                .ToList();

            _logger.LogDebug("Для персонажа {CharacterId} доступно рецептов: {Count}", characterId, available.Count);
            return available;
        }

        /// <summary>
        /// Начать изготовление предмета.
        /// </summary>
        public async Task<ActiveCraftingProcess> StartCraftingAsync(Guid characterId, Guid recipeId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            ValidateGuid(recipeId, nameof(recipeId));
            cancellationToken.ThrowIfCancellationRequested();

            // Проверяем право на редактирование (крафт изменяет инвентарь и золото)
            var character = await GetEditableCharacterAsync(characterId, cancellationToken);

            var recipe = await _recipeRepository.GetByIdAsync(recipeId, cancellationToken)
                         ?? throw new InvalidOperationException("Рецепт не найден.");

            // Проверяем доступность рецепта для данного персонажа
            if (!IsRecipeAvailableForCharacter(recipe, character))
                throw new InvalidOperationException("Персонаж не соответствует требованиям для этого рецепта.");

            // Проверяем золото
            if (recipe.GoldCost > 0 && character.Gold < recipe.GoldCost)
                throw new InvalidOperationException("Недостаточно золота.");

            // Проверяем наличие компонентов
            foreach (var comp in recipe.Components)
            {
                var inventoryItem = character.Inventory.FirstOrDefault(i => i.ItemId == comp.ComponentId);
                if (inventoryItem == null || inventoryItem.Quantity < comp.Quantity)
                    throw new InvalidOperationException($"Не хватает компонента: {comp.Name}");
            }

            // Списываем золото и компоненты через команды
            if (recipe.GoldCost > 0)
                await _commandBus.SendAsync(new SpendGold(characterId, recipe.GoldCost));

            foreach (var comp in recipe.Components)
            {
                await _commandBus.SendAsync(new RemoveInventoryItem(characterId, comp.ComponentId, comp.Quantity));
            }

            // Создаём процесс крафта
            var process = new ActiveCraftingProcess
            {
                ProcessId = Guid.NewGuid(),
                CharacterId = characterId,
                RecipeId = recipeId,
                StartedAt = DateTime.UtcNow,
                TotalHours = recipe.CraftingTimeHours,
                ElapsedHours = 0,
                EstimatedCompletion = DateTime.UtcNow.AddHours(recipe.CraftingTimeHours)
            };

            await _processRepository.AddAsync(process, cancellationToken);
            _logger.LogInformation("Начат процесс крафта {ProcessId} для персонажа {CharacterId}, рецепт {RecipeId}",
                process.ProcessId, characterId, recipeId);

            return process;
        }

        /// <summary>
        /// Продвинуть время крафта на указанное количество часов.
        /// </summary>
        public async Task AdvanceCraftingTimeAsync(Guid processId, int hours, CancellationToken cancellationToken = default)
        {
            ValidateGuid(processId, nameof(processId));
            if (hours <= 0)
                throw new ArgumentOutOfRangeException(nameof(hours), "Количество часов должно быть положительным.");
            cancellationToken.ThrowIfCancellationRequested();

            var process = await _processRepository.GetByIdAsync(processId, cancellationToken)
                          ?? throw new InvalidOperationException("Процесс крафта не найден.");

            process.ElapsedHours += hours;
            if (process.ElapsedHours >= process.TotalHours)
            {
                await CompleteCraftingAsync(process, cancellationToken);
            }
            else
            {
                await _processRepository.UpdateAsync(process, cancellationToken);
            }
        }

        /// <summary>
        /// Завершить изготовление и выдать предмет.
        /// </summary>
        private async Task CompleteCraftingAsync(ActiveCraftingProcess process, CancellationToken cancellationToken)
        {
            var recipe = await _recipeRepository.GetByIdAsync(process.RecipeId, cancellationToken)
                         ?? throw new InvalidOperationException("Рецепт не найден.");

            // Здесь может быть проверка навыка (сложность), пока опускаем

            // Выдаём готовый предмет
            await _commandBus.SendAsync(new AddInventoryItem(process.CharacterId, recipe.ItemId, recipe.ItemName, 1));

            // Удаляем процесс
            await _processRepository.RemoveAsync(process.ProcessId, cancellationToken);

            _logger.LogInformation("Процесс крафта {ProcessId} завершён, предмет {ItemId} добавлен персонажу {CharacterId}",
                process.ProcessId, recipe.ItemId, process.CharacterId);
        }

        /// <summary>
        /// Отменить крафт и вернуть половину золота.
        /// </summary>
        public async Task CancelCraftingAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(processId, nameof(processId));
            cancellationToken.ThrowIfCancellationRequested();

            var process = await _processRepository.GetByIdAsync(processId, cancellationToken)
                          ?? throw new InvalidOperationException("Процесс крафта не найден.");

            var recipe = await _recipeRepository.GetByIdAsync(process.RecipeId, cancellationToken)
                         ?? throw new InvalidOperationException("Рецепт не найден.");

            // Возвращаем половину стоимости золота (целочисленное деление)
            int refundGold = recipe.GoldCost / 2;
            if (refundGold > 0)
                await _commandBus.SendAsync(new AddGold(process.CharacterId, refundGold));

            await _processRepository.RemoveAsync(process.ProcessId, cancellationToken);
            _logger.LogInformation("Процесс крафта {ProcessId} отменён, возвращено золото: {RefundGold}", process.ProcessId, refundGold);
        }

        /// <summary>
        /// Получить список активных процессов крафта для персонажа.
        /// </summary>
        public async Task<List<ActiveCraftingProcess>> GetActiveCraftingProcessesAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            ValidateGuid(characterId, nameof(characterId));
            cancellationToken.ThrowIfCancellationRequested();

            // Достаточно проверить право на просмотр персонажа
            await GetViewableCharacterAsync(characterId, cancellationToken);

            return await _processRepository.GetActiveForCharacterAsync(characterId, cancellationToken);
        }
    }
}