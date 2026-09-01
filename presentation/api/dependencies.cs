using dnd_game.application.command_handlers;
using dnd_game.application.event_handlers;
using dnd_game.application.notifications;
using dnd_game.application.projections;
using dnd_game.application.query_handlers;
using dnd_game.application.security;
using dnd_game.application.services;
using dnd_game.domain.commands;
using dnd_game.domain.interfaces;
using dnd_game.domain.queries;
using dnd_game.domain.sagas;
using dnd_game.infrastructure.ai;
using dnd_game.infrastructure.caching;
using dnd_game.infrastructure.common;
using dnd_game.infrastructure.config;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.localization;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.monitoring;
using dnd_game.infrastructure.network;
using dnd_game.infrastructure.persistence;
using dnd_game.infrastructure.security;
using dnd_game.infrastructure.seeding;
using dnd_game.infrastructure.undo;
using dnd_game.infrastructure.world;
using dnd_game.migrations;
using dnd_game.presentation.api.validators;
using dnd_game.presentation.dm_tools;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.IO;
using System.Linq;

namespace dnd_game.presentation.api
{
    public static class Dependencies
    {
        public static IServiceCollection AddGameServices(this IServiceCollection services, IConfiguration configuration)
        {
            // 1. Конфигурация
            services.Configure<Settings>(configuration.GetSection("Game"));
            services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
            services.Configure<TokenSettings>(configuration.GetSection("Token"));
            services.Configure<RateLimitConfiguration>(configuration.GetSection("RateLimiting"));
            services.Configure<GameServerConfiguration>(configuration.GetSection("GameServer"));

            var tokenSecret = configuration["Token:Secret"];
            if (string.IsNullOrWhiteSpace(tokenSecret) ||
                tokenSecret is "change-me" or "your-secret-key" or "your-secret-key-change-in-production")
            {
                throw new InvalidOperationException(
                    "Token:Secret не настроен или содержит значение по умолчанию. " +
                    "Задайте его через переменную окружения Token__Secret или локально через " +
                    "'dotnet user-secrets set \"Token:Secret\" \"<длинное случайное значение>\"'.");
            }

            // Уведомления
            services.AddSingleton<INotificationService, InMemoryNotificationService>();

            // Регистрируем IConnectionMultiplexer для Redis
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var redisConnectionString = configuration.GetConnectionString("Redis");
                if (string.IsNullOrEmpty(redisConnectionString))
                    throw new InvalidOperationException("Строка подключения Redis (ConnectionStrings:Redis) не задана.");

                var options = ConfigurationOptions.Parse(redisConnectionString);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = 2000;
                options.SyncTimeout = 2000;

                var connection = ConnectionMultiplexer.Connect(options);
                if (!connection.IsConnected)
                    throw new InvalidOperationException("Не удалось подключиться к Redis.");
                return connection;
            });

            // 2. HTTP-контекст
            services.AddHttpContextAccessor();

            // 3. Кэширование
            services.AddSingleton<ICacheProvider>(sp =>
            {
                var redisConnectionString = configuration.GetConnectionString("Redis");
                if (string.IsNullOrEmpty(redisConnectionString))
                {
                    sp.GetRequiredService<ILogger<ICacheProvider>>()
                      .LogWarning("Строка подключения Redis не задана, используется NoOpCacheProvider.");
                    return new NoOpCacheProvider();
                }

                try
                {
                    var options = ConfigurationOptions.Parse(redisConnectionString);
                    options.AbortOnConnectFail = false;
                    options.ConnectTimeout = 2000;
                    options.SyncTimeout = 2000;

                    var redis = ConnectionMultiplexer.Connect(options);
                    if (!redis.IsConnected) throw new InvalidOperationException("Не удалось подключиться к Redis.");

                    var db = redis.GetDatabase();
                    if (db.Ping().TotalMilliseconds > 1000)
                        throw new InvalidOperationException("Redis не отвечает в пределах разумного времени.");

                    return new RedisCacheProvider(redis);
                }
                catch (Exception ex)
                {
                    sp.GetRequiredService<ILogger<ICacheProvider>>()
                      .LogWarning(ex, "Redis недоступен, используется NoOpCacheProvider.");
                    return new NoOpCacheProvider();
                }
            });

