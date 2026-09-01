#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using dnd_game.application.projections;
using dnd_game.domain.events;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.coordination
{
    /// <summary>
    /// Регистрирует проекции на события шины.
    /// Использует единую подписку на IDomainEvent для каждой проекции,
    /// что снижает количество подписок и упрощает поддержку.
    /// </summary>
    public static class ProjectionRegistrations
    {
        public static void RegisterAll(IServiceProvider services)
        {
            var eventBus = services.GetRequiredService<IEventBus>();
            var characterProjection = services.GetRequiredService<CharacterProjection>();
            var campaignProjection = services.GetRequiredService<CampaignProjection>();
            var combatProjection = services.GetRequiredService<CombatProjection>();
            var journeyProjection = services.GetRequiredService<JourneyProjection>();

            // Подписка на все доменные события для CharacterProjection
            eventBus.Subscribe<IDomainEvent>((e, _) =>
            {
                characterProjection.Apply(e);
                return Task.CompletedTask;
            });

            // Для CampaignProjection
            eventBus.Subscribe<IDomainEvent>((e, _) =>
            {
                campaignProjection.Apply(e);
                return Task.CompletedTask;
            });

            // Для CombatProjection
            eventBus.Subscribe<IDomainEvent>((e, _) =>
            {
                combatProjection.Apply(e);
                return Task.CompletedTask;
            });

            // Для JourneyProjection
            eventBus.Subscribe<IDomainEvent>((e, _) =>
            {
                journeyProjection.Apply(e);
                return Task.CompletedTask;
            });
        }
    }
}