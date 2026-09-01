#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.services;
using dnd_game.domain.events; // для TradeItem
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresTradeOfferRepository : PostgresRepositoryBase, ITradeOfferRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public PostgresTradeOfferRepository(string connectionString, ILogger<PostgresTradeOfferRepository> logger)
            : base(connectionString, logger) { }

        public async Task AddAsync(TradeOffer offer, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO trade_offers
                    (offer_id, from_character_id, to_character_id, offered_items, offered_gold,
                     requested_items, requested_gold, status, created_at)
                VALUES
                    (@id, @from, @to, @offeredItems::jsonb, @offeredGold,
                     @requestedItems::jsonb, @requestedGold, @status, @createdAt)";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("id", offer.OfferId);
                cmd.Parameters.AddWithValue("from", offer.FromCharacterId);
                cmd.Parameters.AddWithValue("to", offer.ToCharacterId);
                cmd.Parameters.AddWithValue("offeredItems",
                    JsonSerializer.Serialize(offer.OfferedItems, JsonOptions));
                cmd.Parameters.AddWithValue("offeredGold", offer.OfferedGold);
                cmd.Parameters.AddWithValue("requestedItems",
                    JsonSerializer.Serialize(offer.RequestedItems, JsonOptions));
                cmd.Parameters.AddWithValue("requestedGold", offer.RequestedGold);
                cmd.Parameters.AddWithValue("status", offer.Status.ToString());
                cmd.Parameters.AddWithValue("createdAt", offer.CreatedAt);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TradeOffer?> GetByIdAsync(Guid offerId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM trade_offers WHERE offer_id = @id";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", offerId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return MapOffer(reader);
            }
            return null;
        }

        public async Task UpdateAsync(TradeOffer offer, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE trade_offers
                SET status = @status
                WHERE offer_id = @id";

            await ExecuteNonQueryAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("id", offer.OfferId);
                cmd.Parameters.AddWithValue("status", offer.Status.ToString());
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<TradeOffer>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM trade_offers ORDER BY created_at DESC";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var offers = new List<TradeOffer>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                offers.Add(MapOffer(reader));
            }
            return offers;
        }

        public async Task RemoveAsync(Guid offerId, CancellationToken cancellationToken = default)
        {
            const string sql = "DELETE FROM trade_offers WHERE offer_id = @id";
            await ExecuteNonQueryAsync(sql, cmd => cmd.Parameters.AddWithValue("id", offerId),
                cancellationToken).ConfigureAwait(false);
        }

        private static TradeOffer MapOffer(NpgsqlDataReader reader)
        {
            var offeredJson = reader.GetString(3);
            var requestedJson = reader.GetString(5);

            return new TradeOffer
            {
                OfferId = reader.GetGuid(0),
                FromCharacterId = reader.GetGuid(1),
                ToCharacterId = reader.GetGuid(2),
                OfferedItems = JsonSerializer.Deserialize<List<TradeItem>>(offeredJson, JsonOptions) ?? new(),
                OfferedGold = reader.GetInt32(4),
                RequestedItems = JsonSerializer.Deserialize<List<TradeItem>>(requestedJson, JsonOptions) ?? new(),
                RequestedGold = reader.GetInt32(6),
                Status = Enum.Parse<TradeOfferStatus>(reader.GetString(7)),
                CreatedAt = reader.GetDateTime(8)
            };
        }
    }
}