            // 4. Строка подключения
            var connString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connString))
                throw new InvalidOperationException("Строка подключения 'DefaultConnection' не задана.");

            // 5. Персистентные репозитории (PostgreSQL)
            services.AddSingleton<IUserRepository>(sp => new PostgresUserRepository(connString, sp.GetRequiredService<ILogger<PostgresUserRepository>>()));
            services.AddSingleton<IRefreshTokenStore>(sp => new PostgresRefreshTokenStore(connString, sp.GetRequiredService<ILogger<PostgresRefreshTokenStore>>()));
            services.AddSingleton<ICharacterOwnershipRepository>(sp => new PostgresCharacterOwnershipRepository(connString, sp.GetRequiredService<ILogger<PostgresCharacterOwnershipRepository>>()));
            services.AddSingleton<IRecipeRepository>(sp => new PostgresRecipeRepository(connString, sp.GetRequiredService<ILogger<PostgresRecipeRepository>>()));
            services.AddSingleton<RecipeSeeder>();
            services.AddSingleton<ICraftingProcessRepository>(sp => new PostgresCraftingProcessRepository(connString, sp.GetRequiredService<ILogger<PostgresCraftingProcessRepository>>()));
            services.AddSingleton<ITriggerDefinitionRepository>(sp => new PostgresTriggerDefinitionRepository(connString, sp.GetRequiredService<ILogger<PostgresTriggerDefinitionRepository>>()));
            services.AddSingleton<ITriggerStateStore>(sp => new PostgresTriggerStateStore(connString, sp.GetRequiredService<ILogger<PostgresTriggerStateStore>>()));
            services.AddSingleton<IWebhookSubscriptionRepository>(sp => new PostgresWebhookSubscriptionRepository(connString, sp.GetRequiredService<ILogger<PostgresWebhookSubscriptionRepository>>()));
            services.AddSingleton<ISagaStateRepository>(sp => new PostgresSagaStateRepository(connString, sp.GetRequiredService<ILogger<PostgresSagaStateRepository>>()));
            services.AddSingleton<ITradeRepository>(sp => new PostgresTradeRepository(connString, sp.GetRequiredService<ILogger<PostgresTradeRepository>>()));
            services.AddSingleton<ITradeOfferRepository>(sp => new PostgresTradeOfferRepository(connString, sp.GetRequiredService<ILogger<PostgresTradeOfferRepository>>()));
            services.AddSingleton<TradeSeeder>(sp =>
            new TradeSeeder(
                connString,
                sp.GetRequiredService<ILogger<TradeSeeder>>()
            ));
            services.AddSingleton<IQuestTrackingStore>(sp => new PostgresQuestTrackingStore(connString, sp.GetRequiredService<ILogger<PostgresQuestTrackingStore>>()));
            services.AddSingleton<IDialogueRepository>(sp => new PostgresDialogueRepository(connString, sp.GetRequiredService<ILogger<PostgresDialogueRepository>>()));
            services.AddSingleton<IDialogueStateRepository>(sp => new PostgresDialogueStateRepository(connString, sp.GetRequiredService<ILogger<PostgresDialogueStateRepository>>()));
            services.AddSingleton<IScriptRepository>(sp => new PostgresScriptRepository(connString, sp.GetRequiredService<ILogger<PostgresScriptRepository>>()));

            // 6. In-memory (для разработки/специфичных случаев)
            services.AddSingleton<IReplayEventStore, InMemoryReplayEventStore>();
            services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();

            // 7. Снимки
            services.AddSingleton<ISnapshotStore>(sp =>
            {
                var snapshotConfig = new SnapshotConfiguration
                {
                    Policy = SnapshotPolicy.EventCount,
                    EventCountInterval = configuration.GetValue<int?>("EventStore:SnapshotInterval") ?? 100,
                    TimeInterval = TimeSpan.FromMinutes(configuration.GetValue<int?>("EventStore:SnapshotIntervalMinutes") ?? 30)
                };
                return new SnapshotStore(connString, snapshotConfig);
            });

            // 8. Блокировки
            services.AddSingleton<IDistributedLockManager>(sp =>
            {
                var redisConnectionString = configuration.GetConnectionString("Redis");
                if (!string.IsNullOrEmpty(redisConnectionString))
                {
                    try
                    {
                        var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                        if (redis.IsConnected)
                        {
                            return new RedisDistributedLockManager(
                                redis,
                                sp.GetRequiredService<PermissionChecker>(),
                                sp.GetRequiredService<ILogger<RedisDistributedLockManager>>());
                        }
                    }
                    catch (Exception ex)
                    {
                        sp.GetRequiredService<ILogger<IDistributedLockManager>>()
                          .LogWarning(ex, "Не удалось использовать Redis для блокировок, переключаемся на InMemoryLockManager.");
                    }
                }

                return new InMemoryLockManager(
                    sp.GetRequiredService<PermissionChecker>(),
                    sp.GetRequiredService<ILogger<InMemoryLockManager>>());
            });

            services.AddSingleton<ICommandPipelineBehavior, CommandAuthorizationBehavior>();

            // 9. Менеджер согласованности
            services.AddSingleton<IConsistencyManager>(sp =>
                new ConsistencyManager(
                    sp,
                    sp.GetRequiredService<IDistributedLockManager>(),
                    sp.GetRequiredService<ILogger<ConsistencyManager>>(),
                    sp.GetRequiredService<IMetricsCollector>()));

            // 10. Event Store
            services.AddSingleton<IEventStore>(sp =>
                new PostgresEventStore(
                    connString,
                    sp.GetRequiredService<ISnapshotStore>(),
                    sp.GetRequiredService<IConsistencyManager>(),
                    sp.GetRequiredService<ILogger<PostgresEventStore>>(),
                    sp.GetRequiredService<IMetricsCollector>(),
                    sp.GetRequiredService<IEventBus>()));

            // 11. Проекции
            services.AddSingleton<CharacterProjection>();
            services.AddSingleton<CombatProjection>(sp =>
                new CombatProjection(
                    sp.GetRequiredService<ICacheProvider>(),
                    sp.GetRequiredService<CharacterProjection>(),
                    TimeSpan.FromMinutes(1)));
            services.AddSingleton<CampaignProjection>();
            services.AddSingleton<JourneyProjection>();

            // 12. Обработчики запросов
            services.AddSingleton<CharacterQueryHandler>();
            services.AddSingleton<CombatQueryHandler>();
            services.AddSingleton<CampaignQueryHandler>();
            services.AddSingleton<JourneyQueryHandler>();
            RegisterQueryHandlers(services);

            // 13. Шина сообщений
            services.AddSingleton<InMemoryBus>(sp => new InMemoryBus(sp, sp.GetRequiredService<ILogger<InMemoryBus>>()));
            services.AddSingleton<IQueryBus>(sp => sp.GetRequiredService<InMemoryBus>());

            services.AddSingleton<RabbitMqBus>(sp =>
            {
                var rabbitMqUri = configuration.GetConnectionString("RabbitMq");
                return new RabbitMqBus(
                    string.IsNullOrEmpty(rabbitMqUri) ? "amqp://invalid" : rabbitMqUri,
                    sp,
                    sp.GetRequiredService<ILogger<RabbitMqBus>>());
            });

            services.AddSingleton<MessageBusSelector>(sp =>
            {
                var inMemoryBus = sp.GetRequiredService<InMemoryBus>();
                return new MessageBusSelector(inMemoryBus, inMemoryBus, sp.GetRequiredService<ILogger<MessageBusSelector>>());
            });

            services.AddSingleton<ICommandBus>(sp => sp.GetRequiredService<MessageBusSelector>().CommandBus);
            services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<MessageBusSelector>().EventBus);

            // 14. Обработчики команд (классы)
            services.AddSingleton<CharacterHandler>();
            services.AddSingleton<MovementHandler>();
            services.AddSingleton<RestHandler>();
            services.AddSingleton<CampaignHandler>();
            services.AddSingleton<CombatHandler>();
            services.AddSingleton<TravelHandler>();
            RegisterCommandHandlers(services);

            // 15. Обработчики событий
            services.AddSingleton<LoggingHandler>();
            services.AddSingleton<MetricHandler>();
            services.AddSingleton<NotificationHandler>();
            services.AddSingleton<AiHandler>();
            services.AddSingleton<ReplayHandler>();
            services.AddSingleton<TriggerHandler>();
            services.AddSingleton<WebhookHandler>();

            // 16. Саги
            services.AddSingleton<ISagaRegistry, SagaRegistry>();
            services.AddSingleton<SagaCoordinator>();
            services.AddSingleton<ISagaDispatcher>(sp => sp.GetRequiredService<SagaCoordinator>());

            // 17. Сервисы приложения (без CombatService, он не используется)
            services.AddSingleton<CraftingService>();
            services.AddSingleton<DialogService>();
            services.AddSingleton<TradeService>();
            services.AddSingleton<TravelService>();

            // 18. Безопасность
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddSingleton<IAuthProvider, AuthProvider>();
            services.AddSingleton<ITokenService, TokenService>();
            services.AddSingleton<PermissionChecker>();
            services.AddSingleton<PolicyEnforcer>();
            services.AddSingleton<IUserSecurityContextProvider, HttpUserSecurityContextProvider>();

            // 19. AI и восприятие
            services.AddSingleton<IBlackboardStore, BlackboardStore>();
            services.AddSingleton<MonsterAi>();
            services.AddSingleton<PerceptionPipeline>();
            services.AddSingleton<ScriptEngine>();

            // 20. Undo/Redo
            services.AddSingleton<UndoManager>();

            // 21. Локализация (регистрируем LocaleManager и интерфейс один раз)
            services.AddSingleton<ILocaleProvider>(sp =>
            {
                var localesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Locales");
                return new JsonFileLocaleProvider(localesPath, sp.GetRequiredService<ILogger<JsonFileLocaleProvider>>());
            });
            services.AddSingleton<LocaleManager>();
            services.AddSingleton<ILocaleManager>(sp => sp.GetRequiredService<LocaleManager>());

            // 22. Мониторинг
            services.AddSingleton<IMetricsCollector, MetricsCollector>();
            services.AddSingleton<ITracer>(sp => new SimpleTracer(sp.GetRequiredService<ILogger<SimpleTracer>>()));
            services.AddSingleton<IHealthCheck, DndHealthCheck>();

            // 23. Сетевые компоненты
            services.AddSingleton<ISessionManager, SessionManager>();
            services.AddSingleton<INetworkProtocol, JsonNetworkProtocol>();
            services.AddSingleton<IRateLimiter, RateLimiter>();
            services.AddSingleton<WebSocketHandler>();
            services.AddSingleton<GameServer>(sp =>
                new GameServer(
                    sp.GetRequiredService<IOptions<GameServerConfiguration>>().Value,
                    sp,
                    sp.GetRequiredService<ICommandBus>(),
                    sp.GetRequiredService<IEventBus>(),
                    sp.GetRequiredService<IQueryBus>(),
                    sp.GetRequiredService<ISessionManager>(),
                    sp.GetRequiredService<PermissionChecker>(),
                    sp.GetRequiredService<IMetricsCollector>(),
                    sp.GetRequiredService<ITracer>(),
                    sp.GetRequiredService<IAuthProvider>(),
                    sp.GetRequiredService<ILogger<GameServer>>()));

            // 24. Мировые службы
            services.AddSingleton<IGridProvider>(sp =>
            {
                var width = configuration.GetValue<int?>("World:GridWidth") ?? 100;
                var height = configuration.GetValue<int?>("World:GridHeight") ?? 100;
                var type = configuration.GetValue<GridType?>("World:GridType") ?? GridType.Square;
                return new GridProvider(width, height, type);
            });
            services.AddSingleton<VisibilityCalculator>();

            // 25. HTTP-клиент для вебхуков
            services.AddHttpClient<IWebhookClient, DefaultWebhookClient>();

            // 26. Миграции
            services.AddSingleton<DatabaseMigrator>(sp =>
                new DatabaseMigrator(connString,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migrations"),
                    sp.GetRequiredService<ILogger<DatabaseMigrator>>()));

            // 27. Фоновые сервисы
            services.AddHostedService<RefreshTokenCleanupService>();

            // 28. FluentValidation
            services.AddFluentValidationAutoValidation();
            services.AddValidatorsFromAssemblyContaining<CreateCharacterRequestValidator>();

            // 29. Обработка ошибок валидации
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kv => kv.Key,
                            kv => kv.Value?.Errors.Select(e => e.ErrorMessage).ToArray());
                    return new BadRequestObjectResult(new { errors });
                };
            });

            // 30. DM Tools
            services.AddSingleton(sp => new DmUi(
                sp.GetRequiredService<ICommandBus>(),
                sp.GetRequiredService<CharacterProjection>(),
                sp.GetRequiredService<CombatProjection>(),
                sp.GetRequiredService<CampaignProjection>(),
                sp.GetRequiredService<PermissionChecker>()));
            services.AddSingleton(sp => new OverrideCommands(
                sp.GetRequiredService<ICommandBus>(),
                sp.GetRequiredService<CharacterProjection>()));
            services.AddSingleton(sp => new DmUndoManager(
                sp.GetRequiredService<UndoManager>(),
                sp.GetRequiredService<ICommandBus>(),
                sp.GetRequiredService<PermissionChecker>(),
                sp.GetRequiredService<ILogger<DmUndoManager>>()));

            // 31. Дополнительные службы для ReplayHandler
            services.AddSingleton<ICurrentSessionProvider, DefaultCurrentSessionProvider>();
            services.AddSingleton<INarrativeLogBuilder, DefaultNarrativeLogBuilder>();

            return services;
        }

        private static void RegisterQueryHandlers(IServiceCollection services)
        {
            services.AddSingleton<IQueryHandler<GetCharacterById, CharacterDto?>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetAllCharacters, List<CharacterDto>>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterHitPoints, CharacterHitPointsDto?>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterCombatStats, CharacterCombatStatsDto?>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterSpells, CharacterSpellsDto?>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterInventory, List<InventoryItemDto>>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterEquipment, List<EquippedItemDto>>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterDeathStatus, CharacterDeathStatusDto?>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterConditions, List<string>>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCharacterDefenses, CharacterDefensesDto?>, CharacterQueryHandler>();
            services.AddSingleton<IQueryHandler<SearchCharacters, List<CharacterSummaryDto>>, CharacterQueryHandler>();

            services.AddSingleton<IQueryHandler<GetCombatStatus, CombatStatusDto?>, CombatQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCombatParticipants, List<CombatParticipantDto>>, CombatQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCurrentCombatParticipant, CombatParticipantDto?>, CombatQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCombatRound, int>, CombatQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCombatTurnOrder, List<Guid>>, CombatQueryHandler>();
            services.AddSingleton<IQueryHandler<IsCombatActive, bool>, CombatQueryHandler>();

            services.AddSingleton<IQueryHandler<GetActiveQuests, List<Guid>>, CampaignQueryHandler>();
            services.AddSingleton<IQueryHandler<GetQuestDetails, QuestInfo?>, CampaignQueryHandler>();
            services.AddSingleton<IQueryHandler<GetQuestsByStatus, List<QuestInfo>>, CampaignQueryHandler>();
            services.AddSingleton<IQueryHandler<GetCampaignState, CampaignState?>, CampaignQueryHandler>();
            services.AddSingleton<IQueryHandler<GetFactionReputation, FactionState?>, CampaignQueryHandler>();
            services.AddSingleton<IQueryHandler<GetAllFactions, List<FactionState>>, CampaignQueryHandler>();
            services.AddSingleton<IQueryHandler<GetActiveWorldEvents, List<string>>, CampaignQueryHandler>();
            services.AddSingleton<IQueryHandler<GetJourneyStatus, JourneyStateDto?>, JourneyQueryHandler>();
        }

        private static void RegisterCommandHandlers(IServiceCollection services)
        {
            // Характер команды
            services.AddSingleton<ICommandHandler<CreateCharacter>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<UpdateCharacter>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<DealDamage>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<HealCharacter>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<SetTemporaryHitPoints>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<GainExperience>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<LevelUpCharacter>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<SetAbilityScore>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddSkillProficiency>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveSkillProficiency>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddSavingThrowProficiency>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveSavingThrowProficiency>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<ChooseRace>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<ChooseClass>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<ChooseBackground>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddFeat>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveFeat>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddSpell>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveSpell>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<PrepareSpell>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<UnprepareSpell>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<UseSpellSlot>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RestoreAllSpellSlots>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<ApplyCondition>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveCondition>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<ClearAllConditionsCommand>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<UpdateArmorClass>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<UpdateSpeed>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddResistance>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveResistance>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddVulnerability>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveVulnerability>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddImmunity>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveImmunity>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<EquipItem>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<UnequipItem>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddInventoryItem>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<RemoveInventoryItem>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<DeathSavingThrow>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<StabilizeCharacter>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<ReviveCharacter>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<ResetDeathSavingThrows>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<AddGold>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<SpendGold>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<SetGoldCommand>, CharacterHandler>();
            services.AddSingleton<ICommandHandler<UpdateProficiencyBonus>, CharacterHandler>();

            // Перемещение
            services.AddSingleton<ICommandHandler<MoveCharacter>, MovementHandler>();
            services.AddSingleton<ICommandHandler<MoveCharacterToPosition>, MovementHandler>();
            services.AddSingleton<ICommandHandler<MoveCharacterWithDash>, MovementHandler>();
            services.AddSingleton<ICommandHandler<MoveCharacterWithDisengage>, MovementHandler>();
            services.AddSingleton<ICommandHandler<MoveCharacterStealthily>, MovementHandler>();
            services.AddSingleton<ICommandHandler<ClimbCharacter>, MovementHandler>();
            services.AddSingleton<ICommandHandler<SwimCharacter>, MovementHandler>();
            services.AddSingleton<ICommandHandler<FlyCharacter>, MovementHandler>();
            services.AddSingleton<ICommandHandler<BurrowCharacter>, MovementHandler>();
            services.AddSingleton<ICommandHandler<JumpCharacter>, MovementHandler>();
            services.AddSingleton<ICommandHandler<SetCharacterSpeed>, MovementHandler>();
            services.AddSingleton<ICommandHandler<ResetCharacterSpeed>, MovementHandler>();
            services.AddSingleton<ICommandHandler<ApplyDifficultTerrain>, MovementHandler>();
            services.AddSingleton<ICommandHandler<RemoveDifficultTerrain>, MovementHandler>();
            services.AddSingleton<ICommandHandler<ApplyMovementImpairment>, MovementHandler>();
            services.AddSingleton<ICommandHandler<RemoveMovementImpairment>, MovementHandler>();
            services.AddSingleton<ICommandHandler<MakeAthleticsCheckForMovement>, MovementHandler>();
            services.AddSingleton<ICommandHandler<MakeAcrobaticsCheckForMovement>, MovementHandler>();
            services.AddSingleton<ICommandHandler<TakeFallDamage>, MovementHandler>();

            // Отдых
            services.AddSingleton<ICommandHandler<StartRest>, RestHandler>();
            services.AddSingleton<ICommandHandler<EndRest>, RestHandler>();
            services.AddSingleton<ICommandHandler<SpendHitDie>, RestHandler>();
            services.AddSingleton<ICommandHandler<InterruptRest>, RestHandler>();

            // Кампания
            services.AddSingleton<ICommandHandler<AcceptQuestCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<CompleteQuestCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<FailQuestCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<CreateQuestCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<UpdateQuestObjectiveCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<ChangeFactionReputationCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<DeleteQuestCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<CreateCampaignCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<AddPlayerToCampaignCommand>, CampaignHandler>();
            services.AddSingleton<ICommandHandler<RemovePlayerFromCampaignCommand>, CampaignHandler>();

            // Бой
            services.AddSingleton<ICommandHandler<StartCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<EndCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<RollInitiative>, CombatHandler>();
            services.AddSingleton<ICommandHandler<StartRound>, CombatHandler>();
            services.AddSingleton<ICommandHandler<NextTurn>, CombatHandler>();
            services.AddSingleton<ICommandHandler<EndRound>, CombatHandler>();
            services.AddSingleton<ICommandHandler<AddParticipantToCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<RemoveParticipantFromCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<TakeMoveAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<TakeStandardAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<TakeBonusAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<TakeReaction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<ReadyAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<TriggerReadyAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<DealDamageToTarget>, CombatHandler>();
            services.AddSingleton<ICommandHandler<HealTarget>, CombatHandler>();
            services.AddSingleton<ICommandHandler<ApplyConditionToTarget>, CombatHandler>();
            services.AddSingleton<ICommandHandler<RemoveConditionFromTarget>, CombatHandler>();
            services.AddSingleton<ICommandHandler<MakeSavingThrowInCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<MakeDeathSavingThrowInCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<StabilizeInCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<MakeConcentrationCheck>, CombatHandler>();
            services.AddSingleton<ICommandHandler<DelayTurn>, CombatHandler>();
            services.AddSingleton<ICommandHandler<SurrenderInCombat>, CombatHandler>();
            services.AddSingleton<ICommandHandler<PerformAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<HelpAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<HideAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<SearchAction>, CombatHandler>();
            services.AddSingleton<ICommandHandler<UseObjectAction>, CombatHandler>();

            // Путешествия
            services.AddSingleton<ICommandHandler<StartJourneyCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<EndJourneyCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<TravelDayCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<SetTravelPaceCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<ForcedMarchCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<NavigationCheckCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<PartyLostCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<ConsumeResourcesCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<RandomEncounterCheckCommand>, TravelHandler>();
            services.AddSingleton<ICommandHandler<ApplyExhaustionCommand>, TravelHandler>();
        }
    }
}