using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api
{
    /// <summary>
    /// Контроллер управления крафтом предметов.
    /// Все методы требуют аутентификации.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CraftingController(CraftingService craftingService) : GameControllerBase
    {
        private readonly CraftingService _craftingService = craftingService ?? throw new ArgumentNullException(nameof(craftingService));

        /// <summary>
        /// Возвращает список рецептов, доступных указанному персонажу.
        /// </summary>
        /// <param name="characterId">Идентификатор персонажа.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpGet("recipes")]
        public async Task<IActionResult> GetRecipes(
            [FromQuery] Guid characterId,
            CancellationToken cancellationToken)
        {
            if (characterId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });

            try
            {
                var recipes = await _craftingService.GetAvailableRecipesAsync(characterId, cancellationToken);
                return Ok(recipes);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status499ClientClosedRequest, new { error = "Запрос был отменён." });
            }
        }

        /// <summary>
        /// Запускает процесс крафта для персонажа.
        /// </summary>
        /// <param name="request">Данные запроса: идентификаторы персонажа и рецепта.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpPost("start")]
        public async Task<IActionResult> StartCrafting(
            [FromBody] StartCraftingRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var process = await _craftingService.StartCraftingAsync(
                    request.CharacterId,
                    request.RecipeId,
                    cancellationToken);
                return Ok(process);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status499ClientClosedRequest, new { error = "Запрос был отменён." });
            }
        }

        /// <summary>
        /// Возвращает список активных процессов крафта персонажа.
        /// </summary>
        /// <param name="characterId">Идентификатор персонажа.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpGet("processes")]
        public async Task<IActionResult> GetProcesses(
            [FromQuery] Guid characterId,
            CancellationToken cancellationToken)
        {
            if (characterId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });

            try
            {
                var processes = await _craftingService.GetActiveCraftingProcessesAsync(characterId, cancellationToken);
                return Ok(processes);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { error = ex.Message });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status499ClientClosedRequest, new { error = "Запрос был отменён." });
            }
        }

        /// <summary>
        /// Отменяет активный процесс крафта.
        /// </summary>
        /// <param name="request">Данные запроса: идентификатор процесса.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpPost("cancel")]
        public async Task<IActionResult> CancelCrafting(
            [FromBody] CancelCraftingRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                await _craftingService.CancelCraftingAsync(request.ProcessId, cancellationToken);
                return Ok(new { message = "Крафт успешно отменён." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status499ClientClosedRequest, new { error = "Запрос был отменён." });
            }
        }
    }
}