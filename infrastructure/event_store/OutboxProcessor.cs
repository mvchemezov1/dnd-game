#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace dnd_game.infrastructure.event_store
{
    public class OutboxProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly string _connectionString;
        private readonly ILogger<OutboxProcessor> _logger;

        public OutboxProcessor(IServiceProvider serviceProvider, string connectionString, ILogger<OutboxProcessor> logger)
        {
            _serviceProvider = serviceProvider;
            _connectionString = connectionString;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingEventsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка обработки outbox-событий.");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        private async Task ProcessPendingEventsAsync(CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Выбираем непрочитанные события
            await using var cmd = new NpgsqlCommand(@"
                SELECT id, event_type, payload FROM outbox_events
                WHERE processed_at IS NULL
                ORDER BY id
                LIMIT 100
                FOR UPDATE SKIP LOCKED", conn, tx);

            var events = new List<(long Id, Type EventType, IDomainEvent DomainEvent)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt64(0);
                var typeName = reader.GetString(1);
                var payload = reader.GetString(2);
                var type = Type.GetType(typeName);
                if (type != null && JsonSerializer.Deserialize(payload, type) is IDomainEvent domainEvent)
                {
                    events.Add((id, type, domainEvent));
                }
            }
            reader.Close();

            if (events.Count == 0)
            {
                await tx.CommitAsync(ct);
                return;
            }

            var eventBus = _serviceProvider.GetRequiredService<IEventBus>();
            foreach (var (id, _, domainEvent) in events)
            {
                try
                {
                    await eventBus.PublishAsync(domainEvent, ct);
                    // Помечаем обработанным
                    await using var updateCmd = new NpgsqlCommand(
                        "UPDATE outbox_events SET processed_at = NOW() WHERE id = @id", conn, tx);
                    updateCmd.Parameters.AddWithValue("id", id);
                    await updateCmd.ExecuteNonQueryAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось опубликовать outbox-событие {EventId}.", id);
                    // Можно увеличить retry_count и продолжить
                }
            }

            await tx.CommitAsync(ct);
        }
    }
}