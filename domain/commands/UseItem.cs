#nullable enable
using System;

namespace dnd_game.domain.commands
{
    /// <summary>
    /// Команда использования предмета персонажем.
    /// </summary>
    /// <param name="CharacterId">Идентификатор персонажа, использующего предмет.</param>
    /// <param name="ItemId">Идентификатор используемого предмета.</param>
    public record UseItem(
        Guid CharacterId,
        string ItemId
    ) : ICommand;
}