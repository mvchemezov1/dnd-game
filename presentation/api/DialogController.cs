using dnd_game.application.security;
using dnd_game.application.services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api
{
    /// <summary>
    /// Контроллер управления диалогами между персонажами и NPC.
    /// Все методы требуют аутентификации.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DialogController(DialogService dialogService, IDialogueRepository dialogueRepository, PermissionChecker permissionChecker) : GameControllerBase
    {
        private readonly DialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        private readonly IDialogueRepository _dialogueRepository = dialogueRepository ?? throw new ArgumentNullException(nameof(dialogueRepository));
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));

        /// <summary>
        /// Начинает диалог между персонажем и NPC.
        /// </summary>
        /// <param name="request">Данные запроса: идентификаторы диалога, NPC и персонажа.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpPost("start")]
        public async Task<IActionResult> StartDialog(
            [FromBody] StartDialogRequest request,
            CancellationToken cancellationToken)
        {
            if (request.DialogueId == Guid.Empty || request.NpcId == Guid.Empty || request.CharacterId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы диалога, NPC и персонажа не могут быть пустыми." });

            try
            {
                var state = await _dialogService.StartDialogueAsync(
                    request.DialogueId,
                    request.NpcId,
                    request.CharacterId,
                    cancellationToken);
                return Ok(state);
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
        /// Выбирает вариант ответа в диалоге.
        /// </summary>
        /// <param name="request">Данные запроса: идентификатор диалога и вариант ответа.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpPost("option")]
        public async Task<IActionResult> SelectOption(
            [FromBody] SelectOptionRequest request,
            CancellationToken cancellationToken)
        {
            if (request.DialogueId == Guid.Empty || request.OptionId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор диалога или варианта ответа не может быть пустым." });

            try
            {
                var state = await _dialogService.SelectOptionAsync(
                    request.DialogueId,
                    request.OptionId,
                    cancellationToken);
                return Ok(state);
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
        /// Возвращает текущее состояние диалога (текст NPC и варианты ответов).
        /// </summary>
        /// <param name="dialogueId">Идентификатор активного диалога.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("state/{dialogueId:guid}")]
        public async Task<IActionResult> GetState(
            Guid dialogueId,
            CancellationToken cancellationToken)
        {
            if (dialogueId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор диалога не может быть пустым." });

            try
            {
                var node = await _dialogService.GetCurrentDialogueNodeAsync(dialogueId, cancellationToken);
                if (node == null)
                    return NotFound(new { error = "Диалог не найден или не активен." });

                return Ok(node);
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
        /// Принудительно завершает диалог.
        /// </summary>
        /// <param name="request">Данные запроса: идентификатор диалога.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpPost("end")]
        public async Task<IActionResult> EndDialog(
            [FromBody] EndDialogRequest request,
            CancellationToken cancellationToken)
        {
            if (request.DialogueId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор диалога не может быть пустым." });

            try
            {
                await _dialogService.EndDialogueAsync(request.DialogueId, cancellationToken);
                return Ok(new { message = "Диалог завершён." });
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

        /// <summary>Создаёт новый диалог с корневым узлом. Только GameMaster или Admin.</summary>
        [HttpPost("create")]
        public async Task<IActionResult> CreateDialogue(
            [FromBody] CreateDialogueRequest request,
            CancellationToken cancellationToken)
        {
            if (request.DialogueId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор диалога обязателен." });
            if (string.IsNullOrWhiteSpace(request.NpcText))
                return BadRequest(new { error = "Текст NPC обязателен." });

            if (!await _permissionChecker.IsGameMasterAsync(cancellationToken))
                return Forbid();

            var rootNode = new DialogueNode
            {
                NodeId = Guid.NewGuid(),
                NpcText = request.NpcText,
                IsExitNode = request.IsExitNode,
                Options = request.Options ?? new List<DialogueOption>()
            };

            await _dialogueRepository.AddNodeAsync(
                request.DialogueId,
                rootNode,
                isRoot: true,
                cancellationToken: cancellationToken);

            return CreatedAtAction(nameof(GetState), new { dialogueId = request.DialogueId }, null);
        }

        /// <summary>Добавляет узел к существующему диалогу. Только GameMaster или Admin.</summary>
        [HttpPost("{dialogueId:guid}/nodes")]
        public async Task<IActionResult> AddNode(
            Guid dialogueId,
            [FromBody] AddDialogueNodeRequest request,
            CancellationToken cancellationToken)
        {
            if (dialogueId == Guid.Empty || request.NodeId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы диалога и узла обязательны." });
            if (string.IsNullOrWhiteSpace(request.NpcText))
                return BadRequest(new { error = "Текст NPC обязателен." });

            if (!await _permissionChecker.IsGameMasterAsync(cancellationToken))
                return Forbid();

            var node = new DialogueNode
            {
                NodeId = request.NodeId,
                NpcText = request.NpcText,
                IsExitNode = request.IsExitNode,
                Options = request.Options ?? new List<DialogueOption>()
            };

            await _dialogueRepository.AddNodeAsync(
                dialogueId,
                node,
                isRoot: request.IsRoot,
                cancellationToken: cancellationToken);

            return Ok(new { message = "Узел добавлен." });
        }

        /// <summary>Устанавливает корневой узел диалога. Только GameMaster или Admin.</summary>
        [HttpPost("{dialogueId:guid}/root")]
        public async Task<IActionResult> SetRoot(
            Guid dialogueId,
            [FromBody] SetDialogueRootRequest request,
            CancellationToken cancellationToken)
        {
            if (dialogueId == Guid.Empty || request.NodeId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы диалога и узла обязательны." });

            if (!await _permissionChecker.IsGameMasterAsync(cancellationToken))
                return Forbid();

            await _dialogueRepository.SetRootNodeAsync(dialogueId, request.NodeId, cancellationToken);
            return Ok(new { message = "Корневой узел обновлён." });
        }
    }
}