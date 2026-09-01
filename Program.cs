using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.events;
using dnd_game.infrastructure.coordination;
using dnd_game.infrastructure.event_store;
using dnd_game.infrastructure.exceptions;
using dnd_game.infrastructure.localization;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.security;
using dnd_game.infrastructure.seeding;
using dnd_game.migrations;
using dnd_game.presentation.api;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.Reflection;
using System.Text;

namespace dnd_game
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            // ============================================================
            // 1. Настройка Serilog
            // ============================================================
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("dnd_game.infrastructure.coordination.SagaCoordinator", LogEventLevel.Debug)
                .MinimumLevel.Override("dnd_game.presentation.api.WebSocketHandler", LogEventLevel.Information)
                .MinimumLevel.Override("dnd_game.infrastructure.event_store.PostgresEventStore", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: "logs/dnd_game-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            var builder = WebApplication.CreateBuilder(args);
            // 1. Получаем секрет из конфигурации
            var jwtSecret = builder.Configuration["Token:Secret"];
            if (string.IsNullOrEmpty(jwtSecret))
            {
                throw new InvalidOperationException("Token:Secret is not configured.");
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false; // для разработки
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = false, // если не проверяете издателя
                    ValidateAudience = false, // если не проверяете аудиторию
                    ClockSkew = TimeSpan.Zero
                };
            });

            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("RequireAdmin", policy =>
                    policy.RequireRole("Admin"));
            });
            builder.Host.UseSerilog();

            // ============================================================
            // 2. Базовые сервисы, которые не покрываются AddGameServices
            // ============================================================
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<ICurrentUserService, CurrentUserService>();

            // ============================================================
            // 3. Основные игровые сервисы (включая EventStore, шины, проекции, обработчики, безопасность и т.д.)
            // ============================================================
            // Регистрируем JwtSettings как одиночный экземпляр (не только IOptions)
            builder.Services.AddSingleton(sp =>
                sp.GetRequiredService<IOptions<JwtSettings>>().Value);

            // Если IDistributedLockManager не зарегистрирован в AddGameServices, добавьте здесь:
            builder.Services.AddSingleton<IDistributedLockManager, InMemoryLockManager>();
            builder.Services.AddGameServices(builder.Configuration);

            // ============================================================
            // 4. ASP.NET Core: контроллеры, валидация, обработка ошибок, Swagger
            // ============================================================
            builder.Services.AddControllers();
            builder.Services.AddFluentValidationAutoValidation();
            builder.Services.AddValidatorsFromAssemblyContaining<Program>();

            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "DnD Game API",
                    Version = "v1",
                    Description = "Backend для D&D-подобной RPG на Event Sourcing + CQRS"
                });

                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT токен в формате: Bearer {token}",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });

                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                    c.IncludeXmlComments(xmlPath);
            });

            // ============================================================
            // 5. CORS
            // ============================================================
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
            if (allowedOrigins == null || allowedOrigins.Length == 0)
            {
                if (builder.Environment.IsDevelopment())
                {
                    builder.Services.AddCors(options =>
                    {
                        options.AddPolicy("AllowSpecificOrigins", policy =>
                        {
                            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                        });
                    });
                }
                else
                {
                    throw new InvalidOperationException("CORS: список разрешённых источников не задан в конфигурации.");
                }
            }
            else
            {
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy("AllowSpecificOrigins", policy =>
                    {
                        policy.WithOrigins(allowedOrigins)
                              .AllowAnyMethod()
                              .AllowAnyHeader()
                              .AllowCredentials();
                    });
                });
            }

            // ============================================================
            // 6. Построение приложения
            // ============================================================
            var app = builder.Build();

            // =====================================================================
            // Восстановление проекций и подписка на события при старте
            // =====================================================================
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                // Восстановление проекций
                var eventStore = services.GetRequiredService<IEventStore>();
                var characterProjection = services.GetRequiredService<CharacterProjection>();
                var campaignProjection = services.GetRequiredService<CampaignProjection>();
                var combatProjection = services.GetRequiredService<CombatProjection>();

                await characterProjection.RebuildAsync(eventStore);

                var allEvents = await eventStore.GetAllEvents();
                foreach (var eventObj in allEvents)
                {
                    if (eventObj is IDomainEvent domainEvent)
                    {
                        campaignProjection.Apply(domainEvent);
                        combatProjection.Apply(domainEvent);
                    }
                }

                // Инициализация RabbitMQ с fallback
                var rabbitMqBus = services.GetRequiredService<RabbitMqBus>();
                var selector = services.GetRequiredService<MessageBusSelector>();
                try
                {
                    await rabbitMqBus.InitializeAsync();
                    selector.UseRabbitMq(rabbitMqBus);
                    Log.Information("RabbitMQ подключён, используется для команд и событий.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Не удалось подключиться к RabbitMQ, используется InMemoryBus.");
                    // Селектор остаётся на InMemoryBus
                }
            }

            // ============================================================
            // 7. Миграции базы данных
            // ============================================================
            var migrator = app.Services.GetRequiredService<DatabaseMigrator>();
            if (!migrator.Migrate())
            {
                Log.Fatal("Database migration failed. Exiting.");
                return;
            }
            // Сидинг рецептов
            var recipeSeeder = app.Services.GetRequiredService<RecipeSeeder>();
            await recipeSeeder.SeedAsync();
            var tradeSeeder = app.Services.GetRequiredService<TradeSeeder>();
            await tradeSeeder.SeedAsync();

            // ============================================================
            // 8. Регистрация саг и подписок проекций на события
            // ============================================================
            // Подписываем проекции на события
            ProjectionRegistrations.RegisterAll(app.Services);

            // Регистрируем саги и подписываем их на события
            SagaRegistrations.RegisterAll(app.Services);
            // ============================================================
            // 9. Загрузка языка
            // ============================================================
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(
                    Path.Combine(builder.Environment.ContentRootPath, "Resources", "Locales")),
                RequestPath = "/locales"
            });

            var localeManager = app.Services.GetRequiredService<LocaleManager>();
            await localeManager.LoadLocaleAsync("ru");
            await localeManager.LoadLocaleAsync("en");

            // ============================================================
            // 10. Middleware
            // ============================================================
            app.UseAuthentication();
            app.UseMiddleware<UserActivityMiddleware>();
            app.UseCors("AllowSpecificOrigins");
            app.UseStaticFiles();

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "DnD Game API v1");
                c.RoutePrefix = "swagger";
            });

            app.UseWebSockets();

            app.Map("/ws", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                await handler.HandleAsync(
                    webSocket,
                    context,
                    context.RequestAborted,
                    context.Connection.RemoteIpAddress
                );
            });

            app.MapControllers();
            app.MapFallbackToFile("index.html");

            // ============================================================
            // 11. Запуск
            // ============================================================
            var url = Environment.GetEnvironmentVariable("APP_URL") ?? "http://0.0.0.0:5000";
            Log.Information("Starting application on {Url}", url);

            try
            {
                await app.RunAsync(url);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}