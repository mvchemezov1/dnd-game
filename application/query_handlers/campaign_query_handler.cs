using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.domain.queries;

namespace dnd_game.application.query_handlers
{
    /// <summary>
    /// Обработчик запросов, связанных с кампаниями.
    /// Предоставляет доступ к данным проекции кампании: квестам, состоянию, фракциям и событиям.
    /// </summary>
    public class CampaignQueryHandler(CampaignProjection projection) : IQueryHandler<GetActiveQuests, List<Guid>>,
                                        IQueryHandler<GetQuestDetails, QuestInfo?>,
                                        IQueryHandler<GetQuestsByStatus, List<QuestInfo>>,
                                        IQueryHandler<GetCampaignState, CampaignState?>,
                                        IQueryHandler<GetFactionReputation, FactionState?>,
                                        IQueryHandler<GetAllFactions, List<FactionState>>,
                                        IQueryHandler<GetActiveWorldEvents, List<string>>
    {
        private readonly CampaignProjection _projection = projection ?? throw new ArgumentNullException(nameof(projection));

        /// <summary>
        /// Получает список идентификаторов активных квестов указанной кампании.
        /// </summary>
        public Task<List<Guid>> Handle(GetActiveQuests query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetActiveQuestIds(query.CampaignId, cancellationToken);
        }

        /// <summary>
        /// Получает детальную информацию о конкретном квесте.
        /// </summary>
        public Task<QuestInfo?> Handle(GetQuestDetails query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetQuestDetails(query.CampaignId, query.QuestId, cancellationToken);
        }

        /// <summary>
        /// Получает список квестов кампании, отфильтрованных по статусу.
        /// </summary>
        public Task<List<QuestInfo>> Handle(GetQuestsByStatus query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetQuests(query.CampaignId, query.StatusFilter, cancellationToken);
        }

        /// <summary>
        /// Получает текущее состояние кампании: игровое время, погоду, флаги и т.д.
        /// </summary>
        public Task<CampaignState?> Handle(GetCampaignState query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetCampaignState(query.CampaignId, cancellationToken);
        }

        /// <summary>
        /// Получает информацию о репутации указанной фракции.
        /// </summary>
        public Task<FactionState?> Handle(GetFactionReputation query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetFaction(query.FactionId, cancellationToken);
        }

        /// <summary>
        /// Получает список всех известных фракций с их текущей репутацией.
        /// </summary>
        public Task<List<FactionState>> Handle(GetAllFactions query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetAllFactions(cancellationToken);
        }

        /// <summary>
        /// Получает список активных мировых событий указанной кампании.
        /// </summary>
        public Task<List<string>> Handle(GetActiveWorldEvents query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return _projection.GetActiveWorldEvents(query.CampaignId, cancellationToken);
        }
    }
}