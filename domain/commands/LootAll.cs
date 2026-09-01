#nullable enable
using System;

namespace dnd_game.domain.commands
{
    /// <summary>
    /// Команда "забрать всё" для персонажа: подобрать все доступные предметы.
    /// </summary>
    public record LootAll(Guid CharacterId) : ICommand;
}