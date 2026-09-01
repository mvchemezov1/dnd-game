#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.domain.events
{
    // --------------------------------------------------------------------------------------------
    // События, связанные с диалогами (диалоговыми сценами).
    // Все события реализуют IDialogueEvent, что позволяет обрабатывать их единообразно
    // и использовать идентификатор диалога (DialogueId) как идентификатор агрегата.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Интерфейс события диалога. Содержит идентификатор диалога.
    /// </summary>
    public interface IDialogueEvent : IAggregateEvent
    {
        /// <summary>
        /// Идентификатор диалога.
        /// </summary>
        Guid DialogueId { get; }
    }

    // ---------- Управление диалогом ----------

    /// <summary>Диалог начат между NPC и персонажем.</summary>
    public record DialogueStarted(
        Guid DialogueId,
        Guid NpcId,
        Guid CharacterId,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    /// <summary>Диалог завершён.</summary>
    public record DialogueEnded(
        Guid DialogueId,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    // ---------- Навигация по узлам ----------

    /// <summary>Достигнут новый узел диалога.</summary>
    public record DialogueNodeReached(
        Guid DialogueId,
        Guid NodeId,
        string NpcText,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    // ---------- Выбор варианта ----------

    /// <summary>Игрок выбрал вариант ответа.</summary>
    public record DialogueOptionSelected(
        Guid DialogueId,
        Guid OptionId,
        string PlayerText,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    // ---------- Проверки навыков в диалоге ----------

    /// <summary>Начата проверка навыка/характеристики в диалоге.</summary>
    public record DialogueSkillCheckAttempted(
        Guid DialogueId,
        string SkillOrAbility,
        int DifficultyClass,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    /// <summary>Проверка навыка завершена, известен итоговый результат.</summary>
    public record DialogueSkillCheckResolved(
        Guid DialogueId,
        string SkillOrAbility,
        int DifficultyClass,
        int RollResult,
        int TotalModifier,
        bool Success,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    // ---------- Эффекты диалога ----------

    /// <summary>Применён эффект диалога (изменение репутации, выдача предмета и т.п.).</summary>
    public record DialogueEffectApplied(
        Guid DialogueId,
        string EffectType,
        Dictionary<string, string> Parameters,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    // ---------- Результаты (успех/провал) ----------

    /// <summary>Вариант диалога завершился успехом.</summary>
    public record DialogueOptionSucceeded(
        Guid DialogueId,
        Guid OptionId,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }

    /// <summary>Вариант диалога завершился провалом (указана причина).</summary>
    public record DialogueOptionFailed(
        Guid DialogueId,
        Guid OptionId,
        string Reason,
        DateTime OccurredOn) : IDialogueEvent
    {
        public Guid AggregateId => DialogueId;
    }
}