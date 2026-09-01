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
    /// Контроллер управления перемещением и путешествиями.
    /// Все методы требуют аутентификации.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TravelController(TravelService travelService) : GameControllerBase
    {
        private readonly TravelService _travelService = travelService ?? throw new ArgumentNullException(nameof(travelService));

        /// <summary>
        /// Перемещает персонажа на тактической карте (в футах).
        /// </summary>
        [HttpPost("move")]
        public async Task<IActionResult> MoveCharacter(
            [FromBody] MoveCharacterRequest request,
            CancellationToken cancellationToken)
        {
            if (request.CharacterId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });

            try
            {
                await _travelService.MoveCharacterAsync(
                    request.CharacterId,
                    request.TargetX,
                    request.TargetY,
                    cancellationToken);
                return Ok(new { message = "Персонаж успешно перемещён." });
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
        /// Использует действие Dash (удвоение скорости на текущий ход).
        /// </summary>
        [HttpPost("dash")]
        public async Task<IActionResult> Dash(
            [FromBody] DashRequest request,
            CancellationToken cancellationToken)
        {
            if (request.CharacterId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });

            try
            {
                await _travelService.DashAsync(request.CharacterId, cancellationToken);
                return Ok(new { message = "Рывок использован." });
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
        /// Выполняет специальное перемещение (Climb, Swim, Fly, Burrow).
        /// </summary>
        [HttpPost("special-movement")]
        public async Task<IActionResult> SpecialMovement(
            [FromBody] SpecialMovementRequest request,
            CancellationToken cancellationToken)
        {
            if (request.CharacterId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(request.MovementType))
                return BadRequest(new { error = "Тип перемещения не может быть пустым." });
            if (request.DistanceFeet <= 0)
                return BadRequest(new { error = "Дистанция должна быть положительной." });

            try
            {
                await _travelService.SpecialMovementAsync(
                    request.CharacterId,
                    request.DistanceFeet,
                    request.MovementType,
                    cancellationToken);
                return Ok(new { message = $"Специальное перемещение ({request.MovementType}) выполнено." });
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
        /// Начинает путешествие группы по глобальной карте.
        /// </summary>
        [HttpPost("journey/start")]
        public async Task<IActionResult> StartJourney(
            [FromBody] StartJourneyRequest request,
            CancellationToken cancellationToken)
        {
            if (request.PartyId == Guid.Empty || request.RouteId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы группы и маршрута не могут быть пустыми." });

            try
            {
                await _travelService.StartJourneyAsync(
                    request.PartyId,
                    request.RouteId,
                    request.Pace,
                    cancellationToken);
                return Ok(new { message = "Путешествие начато." });
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
        /// Завершает путешествие.
        /// </summary>
        [HttpPost("journey/end")]
        public async Task<IActionResult> EndJourney(
            [FromBody] EndJourneyRequest request,
            CancellationToken cancellationToken)
        {
            if (request.PartyId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор группы не может быть пустым." });

            try
            {
                await _travelService.EndJourneyAsync(request.PartyId, cancellationToken);
                return Ok(new { message = "Путешествие завершено." });
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
        /// Проходит один день пути.
        /// </summary>
        [HttpPost("journey/day")]
        public async Task<IActionResult> TravelDay(
            [FromBody] TravelDayRequest request,
            CancellationToken cancellationToken)
        {
            if (request.PartyId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор группы не может быть пустым." });
            if (request.HoursTraveled < 0)
                return BadRequest(new { error = "Количество часов не может быть отрицательным." });

            try
            {
                await _travelService.TravelDayAsync(
                    request.PartyId,
                    request.Terrain,
                    request.HoursTraveled,
                    request.NavigationCheckResult,
                    cancellationToken);
                return Ok(new { message = "День путешествия пройден." });
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
        /// Возвращает скорость персонажа.
        /// </summary>
        [HttpGet("speed/{characterId:guid}")]
        public async Task<IActionResult> GetSpeed(
            Guid characterId,
            CancellationToken cancellationToken)
        {
            if (characterId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });

            try
            {
                var speed = await _travelService.GetCharacterSpeedAsync(characterId, cancellationToken);
                return Ok(new { characterId, speed });
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
    }
}