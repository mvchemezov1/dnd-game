#nullable enable
using System;

namespace dnd_game.domain.commands
{
    /// <summary>
    /// Команда ввода текста в диалоге (например, для свободного ответа игрока).
    /// </summary>
    public record DialogueTextInput(
        Guid DialogueId,
        string Text
    ) : ICommand;
}