#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.rules
{
    /// <summary>
    /// Статический справочник магических предметов.
    /// В реальном проекте следует заменить на БД или конфигурацию.
    /// </summary>
    public static class MagicItems
    {
        private static readonly HashSet<string> MagicalItemIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "potion-of-healing",
            "wand-of-magic-missiles",
            "ring-of-protection",
            "cloak-of-elvenkind",
            "boots-of-speed",
            "amulet-of-health",
            "staff-of-power",
            "horn-of-valhalla"
            // Добавьте сюда другие магические предметы
        };

        public static bool IsMagical(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;
            return MagicalItemIds.Contains(itemId);
        }
    }
}