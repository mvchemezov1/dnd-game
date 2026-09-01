#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using dnd_game.application.projections;
using dnd_game.domain.events;
using dnd_game.domain.interfaces;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.message_bus;

namespace dnd_game.infrastructure.coordination
{
    /// <summary>
    /// Регистрирует фабрики саг в <see cref="ISagaRegistry"/> и подписывает <see cref="ISagaDispatcher"/>
    /// на соответствующие события через <see cref="IEventBus"/>.
    /// Вызывается один раз при старте приложения после настройки DI-контейнера.
    /// </summary>
    public static class SagaRegistrations
    {
        public static void RegisterAll(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            var registry = services.GetRequiredService<ISagaRegistry>();
            var dispatcher = services.GetRequiredService<ISagaDispatcher>();
            var eventBus = services.GetRequiredService<IEventBus>();
            var commandBus = services.GetRequiredService<ICommandBus>();
            var characterProjection = services.GetRequiredService<CharacterProjection>();
            var campaignProjection = services.GetRequiredService<CampaignProjection>();
            var questTrackingStore = services.GetRequiredService<IQuestTrackingStore>();

            // Локальная функция для единообразной регистрации фабрики и подписки на событие
            void RegisterSaga<TEvent>(Func<TEvent, ISaga> factory) where TEvent : IDomainEvent
            {
                ArgumentNullException.ThrowIfNull(factory);

                registry.Register(factory);
                eventBus.Subscribe<TEvent>((e, ct) => dispatcher.DispatchAsync(e, ct));
            }

            // ==================== Торговые сделки ====================
            // Один экземпляр TradeSaga на одно предложение (SagaId = OfferId)
            RegisterSaga<TradeOfferCreated>(e => new TradeSaga(e.OfferId, commandBus, eventBus, characterProjection));
            RegisterSaga<TradeOfferAccepted>(e => new TradeSaga(e.OfferId, commandBus, eventBus, characterProjection));
            RegisterSaga<TradeOfferDeclined>(e => new TradeSaga(e.OfferId, commandBus, eventBus, characterProjection));
            RegisterSaga<TradeOfferCancelled>(e => new TradeSaga(e.OfferId, commandBus, eventBus, characterProjection));

            // ==================== Квесты ====================
            // Один экземпляр QuestSaga на один квест (SagaId = QuestId)
            RegisterSaga<QuestAccepted>(e => new QuestSaga(e.QuestId, commandBus, campaignProjection, characterProjection, questTrackingStore));
            RegisterSaga<QuestObjectiveUpdated>(e => new QuestSaga(e.QuestId, commandBus, campaignProjection, characterProjection, questTrackingStore));
            RegisterSaga<QuestCompleted>(e => new QuestSaga(e.QuestId, commandBus, campaignProjection, characterProjection, questTrackingStore));
            RegisterSaga<QuestFailed>(e => new QuestSaga(e.QuestId, commandBus, campaignProjection, characterProjection, questTrackingStore));

            // Обработка смерти персонажа для квестов.
            // В отличие от других событий, CharacterDied не содержит QuestId, поэтому используется
            // одноразовый экземпляр QuestSaga с SagaId = CharacterId. Сам обработчик внутри саги
            // находит затронутые квесты через IQuestTrackingStore.
            RegisterSaga<CharacterDied>(e => new QuestSaga(e.CharacterId, commandBus, campaignProjection, characterProjection, questTrackingStore));

            // ==================== Повышение уровня ====================
            // Один экземпляр LevelUpSaga на персонажа (SagaId = CharacterId)
            RegisterSaga<ExperienceGained>(e => new LevelUpSaga(e.CharacterId, commandBus, characterProjection));

            // ==================== Бой ====================
            // Один экземпляр CombatSaga на один бой (SagaId = CombatId)
            RegisterSaga<CombatStarted>(e => new CombatSaga(e.CombatId, commandBus));
            RegisterSaga<InitiativeRolled>(e => new CombatSaga(e.CombatId, commandBus));
            RegisterSaga<CombatRoundStarted>(e => new CombatSaga(e.CombatId, commandBus));
            RegisterSaga<CombatTurnEnded>(e => new CombatSaga(e.CombatId, commandBus));
            RegisterSaga<ParticipantRemovedFromCombat>(e => new CombatSaga(e.CombatId, commandBus));
            // ==================== Повышение уровня ====================
            // Один экземпляр LevelUpSaga на персонажа (SagaId = CharacterId)
            RegisterSaga<ExperienceGained>(e => new LevelUpSaga(e.CharacterId, commandBus, characterProjection));
        }
    }
}