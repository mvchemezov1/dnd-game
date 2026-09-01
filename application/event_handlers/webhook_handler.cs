using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using dnd_game.domain.events;

namespace dnd_game.application.event_handlers
{
    /// <summary>
    /// Описывает подписку на webhook-уведомления.
    /// </summary>
    public class WebhookSubscription
    {
        public Guid Id { get; set; }
        public string EventType { get; set; } = string.Empty; // Например, "CharacterDied", "CombatStarted" или "*" для всех
        public string Url { get; set; } = string.Empty;
        public string? Secret { get; set; } // Для HMAC-подписи
        public int MaxRetries { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 10;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Репозиторий подписок (может быть в БД, конфигурационном файле и т.п.).
    /// </summary>
    public interface IWebhookSubscriptionRepository
    {
        Task<IEnumerable<WebhookSubscription>> GetSubscriptionsForEventAsync(
            string eventType,
            CancellationToken cancellationToken = default);

        // Добавьте этот метод
        Task<IEnumerable<WebhookSubscription>> GetAllAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Клиент отправки HTTP-уведомлений с поддержкой повторных попыток.
    /// </summary>
    public interface IWebhookClient
    {
        Task SendAsync(WebhookSubscription subscription, object payload, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Реализация <see cref="IWebhookClient"/> с использованием <see cref="HttpClient"/>.
    /// </summary>
    public class DefaultWebhookClient(HttpClient httpClient, ILogger<DefaultWebhookClient> logger) : IWebhookClient
    {
        private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        private readonly ILogger<DefaultWebhookClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task SendAsync(WebhookSubscription subscription, object payload, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            ArgumentNullException.ThrowIfNull(payload);

            string jsonPayload = JsonSerializer.Serialize(payload);

            int attempt = 0;
            int maxRetries = Math.Max(0, subscription.MaxRetries);

            while (true)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                // Создаём новый запрос на каждой попытке (нельзя повторно использовать HttpRequestMessage)
                using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                // Добавляем HMAC-подпись, если задан секрет
                if (!string.IsNullOrEmpty(subscription.Secret))
                {
                    string signature = ComputeHmacSignature(jsonPayload, subscription.Secret);
                    request.Headers.TryAddWithoutValidation("X-DnD-Signature", signature);
                }

                try
                {
                    // Таймаут на отдельную попытку
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(subscription.TimeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogDebug("Webhook на {Url} успешно отправлен (попытка {Attempt})", subscription.Url, attempt);
                        return;
                    }

                    _logger.LogWarning("Webhook на {Url} вернул статус {StatusCode} (попытка {Attempt})",
                        subscription.Url, (int)response.StatusCode, attempt);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Отправка webhook на {Url} отменена.", subscription.Url);
                    throw; // Пробрасываем отмену выше
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка отправки webhook на {Url} (попытка {Attempt})", subscription.Url, attempt);
                }

                if (attempt > maxRetries) // Первая попытка уже сделана, поэтому строгое сравнение
                {
                    _logger.LogError("Webhook на {Url} не удалось отправить после {MaxRetries} попыток", subscription.Url, maxRetries + 1);
                    return;
                }

                // Экспоненциальная задержка перед следующей попыткой
                int delayMs = (int)Math.Pow(2, attempt) * 1000;
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        private static string ComputeHmacSignature(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }
    }

    /// <summary>
    /// Обработчик доменных событий, рассылающий webhook-уведомления.
    /// </summary>
    public class WebhookHandler(
        IWebhookSubscriptionRepository subscriptionRepo,
        IWebhookClient webhookClient,
        ILogger<WebhookHandler> logger) : IEventHandler<IDomainEvent>
    {
        private readonly IWebhookSubscriptionRepository _subscriptionRepo = subscriptionRepo ?? throw new ArgumentNullException(nameof(subscriptionRepo));
        private readonly IWebhookClient _webhookClient = webhookClient ?? throw new ArgumentNullException(nameof(webhookClient));
        private readonly ILogger<WebhookHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task Handle(IDomainEvent @event, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(@event);
            cancellationToken.ThrowIfCancellationRequested();

            string eventType = @event.GetType().Name;

            // Получаем подписки для конкретного типа события и универсальные ("*")
            var specificSubscriptions = await _subscriptionRepo.GetSubscriptionsForEventAsync(eventType, cancellationToken).ConfigureAwait(false);
            var wildcardSubscriptions = await _subscriptionRepo.GetSubscriptionsForEventAsync("*", cancellationToken).ConfigureAwait(false);

            var allSubscriptions = (specificSubscriptions ?? [])
                .Concat(wildcardSubscriptions ?? [])
                .Where(s => s.IsActive)
                .DistinctBy(s => s.Id); // Избегаем дубликатов, если подписка попала в оба списка

            foreach (var sub in allSubscriptions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var payload = BuildPayload(@event);
                _logger.LogDebug("Отправка webhook для события {EventType} на {Url}", eventType, sub.Url);

                // Ожидаем завершения отправки, чтобы корректно обрабатывать ошибки и не терять уведомления.
                // При большом количестве подписок можно использовать параллельную отправку с ограничением.
                await _webhookClient.SendAsync(sub, payload, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Формирует объект payload, отправляемый в webhook.
        /// </summary>
        private static Dictionary<string, object?> BuildPayload(IDomainEvent @event)
        {
            var result = new Dictionary<string, object?>
            {
                ["eventType"] = @event.GetType().Name,
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["data"] = @event
            };

            if (@event is ICharacterEvent charEvent)
            {
                result["characterId"] = charEvent.CharacterId;
            }

            return result;
        }
    }
}