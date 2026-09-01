#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.domain.queries;

namespace dnd_game.application.query_handlers
{
    public class JourneyQueryHandler : IQueryHandler<GetJourneyStatus, JourneyStateDto?>
    {
        private readonly JourneyProjection _projection;

        public JourneyQueryHandler(JourneyProjection projection)
        {
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        }

        public Task<JourneyStateDto?> Handle(GetJourneyStatus query, CancellationToken cancellationToken)
        {
            return _projection.GetByPartyIdAsync(query.PartyId, cancellationToken);
        }
    }
}