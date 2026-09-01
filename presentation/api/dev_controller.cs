using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.event_handlers;
using dnd_game.infrastructure.ai;
using dnd_game.infrastructure.monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace dnd_game.presentation.api
{
    /// <summary>
    /// Диагностические эндпоинты для панели разработчика.
    /// Предоставляют доступ к проверке здоровья, спискам скриптов/вебхуков и воспроизведению событий.
    /// Доступ только для роли Admin (политика "RequireAdmin").
    /// </summary>
    [ApiController]
    [Route("api/dev")]
    [Authorize(Policy = "RequireAdmin")]
    public class DevController(
        IHealthCheck healthCheck,
        IScriptRepository scriptRepository,
        IWebhookSubscriptionRepository webhookRepository,
        IReplayEventStore replayEventStore) : ControllerBase
    {
        private readonly IHealthCheck _healthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        private readonly IScriptRepository _scriptRepository = scriptRepository ?? throw new ArgumentNullException(nameof(scriptRepository));
        private readonly IWebhookSubscriptionRepository _webhookRepository = webhookRepository ?? throw new ArgumentNullException(nameof(webhookRepository));
        private readonly IReplayEventStore _replayEventStore = replayEventStore ?? throw new ArgumentNullException(nameof(replayEventStore));

        /// <summary>
        /// Возвращает состояние БД, EventStore, шины сообщений и распределённых блокировок.
        /// </summary>
        [HttpGet("health")]
        public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
        {
            var result = await _healthCheck.CheckAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Возвращает список зарегистрированных AI-скриптов.
        /// </summary>
        [HttpGet("scripts")]
        public async Task<IActionResult> GetScripts(CancellationToken cancellationToken)
        {
            var scripts = await _scriptRepository.GetAllScriptNamesAsync(cancellationToken);
            return Ok(scripts);
        }

        /// <summary>
        /// Возвращает список зарегистрированных webhook-подписок.
        /// </summary>
        [HttpGet("webhooks")]
        public async Task<IActionResult> GetWebhooks(CancellationToken cancellationToken)
        {
            var subscriptions = await _webhookRepository.GetAllAsync(cancellationToken);
            return Ok(subscriptions);
        }

        /// <summary>
        /// Воспроизводит события конкретного агрегата для отладки.
        /// Можно указать момент времени, до которого выбираются события (включительно).
        /// </summary>
        /// <param name="aggregateId">Идентификатор агрегата.</param>
        /// <param name="toTimestamp">Необязательная временная граница для выборки событий.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("replay/{aggregateId:guid}")]
        public async Task<IActionResult> GetReplay(
            Guid aggregateId,
            DateTime? toTimestamp = null,
            CancellationToken cancellationToken = default)
        {
            if (aggregateId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор агрегата не может быть пустым." });

            var events = await _replayEventStore.GetEventsAsync(aggregateId, toTimestamp, cancellationToken);
            var count = await _replayEventStore.GetEventCountAsync(aggregateId, cancellationToken);

            return Ok(new
            {
                aggregateId,
                eventCount = count,
                events
            });
        }
    }
}