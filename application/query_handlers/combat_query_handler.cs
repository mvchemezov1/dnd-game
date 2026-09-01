using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.domain.queries;

namespace dnd_game.application.query_handlers
{
    /// <summary>
    /// Обработчик запросов, связанных с боевыми сценами.
    /// Предоставляет данные о состоянии боя, участниках и порядке ходов.
    /// </summary>
    public class CombatQueryHandler(CombatProjection projection) : IQueryHandler<GetCombatStatus, CombatStatusDto?>,
                                      IQueryHandler<GetCombatParticipants, List<CombatParticipantDto>>,
                                      IQueryHandler<GetCurrentCombatParticipant, CombatParticipantDto?>,
                                      IQueryHandler<GetCombatRound, int>,
                                      IQueryHandler<GetCombatTurnOrder, List<Guid>>,
                                      IQueryHandler<IsCombatActive, bool>
    {
        private readonly CombatProjection _projection = projection ?? throw new ArgumentNullException(nameof(projection));

        /// <summary>
        /// Получает полный статус боя по идентификатору.
        /// </summary>
        public Task<CombatStatusDto?> Handle(GetCombatStatus query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetStatus(query.CombatId, cancellationToken);
        }

        /// <summary>
        /// Получает список всех участников боя с их параметрами.
        /// </summary>
        public Task<List<CombatParticipantDto>> Handle(GetCombatParticipants query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetParticipants(query.CombatId, cancellationToken);
        }

        /// <summary>
        /// Получает текущего активного участника (чья очередь ходить).
        /// </summary>
        public Task<CombatParticipantDto?> Handle(GetCurrentCombatParticipant query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetCurrentParticipant(query.CombatId, cancellationToken);
        }

        /// <summary>
        /// Получает номер текущего раунда боя. Если бой не найден, возвращает 0.
        /// </summary>
        public async Task<int> Handle(GetCombatRound query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            var status = await _projection.GetStatus(query.CombatId, cancellationToken);
            return status?.Round ?? 0;
        }

        /// <summary>
        /// Получает порядок ходов: список идентификаторов персонажей в порядке инициативы.
        /// </summary>
        public async Task<List<Guid>> Handle(GetCombatTurnOrder query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            var participants = await _projection.GetParticipants(query.CombatId, cancellationToken);
            return [.. participants.Select(p => p.CharacterId)];
        }

        /// <summary>
        /// Проверяет, активен ли бой в данный момент.
        /// </summary>
        public async Task<bool> Handle(IsCombatActive query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            var status = await _projection.GetStatus(query.CombatId, cancellationToken);
            return status?.IsActive ?? false;
        }
    }
}