#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.services;
using dnd_game.domain.events; // Rarity, TradeItem
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace dnd_game.infrastructure.seeding
{
    /// <summary>
    /// Заполняет таблицы торговли начальными данными (предметы и множители для NPC).
    /// </summary>
    public class TradeSeeder
    {
        private readonly string _connectionString;
        private readonly ILogger<TradeSeeder> _logger;

        public TradeSeeder(string connectionString, ILogger<TradeSeeder>? logger = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? NullLogger<TradeSeeder>.Instance;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            // Предметы
            var items = new List<TradeItem>
            {
                new TradeItem { ItemId = "iron-sword", ItemName = "Железный меч", BasePriceGold = 50, IsMagical = false, Rarity = Rarity.Common },
                new TradeItem { ItemId = "leather-armor", ItemName = "Кожаный доспех", BasePriceGold = 20, IsMagical = false, Rarity = Rarity.Common },
                new TradeItem { ItemId = "potion-of-healing", ItemName = "Зелье лечения", BasePriceGold = 50, IsMagical = true, Rarity = Rarity.Uncommon },
                new TradeItem { ItemId = "shield", ItemName = "Щит", BasePriceGold = 10, IsMagical = false, Rarity = Rarity.Common },
                new TradeItem { ItemId = "longbow", ItemName = "Длинный лук", BasePriceGold = 50, IsMagical = false, Rarity = Rarity.Common }
            };

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            foreach (var item in items)
            {
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO trade_items (item_id, item_name, base_price_gold, is_magical, rarity)
                    VALUES (@itemId, @itemName, @basePrice, @isMagical, @rarity)
                    ON CONFLICT (item_id) DO UPDATE SET
                        item_name = EXCLUDED.item_name,
                        base_price_gold = EXCLUDED.base_price_gold,
                        is_magical = EXCLUDED.is_magical,
                        rarity = EXCLUDED.rarity", conn);
                cmd.Parameters.AddWithValue("itemId", item.ItemId);
                cmd.Parameters.AddWithValue("itemName", item.ItemName);
                cmd.Parameters.AddWithValue("basePrice", item.BasePriceGold);
                cmd.Parameters.AddWithValue("isMagical", item.IsMagical);
                cmd.Parameters.AddWithValue("rarity", (int)item.Rarity);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Примеры множителей для конкретных NPC (пусть NPC ID = Guid.Empty означает "общий торговец")
            var defaultMultipliers = new List<(Guid NpcId, Guid CharacterId, float Buy, float Sell)>
            {
                (Guid.Empty, Guid.Empty, 1.0f, 0.5f), // Общий случай
                (Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"), Guid.Empty, 1.2f, 0.4f) // Пример конкретного NPC с наценкой
            };

            foreach (var m in defaultMultipliers)
            {
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO trade_multipliers (npc_id, character_id, buy_multiplier, sell_multiplier)
                    VALUES (@npcId, @charId, @buy, @sell)
                    ON CONFLICT (npc_id, character_id) DO UPDATE SET
                        buy_multiplier = EXCLUDED.buy_multiplier,
                        sell_multiplier = EXCLUDED.sell_multiplier", conn);
                cmd.Parameters.AddWithValue("npcId", m.NpcId);
                cmd.Parameters.AddWithValue("charId", m.CharacterId);
                cmd.Parameters.AddWithValue("buy", m.Buy);
                cmd.Parameters.AddWithValue("sell", m.Sell);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("Торговые данные (предметы и множители) успешно добавлены.");
        }
    }
}