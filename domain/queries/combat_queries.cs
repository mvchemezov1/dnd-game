#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.application.projections; // DTO боя: CombatStatusDto, CombatParticipantDto

namespace dnd_game.domain.queries
{
    // --------------------------------------------------------------------------------------------
    // Запросы, связанные с боевыми сценами: статус, участники, текущий ход, раунд, порядок ходов.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить полный статус боя по идентификатору.
    /// </summary>
    public record GetCombatStatus(Guid CombatId) : IQuery<CombatStatusDto?>;

    /// <summary>
    /// Получить список всех участников боя с их детальными параметрами.
    /// </summary>
    public record GetCombatParticipants(Guid CombatId) : IQuery<List<CombatParticipantDto>>;

    /// <summary>
    /// Получить текущего активного участника (чья очередь ходить).
    /// </summary>
    public record GetCurrentCombatParticipant(Guid CombatId) : IQuery<CombatParticipantDto?>;

    /// <summary>
    /// Получить номер текущего раунда боя.
    /// </summary>
    public record GetCombatRound(Guid CombatId) : IQuery<int>;

    /// <summary>
    /// Получить порядок ходов: список идентификаторов персонажей в порядке инициативы.
    /// </summary>
    public record GetCombatTurnOrder(Guid CombatId) : IQuery<List<Guid>>;

    /// <summary>
    /// Проверить, активен ли бой в данный момент.
    /// </summary>
    public record IsCombatActive(Guid CombatId) : IQuery<bool>;
}