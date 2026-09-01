# 🎲 DnD Game - D&D RPG Backend на Event Sourcing + CQRS

Полнофункциональный backend для D&D-подобной RPG-игры, построенный на архитектурных паттернах **Event Sourcing** и **CQRS**. Приложение предоставляет полный стек функциональности для управления персонажами, кампаниями, боями, диалогами и другими игровыми механиками.

## 📋 Содержание

- [Обзор](#-обзор)
- [Ключевые возможности](#-ключевые-возможности)
- [Технологический стек](#-технологический-стек)
- [Архитектура](#-архитектура)
- [Быстрый старт](#-быстрый-старт)
- [API Документация](#-api-документация)
- [Структура проекта](#-структура-проекта)
- [Конфигурация](#-конфигурация)
- [Базы данных](#-базы-данных)
- [Аутентификация и авторизация](#-аутентификация-и-авторизация)
- [Игровые сервисы](#-игровые-сервисы)
- [Мониторинг и логирование](#-мониторинг-и-логирование)
- [Лицензия](#-лицензия)

---

## 🎯 Обзор

**DnD Game** — это backend-приложение на **.NET 8** с поддержкой микросервисной архитектуры. Приложение реализует:

- **Event Sourcing**: полная история всех событий в игре
- **CQRS (Command Query Responsibility Segregation)**: разделение команд и запросов
- **Проекции**: несколько read-моделей для разных случаев использования
- **Саги**: управление сложными транзакциями между агрегатами
- **WebSocket**: real-time обновления состояния игры
- **JWT авторизация**: защита API с помощью токенов
- **PostgreSQL + Redis**: надёжное хранилище и кэширование
- **RabbitMQ**: асинхронный message bus с fallback на in-memory
- **Локализация**: поддержка русского и английского языков

---

## ✨ Ключевые возможности

### Управление персонажами
- Создание, редактирование, удаление персонажей
- Система характеристик (Strength, Dexterity, Constitution и т.д.)
- Управление inventarem (предметами и оборудованием)
- Система уровней и опыта
- История персонажа и изменения

### Кампании и миры
- Создание и управление кампаниями
- Роли игроков (Game Master, Player, Observer)
- Система местоположений и регионов
- Условия окружения (weather, time of day)

### Система боя
- Real-time боевая система
- Инициатива и очередность действий
- Система броска костей (d20, d12, d6 и т.д.)
- Условия боя (stunned, poisoned, bleeding и т.д.)
- Спеллы и способности

### Торговля и крафт
- Торговля между персонажами
- Система крафта с рецептами
- NPC торговцы с системой цен
- Ограничения на количество предметов

### Диалоги и повествование
- Система диалогов с NPC
- Ветвящиеся диалоги с условиями
- Эффекты диалогов (изменение репутации, получение квестов)
- Состояние диалога с сохранением прогресса

### Перемещение и путешествия
- Тактическое движение на картах
- Система скорости и действий (Dash)
- Специальные типы движения (Climb, Swim, Fly, Burrow)
- Система маршрутов между местоположениями

### AI и скрипты
- Движок для выполнения скриптов
- Условные выражения и циклы в скриптах
- AI-контролируемые враги и NPC
- Вебхуки для интеграции

### Мониторинг и диагностика
- Health check эндпоинты
- Просмотр состояния БД, EventStore, Message Bus
- Воспроизведение событий (replay)
- Логи и трейсинг

---

## 🛠 Технологический стек

| Область | Технология |
|---------|-----------|
| **Framework** | ASP.NET Core 8 |
| **Language** | C# 12 |
| **API** | REST API + WebSocket |
| **Authentication** | JWT Bearer Tokens |
| **Database** | PostgreSQL 14+ |
| **Caching** | Redis 7+ |
| **Message Bus** | RabbitMQ 3.12+ |
| **Logging** | Serilog (Console, File, Elasticsearch, Loki) |
| **Validation** | FluentValidation |
| **API Docs** | Swagger/OpenAPI |
| **Monitoring** | OpenTelemetry |
| **Deployment** | Docker + Docker Compose |

---

## 🏗 Архитектура

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│  (REST API Controllers, WebSocket Handler, Swagger)         │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│              Application Layer (CQRS)                        │
│  (Command/Query Handlers, Services, Projections, Sagas)     │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│                  Domain Layer                               │
│      (Aggregates, Value Objects, Domain Events)             │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│             Infrastructure Layer                            │
│  (EventStore, Repositories, Message Bus, Security, etc.)    │
└─────────────────────────────────────────────────────────────┘
```

### Основные компоненты

- **Event Store (PostgreSQL)**: хранилище всех доменных событий
- **Projections**: read-модели (Character, Campaign, Combat и т.д.)
- **Command Bus**: маршрутизация команд к обработчикам
- **Query Bus**: маршрутизация запросов к обработчикам
- **Saga Coordinator**: управление долгоживущими транзакциями
- **WebSocket Handler**: real-time обновления для клиентов

---

## 🚀 Быстрый старт

### Предварительные требования

- **.NET 8** или выше
- **Docker** и **Docker Compose**
- **PostgreSQL** 14+ (или используйте контейнер)
- **Redis** 7+ (или используйте контейнер)
- **RabbitMQ** 3.12+ (опционально, есть fallback)

### Установка и запуск

#### 1. Клонирование репозитория

```bash
git clone https://github.com/mvchemezov1/dnd-game.git
cd dnd-game
```

#### 2. Установка секрета (Development)

```bash
dotnet user-secrets set "Token:Secret" "your-very-long-secret-key-at-least-32-characters-long"
```

#### 3. Запуск с Docker Compose (рекомендуется)

```bash
docker-compose up -d
```

Это поднимет:
- PostgreSQL на `localhost:5432`
- Redis на `localhost:6379`
- RabbitMQ на `localhost:5672` (Web UI: `localhost:15672`)

#### 4. Запуск приложения

```bash
dotnet run
```

Приложение запустится на `http://localhost:5000`

#### 5. Доступ к Swagger UI

```
http://localhost:5000/swagger
```

---

## 📚 API Документация

### Основные endpoints

#### Аутентификация

```http
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
```

#### Персонажи

```http
GET    /api/characters              # Список персонажей пользователя
POST   /api/characters              # Создать персонажа
GET    /api/characters/{id}         # Получить персонажа
PUT    /api/characters/{id}         # Обновить персонажа
DELETE /api/characters/{id}         # Удалить персонажа
GET    /api/characters/{id}/items   # Инвентарь
```

#### Кампании

```http
GET    /api/campaigns               # Список кампаний
POST   /api/campaigns               # Создать кампанию
GET    /api/campaigns/{id}          # Получить кампанию
PUT    /api/campaigns/{id}          # Обновить кампанию
```

#### Боевая система

```http
POST   /api/combat/start            # Начать бой
GET    /api/combat/{id}/state       # Состояние боя
POST   /api/combat/{id}/action      # Выполнить действие в бою
POST   /api/combat/{id}/end         # Завершить бой
```

#### Торговля и крафт

```http
POST   /api/crafting/start          # Начать крафт
GET    /api/crafting/recipes        # Список доступных рецептов
GET    /api/crafting/processes      # Активные процессы крафта
POST   /api/trade/offer             # Предложить обмен
POST   /api/trade/accept            # Принять обмен
```

#### Диалоги

```http
POST   /api/dialog/start            # Начать диалог
POST   /api/dialog/option           # Выбрать вариант ответа
```

#### Перемещение

```http
POST   /api/travel/move             # Переместить персонажа
POST   /api/travel/dash             # Использовать Dash
POST   /api/travel/special-movement # Специальное перемещение
```

#### Управление пользователями (Admin)

```http
GET    /api/users                   # Список пользователей
GET    /api/users/{id}              # Информация о пользователе
PUT    /api/users/{id}/role         # Изменить роль пользователя
PUT    /api/users/{id}/status       # Изменить статус пользователя
```

#### Диагностика (Admin)

```http
GET    /api/dev/health              # Состояние системы
GET    /api/dev/scripts             # Список зарегистрированных скриптов
GET    /api/dev/webhooks            # Список вебхуков
GET    /api/dev/replay/{aggregateId} # Воспроизведение событий агрегата
```

### WebSocket

```javascript
// Подключение к WebSocket
const ws = new WebSocket('ws://localhost:5000/ws');

// Слушание обновлений состояния игры
ws.onmessage = (event) => {
    const message = JSON.parse(event.data);
    console.log('Game update:', message);
};

// Отправка команды через WebSocket
ws.send(JSON.stringify({
    type: 'CharacterMoved',
    data: { characterId, x, y }
}));
```

> **Для подробной документации API**, откройте Swagger UI после запуска приложения

---

## 📁 Структура проекта

```
dnd-game/
├── application/              # Application Layer (CQRS)
│   ├── command_handlers/    # Обработчики команд
│   ├── query_handlers/      # Обработчики запросов
│   ├── event_handlers/      # Обработчики событий
│   ├── projections/         # Read-модели (Character, Campaign, Combat)
│   ├── services/            # Application сервисы
│   ├── security/            # Авторизация и разрешения
│   └── notifications/       # Система уведомлений
│
├── domain/                   # Domain Layer
│   ├── commands/            # Доменные команды
│   ├── events/              # Доменные события
│   ├── queries/             # Доменные запросы
│   ├── aggregates/          # Агрегаты (Character, Campaign, Combat)
│   ├── value_objects/       # Value Objects
│   ├── interfaces/          # Repository интерфейсы
│   └── sagas/               # Саги для координации
│
├── infrastructure/          # Infrastructure Layer
│   ├── event_store/        # Event Store (PostgreSQL)
│   ├── persistence/        # Репозитории и контекст БД
│   ├── message_bus/        # RabbitMQ + In-Memory fallback
│   ├── security/           # JWT, токены, хеширование паролей
│   ├── caching/            # Redis кэширование
│   ├── coordination/       # Саги и координация
│   ├── ai/                 # AI и скрипты
│   ├── localization/       # Локализация (i18n)
│   ├── monitoring/         # Health checks и метрики
│   ├── exceptions/         # Обработка исключений
│   └── seeding/            # Инициализация данных
│
├── presentation/            # Presentation Layer
│   ├── api/                # REST Controllers
│   │   ├── rest_api.cs                    # Base controller + Characters
│   │   ├── UserManagementController.cs    # User management (Admin)
│   │   ├── CraftingController.cs          # Crafting
│   │   ├── TradeController.cs             # Trading
│   │   ├── TravelController.cs            # Movement & Travel
│   │   ├── DialogController.cs            # Dialogs
│   │   ├── DevController.cs               # Diagnostics (Admin)
│   │   ├── dependencies.cs                # DI setup
│   │   └── validators/                    # FluentValidation validators
│   ├── dm_tools/           # DM tools и утилиты
│   └── api/WebSocketHandler.cs # WebSocket real-time updates
│
├── migrations/              # Database migrations
├── docs/                    # Документация
├── Resources/               # Локализация (YAML/JSON)
│   └── Locales/            # en.yaml, ru.yaml
├── tests/                   # Unit tests, integration tests
├── Dockerfile              # Docker образ
├── docker-compose.yml      # Docker Compose конфигурация
├── appsettings.json        # Конфигурация приложения
├── dnd_game.csproj         # Project file
└── Program.cs              # Entry point
```

---

## ⚙️ Конфигурация

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dnd_game;Username=postgres;Password=password",
    "Redis": "localhost:6379"
  },
  "Jwt": {
    "Issuer": "dnd-game",
    "Audience": "dnd-game-client"
  },
  "Token": {
    "Secret": "your-secret-key-change-in-production",
    "AccessTokenExpirationMinutes": 30,
    "RefreshTokenExpirationDays": 7
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000", "http://localhost:8080"]
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "UserName": "guest",
    "Password": "guest"
  },
  "Game": {
    "Limits": {
      "MaxInventoryItems": 100,
      "MaxCharactersPerUser": 10,
      "MaxCraftingProcesses": 5
    }
  },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "your-email@gmail.com",
    "Password": "your-app-password"
  }
}
```

### Переменные окружения

```bash
# Database
CONNECTIONSTRINGS_DEFAULTCONNECTION=Host=localhost;Port=5432;Database=dnd_game;...
CONNECTIONSTRINGS_REDIS=localhost:6379

# Security
TOKEN_SECRET=your-very-long-secret-key

# Application
APP_URL=http://0.0.0.0:5000
ASPNETCORE_ENVIRONMENT=Development

# RabbitMQ
RABBITMQ_HOSTNAME=localhost
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest
```

---

## 🗄 Базы данных

### PostgreSQL

Приложение использует **Event Store** в PostgreSQL для хранения всех доменных событий.

#### Основные таблицы:

- `events` — все доменные события
- `event_store_checkpoints` — контрольные точки проекций
- `characters` — проекция персонажей
- `campaigns` — проекция кампаний
- `combat` — проекция боев
- `items` — предметы в игре
- `recipes` — рецепты крафта
- `users` — учётные записи пользователей
- `refresh_tokens` — refresh-токены для JWT
- `audit_log` — логирование действий

### Redis

Используется для:
- Кэширования проекций
- Хранения состояния WebSocket сессий
- Rate limiting

### RabbitMQ (опционально)

При отсутствии RabbitMQ приложение использует **in-memory message bus** с полной функциональностью.

---

## 🔐 Аутентификация и авторизация

### JWT токены

Приложение использует **JWT Bearer tokens** для защиты API.

#### Генерация токена

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "player@example.com",
  "password": "password123"
}

Response:
{
  "accessToken": "eyJhbGc...",
  "refreshToken": "eyJhbGc...",
  "expiresIn": 1800
}
```

#### Использование токена

```http
GET /api/characters
Authorization: Bearer eyJhbGc...
X-Session-Id: 123e4567-e89b-12d3-a456-426614174000
```

### Роли и разрешения

| Роль | Разрешения |
|------|-----------|
| **Admin** | Управление пользователями, доступ к dev endpoints, полный контроль |
| **GameMaster** | Управление кампанией, NPC, диалогами, созданием событий |
| **Player** | Управление своими персонажами, участие в кампании |
| **Observer** | Только просмотр игровой сессии |

### Политики доступа

```csharp
// Требует роль Admin
[Authorize(Policy = "RequireAdmin")]

// Требует аутентификацию
[Authorize]

// Открыт для всех
// [AllowAnonymous]
```

### Проверка разрешений

```csharp
var permissionChecker = serviceProvider.GetRequiredService<PermissionChecker>();

// Проверка владения персонажем
if (!await permissionChecker.CanControlCharacterAsync(characterId, ct))
    throw new UnauthorizedAccessException();

// Проверка роли в кампании
if (!await permissionChecker.IsGameMasterOfCampaignAsync(campaignId, ct))
    throw new UnauthorizedAccessException();
```

---

## 🎮 Игровые сервисы

### CharacterService

Управление персонажами и их характеристиками.

```csharp
var character = await characterService.CreateCharacterAsync(request, cancellationToken);
```

### CombatService

Система боя с инициативой, действиями и условиями.

```csharp
var combatState = await combatService.StartCombatAsync(characterIds, cancellationToken);
await combatService.TakeCombatActionAsync(characterId, action, cancellationToken);
```

### CraftingService

Крафт с системой рецептов и временем выполнения.

```csharp
var recipes = await craftingService.GetAvailableRecipesAsync(characterId, cancellationToken);
var process = await craftingService.StartCraftingAsync(characterId, recipeId, cancellationToken);
```

### TradeService

Торговля между персонажами и NPC.

```csharp
var offer = await tradeService.ProposeTradeAsync(fromCharacterId, toCharacterId, ...);
await tradeService.AcceptTradeAsync(offerId, cancellationToken);
```

### DialogService

Система диалогов с ветвлением и эффектами.

```csharp
var dialogState = await dialogService.StartDialogueAsync(dialogueId, npcId, characterId, ct);
var nextState = await dialogService.SelectOptionAsync(dialogueId, optionId, ct);
```

### TravelService

Перемещение и путешествия на картах.

```csharp
await travelService.MoveCharacterAsync(characterId, targetX, targetY, ct);
await travelService.DashAsync(characterId, ct);
```

---

## 📊 Мониторинг и логирование

### Serilog логирование

Логи пишутся в:
- **Console** — вывод в консоль
- **File** — `logs/dnd_game-{date}.txt` (ротация по дням)
- **Elasticsearch** (опционально)
- **Loki** (опционально, для Grafana)

#### Уровни логирования

```csharp
Log.Information("Starting application on {Url}", url);
Log.Warning(ex, "Failed to connect to RabbitMQ");
Log.Error(ex, "Database migration failed");
Log.Debug("Cache hit for character {CharacterId}", characterId);
```

### Health Check

```http
GET /api/dev/health

Response:
{
  "database": "Healthy",
  "eventStore": "Healthy",
  "messageBus": "Healthy",
  "lockManager": "Healthy"
}
```

### OpenTelemetry

Приложение поддерживает **OpenTelemetry** для трейсинга.

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddAspNetCoreInstrumentation()
        .AddSqlClientInstrumentation()
        .AddOtlpExporter());
```

---

## 🐳 Docker

### Dockerfile

Образ строится на основе `mcr.microsoft.com/dotnet/aspnet:8.0-alpine`.

```bash
# Сборка образа
docker build -t dnd-game:latest .

# Запуск контейнера
docker run -p 5000:5000 \
  -e TOKEN_SECRET=your-secret \
  -e CONNECTIONSTRINGS_DEFAULTCONNECTION=... \
  dnd-game:latest
```

### Docker Compose

```yaml
version: '3.8'
services:
  app:
    build: .
    ports:
      - "5000:5000"
    environment:
      - TOKEN_SECRET=your-secret
      - CONNECTIONSTRINGS_DEFAULTCONNECTION=Host=postgres;...
      - CONNECTIONSTRINGS_REDIS=redis:6379
    depends_on:
      - postgres
      - redis
      - rabbitmq

  postgres:
    image: postgres:15-alpine
    ports:
      - "5432:5432"
    environment:
      - POSTGRES_PASSWORD=password
      - POSTGRES_DB=dnd_game

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"

  rabbitmq:
    image: rabbitmq:3.12-management-alpine
    ports:
      - "5672:5672"
      - "15672:15672"
```

```bash
# Запуск всех сервисов
docker-compose up -d

# Логи
docker-compose logs -f app

# Остановка
docker-compose down
```

---

## 📚 Дополнительная информация

### Локализация

Приложение поддерживает локализацию на русском и английском языках.

```csharp
// Загрузка локали
var localeManager = app.Services.GetRequiredService<LocaleManager>();
await localeManager.LoadLocaleAsync("ru");
await localeManager.LoadLocaleAsync("en");

// Файлы локализации в Resources/Locales/
```

### Миграции БД

Приложение использует **dbup** для управления миграциями.

```bash
# SQL-скрипты в папке migrations/
# Выполняются автоматически при запуске приложения
```

### FluentValidation

Все входные данные валидируются перед обработкой.

```csharp
// Пример валидатора
public class CreateCharacterValidator : AbstractValidator<CreateCharacterRequest>
{
    public CreateCharacterValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);
        
        RuleFor(x => x.MaxHitPoints)
            .GreaterThan(0);
    }
}
```

---

## 🤝 Контрибьютинг

Contributions приветствуются! Пожалуйста, откройте issue или pull request.

---

## 📄 Лицензия

Проект распространяется под лицензией **MIT License**.

Подробнее см. файл [LICENSE](LICENSE).

---

## 🔗 Полезные ссылки

- **Swagger UI**: http://localhost:5000/swagger
- **RabbitMQ Management**: http://localhost:15672 (guest/guest)
- **PostgreSQL**: localhost:5432
- **Redis**: localhost:6379

---

## 📞 Контакты

**Разработчик**: [mvchemezov1](https://github.com/mvchemezov1)

**Repository**: [dnd-game](https://github.com/mvchemezov1/dnd-game)

---

**Последнее обновление**: сентябрь 2026

*Спасибо за использование DnD Game! 🎲*
