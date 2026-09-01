#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.commands
{
    /// <summary>
    /// Команда запуска скрипта по имени с передачей параметров.
    /// Используется для триггеров, событий и других сценарных механик.
    /// </summary>
    /// <param name="ScriptName">Имя скрипта для выполнения.</param>
    /// <param name="Parameters">Параметры, передаваемые в скрипт.</param>
    public record TriggerScriptCommand(
        string ScriptName,
        Dictionary<string, object> Parameters
    ) : ICommand;
}