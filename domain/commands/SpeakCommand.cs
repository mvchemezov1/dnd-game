#nullable enable
using System;

namespace dnd_game.domain.commands
{
    /// <summary>
    /// Команда произнесения реплики персонажем (например, для ролевого отыгрыша).
    /// </summary>
    /// <param name="CharacterId">Идентификатор персонажа, который говорит.</param>
    /// <param name="Message">Текст сообщения.</param>
    public record SpeakCommand(
        Guid CharacterId,
        string Message
    ) : ICommand;
}