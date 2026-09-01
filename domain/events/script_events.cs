#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События, связанные со скриптами и триггерами.
    // Фиксируют жизненный цикл скриптов: запуск, выполнение, проверку условий,
    // управление состоянием (пауза, сброс) и отдельные действия.
    // --------------------------------------------------------------------------------------------

    /// <summary>Сработал триггер скрипта с указанными параметрами.</summary>
    public record ScriptTriggered(
        Guid ScriptId,
        string TriggerName,
        Dictionary<string, string> Parameters,
        DateTime Timestamp) : IDomainEvent;

    // ---------- Управление состоянием триггеров ----------

    /// <summary>Триггер скрипта включён.</summary>
    public record ScriptTriggerEnabled(
        Guid ScriptId,
        string TriggerName,
        DateTime Timestamp) : IDomainEvent;

    /// <summary>Триггер скрипта выключен.</summary>
    public record ScriptTriggerDisabled(
        Guid ScriptId,
        string TriggerName,
        DateTime Timestamp) : IDomainEvent;

    // ---------- Выполнение скрипта ----------

    /// <summary>Началось выполнение скрипта.</summary>
    public record ScriptExecutionStarted(
        Guid ScriptId,
        string TriggerName,
        DateTime Timestamp) : IDomainEvent;

    /// <summary>Выполнение скрипта успешно завершено.</summary>
    public record ScriptExecutionCompleted(
        Guid ScriptId,
        string TriggerName,
        DateTime Timestamp) : IDomainEvent;

    /// <summary>Выполнение скрипта завершилось ошибкой.</summary>
    public record ScriptExecutionFailed(
        Guid ScriptId,
        string TriggerName,
        string ErrorMessage,
        DateTime Timestamp) : IDomainEvent;

    // ---------- Проверка условий ----------

    /// <summary>Оценено условие скрипта с указанием результата.</summary>
    public record ScriptConditionEvaluated(
        Guid ScriptId,
        string ConditionType,
        Dictionary<string, string> Parameters,
        bool Result,
        DateTime Timestamp) : IDomainEvent;

    // ---------- Выполнение отдельных действий ----------

    /// <summary>Выполнено отдельное действие скрипта.</summary>
    public record ScriptActionExecuted(
        Guid ScriptId,
        string ActionType,
        Dictionary<string, string> Parameters,
        DateTime Timestamp) : IDomainEvent;

    // ---------- Управление паузами и перезапусками ----------

    /// <summary>Скрипт поставлен на паузу.</summary>
    public record ScriptPaused(
        Guid ScriptId,
        DateTime Timestamp) : IDomainEvent;

    /// <summary>Скрипт возобновлён после паузы.</summary>
    public record ScriptResumed(
        Guid ScriptId,
        DateTime Timestamp) : IDomainEvent;

    /// <summary>Состояние скрипта сброшено.</summary>
    public record ScriptReset(
        Guid ScriptId,
        string TriggerName,
        DateTime Timestamp) : IDomainEvent;
}