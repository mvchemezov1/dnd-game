#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.services;
using dnd_game.domain.events;
using Npgsql;

namespace dnd_game.infrastructure.persistence
{
    public class PostgresTradeRepository : PostgresRepositoryBase, ITradeRepository
    {
        public PostgresTradeRepository(string connectionString, ILogger<PostgresTradeRepository> logger)
            : base(connectionString, logger) { }

        public async Task<TradeItem?> GetItemInfoAsync(string itemId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT item_id, item_name, base_price_gold, is_magical, rarity FROM trade_items WHERE item_id = @id";
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", itemId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new TradeItem
                {
                    ItemId = reader.GetString(0),
                    ItemName = reader.GetString(1),
                    BasePriceGold = reader.GetInt32(2),
                    IsMagical = reader.GetBoolean(3),
                    Rarity = (Rarity)reader.GetInt32(4)
                };
            }
            return null;
        }

        public async Task<float> GetBuyMultiplierAsync(Guid npcId, Guid characterId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT buy_multiplier FROM trade_multipliers
                WHERE npc_id = @npcId AND character_id = @charId";

            var result = await ExecuteScalarAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("npcId", npcId);
                cmd.Parameters.AddWithValue("charId", characterId);
            }, cancellationToken).ConfigureAwait(false);

            return result is float f ? f : 1.0f;
        }

        public async Task<float> GetSellMultiplierAsync(Guid npcId, Guid characterId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT sell_multiplier FROM trade_multipliers
                WHERE npc_id = @npcId AND character_id = @charId";

            var result = await ExecuteScalarAsync(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("npcId", npcId);
                cmd.Parameters.AddWithValue("charId", characterId);
            }, cancellationToken).ConfigureAwait(false);

            return result is float f ? f : 0.5f;
        }
    }
}