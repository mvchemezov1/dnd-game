using System;
using System.Collections.Generic;

namespace dnd_game.application.projections
{
    /// <summary>
    /// DTO состояния боя для отображения в пользовательском интерфейсе.
    /// Содержит информацию о текущем раунде, участниках и порядке хода.
    /// </summary>
    public record CombatStatusDto
    {
        /// <summary>Идентификатор боя.</summary>
        public Guid CombatId { get; init; }

        /// <summary>Активен ли бой в данный момент.</summary>
        public bool IsActive { get; init; }

        /// <summary>Список участников боя.</summary>
        public List<CombatParticipantDto> Participants { get; init; } = [];

        /// <summary>Текущий раунд (начиная с 1).</summary>
        public int Round { get; init; } = 1;

        /// <summary>Индекс участника, чей сейчас ход, в списке Participants.</summary>
        public int CurrentTurnIndex { get; init; }

        /// <summary>Идентификаторы персонажей, управляемых игроками (PC).</summary>
        public List<Guid> PlayerCharacterIds { get; init; } = [];
    }

    /// <summary>
    /// DTO участника боя для отображения его текущего состояния.
    /// </summary>
    public record CombatParticipantDto
    {
        public Guid CharacterId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Initiative { get; init; }
        public bool IsCurrentTurn { get; init; }
        public bool HasAction { get; init; } = true;
        public bool HasBonusAction { get; init; } = true;
        public bool HasReaction { get; init; } = true;
        public bool HasMovement { get; init; } = true;
        public int MovementRemaining { get; init; }
        public List<string> Conditions { get; init; } = [];
        public bool Concentrating { get; init; }
        public string? ReadyActionType { get; init; }
        public string? ReadyTriggerCondition { get; init; }
        public bool HasReadiedAction { get; init; }

        // Новые поля
        public int CurrentHitPoints { get; init; }
        public int MaxHitPoints { get; init; }
        public int TemporaryHitPoints { get; init; }
        public int ArmorClass { get; init; }
    }
}