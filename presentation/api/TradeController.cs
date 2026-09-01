#nullable enable
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
    /// Контроллер управления торговыми операциями между персонажами.
    /// Все методы требуют аутентификации.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TradeController(TradeService tradeService) : GameControllerBase
    {
        private readonly TradeService _tradeService = tradeService ?? throw new ArgumentNullException(nameof(tradeService));

        /// <summary>
        /// Создаёт предложение обмена между двумя персонажами.
        /// </summary>
        /// <param name="request">Данные предложения.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpPost("offer")]
        public async Task<IActionResult> ProposeTrade(
            [FromBody] ProposeTradeRequest request,
            CancellationToken cancellationToken)
        {
            // Валидация основных полей
            if (request.FromCharacterId == Guid.Empty || request.ToCharacterId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы персонажей не могут быть пустыми." });
            if (request.OfferedItems == null || request.RequestedItems == null)
                return BadRequest(new { error = "Списки предлагаемых и запрашиваемых предметов обязательны." });
            if (request.OfferedGold < 0 || request.RequestedGold < 0)
                return BadRequest(new { error = "Количество золота не может быть отрицательным." });

            try
            {
                var offer = await _tradeService.ProposeTradeAsync(
                    request.FromCharacterId,
                    request.ToCharacterId,
                    request.OfferedItems,
                    request.OfferedGold,
                    request.RequestedItems,
                    request.RequestedGold,
                    cancellationToken);

                return Ok(offer);
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
        /// Принимает предложение обмена.
        /// </summary>
        /// <param name="request">Идентификатор предложения.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpPost("accept")]
        public async Task<IActionResult> AcceptTrade(
            [FromBody] AcceptTradeRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OfferId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор предложения не может быть пустым." });

            try
            {
                await _tradeService.AcceptTradeAsync(request.OfferId, cancellationToken);
                return Ok(new { message = "Предложение успешно принято." });
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
        /// Отклоняет предложение обмена.
        /// </summary>
        /// <param name="request">Идентификатор предложения.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpPost("decline")]
        public async Task<IActionResult> DeclineTrade(
            [FromBody] DeclineTradeRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OfferId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор предложения не может быть пустым." });

            try
            {
                await _tradeService.DeclineTradeAsync(request.OfferId, cancellationToken);
                return Ok(new { message = "Предложение отклонено." });
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
        /// Отменяет исходящее предложение обмена.
        /// </summary>
        /// <param name="request">Идентификатор предложения.</param>
        /// <param name="cancellationToken">Токен отмены запроса.</param>
        [HttpPost("cancel")]
        public async Task<IActionResult> CancelTradeOffer(
            [FromBody] CancelTradeOfferRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OfferId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор предложения не может быть пустым." });

            try
            {
                await _tradeService.CancelTradeOfferAsync(request.OfferId, cancellationToken);
                return Ok(new { message = "Предложение отменено." });
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

        /// <summary>Возвращает торговые предложения, связанные с персонажами текущего пользователя.</summary>
        [HttpGet("offers")]
        public async Task<IActionResult> GetOffers(CancellationToken cancellationToken)
        {
            try
            {
                var offers = await _tradeService.GetOffersAsync(cancellationToken);
                return Ok(offers);
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
    }
}