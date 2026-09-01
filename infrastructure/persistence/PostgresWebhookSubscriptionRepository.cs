#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.event_handlers;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresWebhookSubscriptionRepository : PostgresRepositoryBase, IWebhookSubscriptionRepository
    {
        public PostgresWebhookSubscriptionRepository(string connectionString, ILogger<PostgresWebhookSubscriptionRepository> logger)
            : base(connectionString, logger) { }

        public async Task<IEnumerable<WebhookSubscription>> GetSubscriptionsForEventAsync(
            string eventType, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT id, event_type, url, secret, max_retries, timeout_seconds, is_active
                FROM webhook_subscriptions
                WHERE is_active = TRUE AND (event_type = @event OR event_type = '*')";

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("event", eventType);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var subscriptions = new List<WebhookSubscription>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                subscriptions.Add(MapSubscription(reader));
            }
            return subscriptions;
        }

        public async Task<IEnumerable<WebhookSubscription>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT id, event_type, url, secret, max_retries, timeout_seconds, is_active FROM webhook_subscriptions";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var subscriptions = new List<WebhookSubscription>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                subscriptions.Add(MapSubscription(reader));
            }
            return subscriptions;
        }

        private static WebhookSubscription MapSubscription(NpgsqlDataReader reader)
        {
            return new WebhookSubscription
            {
                Id = reader.GetGuid(0),
                EventType = reader.GetString(1),
                Url = reader.GetString(2),
                Secret = reader.IsDBNull(3) ? null : reader.GetString(3),
                MaxRetries = reader.GetInt32(4),
                TimeoutSeconds = reader.GetInt32(5),
                IsActive = reader.GetBoolean(6)
            };
        }
    }
}