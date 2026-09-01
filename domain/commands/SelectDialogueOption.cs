#nullable enable
using System;

namespace dnd_game.domain.commands
{
    /// <summary>
    /// Команда выбора варианта ответа в диалоге по его индексу.
    /// Используется, когда варианты представлены списком, и игрок выбирает один из них.
    /// </summary>
    /// <param name="DialogueId">Идентификатор активного диалога.</param>
    /// <param name="OptionIndex">Индекс выбранного варианта (начиная с 0).</param>
    public record SelectDialogueOption(
        Guid DialogueId,
        int OptionIndex
    ) : ICommand;
}