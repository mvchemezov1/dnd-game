#nullable enable
using System;
using dnd_game.application.projections;

namespace dnd_game.domain.queries
{
    public record GetJourneyStatus(Guid PartyId) : IQuery<JourneyStateDto?>;
}