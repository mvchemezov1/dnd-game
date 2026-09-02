  
     
           
              
                    
 # Аудит D&D Game: рекомендации по UX с позиции тестировщика и DM

Проанализировал код, архитектуру и UI. Ниже разбивка по слоям с конкретными проблемами и решениями. Приоритеты расставлены от критичных (блокирующих игру) до улучшений.

---

## 🔴 1. БЭКЕНД (C# / .NET 8)

### 1.1. D&D-логика: критические пробелы

| Проблема | Почему это важно | Решение |
|----------|------------------|---------|
| **Нет автоматического расчёта модификаторов характеристик** | Игрок вручную считает `(14-10)/2 = +2`. В D&D 5e это базовая механика. | Добавить `ModifierCalculator` в домен и экспонировать через API. Фронтенд должен показывать модификатор рядом со score. |
| **Заклинания, расы, классы — голые строки** | Нет валидации, что `SpellId = "fireball"` существует в SRD. Нет данных о компонентах, дистанции, уроне. | Интегрировать **SRD 5.1** (JSON с ~300 заклинаниями). Создать `SrdSpellRepository`, `SrdClassRepository`. Сейчас `MagicRules.RequiresConcentration(spellId)` — это хардкод, а не данные. |
| **Нет Advantage/Disadvantage** | Ключевая механика 5e отсутствует в домене. | Добавить в `SavingThrowAttempted` и `AbilityCheck` флаги `HasAdvantage`, `HasDisadvantage`. Реализовать бросок 2d20 take higher/lower. |
| **Нет автоматического расчёта AC от брони** | Игрок вручную выставляет AC. Должно считаться из `Equipment` + `Dexterity` modifier. | В `CharacterAggregate` при экипировке автоматически пересчитывать AC по правилам 5e (лёгкая/средняя/тяжёлая броня). |
| **Нет автоматического отслеживания длительности** | `ConditionApplied` принимает `durationRounds`, но нет таймера, который бы снимал состояние через N раундов. | Добавить `CombatAggregate` отслеживание: при `EndRound` проверять `Condition.ExpiresAtRound` и эмитить `ConditionRemoved`. |
| **Нет поддержки мультиклассирования** | `Class` — одна строка. В 5e мультикласс — стандарт. | `Class` → `List<CharacterClassLevel>`. Пересчитывать `ProficiencyBonus` от суммарного уровня. |
| **Нет системы вдохновения (Inspiration)** | Механика DM-награды отсутствует. | Добавить `InspirationGranted` / `InspirationSpent` события. |
| **Нет автоматического броска хитов при лечении** | `Heal` принимает фиксированное число. `Cure Wounds` требует бросок `Xd8 + mod`. | Разделить: `HealFixed` и `HealRolled` (с параметрами костей). Или добавить `DiceRoller` сервис. |
| **Нет системы опыта за уровни (XP Table)** | `LevelUp` принимает любой `newLevel` без проверки XP. | Добавить `ExperienceTable` (domain service) с проверкой `CurrentXP >= RequiredXP`. |
| **Нет exhaustion (истощения)** | Шесть уровней истощения — ключевая механика выживания. | Добавить `ExhaustionLevel` (0-6) с соответствующими штрафами на скорость, хиты, спасброски. |

### 1.2. API и валидация

| Проблема | Решение |
|----------|---------|
| **Ручная валидация в контроллерах** (`if (id == Guid.Empty)`) | Перенести в `FluentValidation`. Сейчас валидаторы есть, но не покрывают 80% endpoints. |
| **Нет `ProblemDetails` с деталями** | `BadRequest(new { error = "..." })` — нестандартизировано. Использовать `ValidationProblemDetails`. |
| **Нет пагинации** | `GET /api/characters` возвращает всех. Добавить `Page`, `PageSize`, `TotalCount`. |
| **Нет сортировки и фильтрации** | `SearchCharacters` есть, но нет по `createdAt`, `level`, `campaignId`. |
| **Нет HATEOAS / связей** | В ответе персонажа нет ссылки на `/api/characters/{id}/inventory`. |
| **Нет `ETag` / `If-Match` для оптимистичной блокировки** | В Event Sourcing конфликты версий есть, но клиент не знает текущей версии. Добавить `ETag: "v12"` в заголовки. |

### 1.3. Боевая система

| Проблема | Решение |
|----------|---------|
| **Нет автоматического расчёта попадания** | `TakeStandardAction` принимает `ActionType` строкой. Нужен `AttackRoll` с `d20 + prof + mod` против `target.AC`. |
| **Нет критических попаданий/промахов** | Natural 20 — удвоенный урон. Natural 1 — автопромах. |
| **Нет зон поражения (AoE)** | `Fireball` должен наносить урон всем в радиусе 20 футов от точки. Нет гео-запросов. |
| **Нет покрова (Cover)** | Half/Three-Quarters/Total Cover влияет на AC и спасброски. |
| **Нет высоты (elevation)** | `Position` — только X,Y. Для полёта и дальнобойности нужна Z. |

### 1.4. Безопасность и надёжность

| Проблема | Решение |
|----------|---------|
| **JWT `ValidateIssuer = false`, `ValidateAudience = false`** | Включить валидацию Issuer/Audience. Сейчас токен от одного сервера принимается другим. |
| **Нет rate limiting на уровне middleware** | Есть `IRateLimiter`, но не применяется глобально. Добавить `RateLimitingMiddleware`. |
| **Нет idempotency на командах** | `IIdempotentCommand` есть, но фронтенд не генерирует `IdempotencyKey`. Добавить middleware, который читает `Idempotency-Key` из заголовка. |
| **Нет audit log для админ-операций** | `SetGold` админом не логируется отдельно. Добавить `AuditLog` таблицу с `ChangedBy`, `ChangedAt`, `OldValue`, `NewValue`. |
| **Нет input sanitization** | `ActionData: object?` в combat — потенциальная дыра. |

### 1.5. Event Sourcing

| Проблема | Решение |
|----------|---------|
| **Нет версионирования событий** | Если изменить `CharacterCreated` (добавить поле), старые события не десериализуются. | Ввести `EventSchemaVersion` в метаданные. Добавить `IEventUpcaster`. |
| **Нет Event Schema Registry** | Невозможно отследить, какие версии событий в проде. | Avro/JSON Schema registry или хотя бы `events_schema` таблица. |
| **Снапшоты — бинарные (BYTEA)** | Не читаемы для отладки. Перейти на JSONB снапшоты. |
| **Нет архивации старых событий** | Таблица `events` будет бесконечно расти. Добавить `events_archive` партиционирование по `timestamp`. |

---

## 🔴 2. ФРОНТЕНД (Vanilla JS)

### 2.1. Архитектура и DX

| Проблема | Решение |
|----------|---------|
| **Vanilla JS без модульной системы** | 15+ файлов в глобальной области. Конфликты имён, нет tree-shaking. | **Мигрировать на Vite + TypeScript + Vue/React/Svelte**. Это критично для поддержки. |
| **Нет type safety** | API ответы — `any`. Ошибки типов в runtime. | Сгенерировать клиент из OpenAPI (swagger) — `openapi-typescript` + `fetch`. |
| **Нет тестов** | Ни unit, ни e2e. | Playwright для критических путей: создание персонажа → бой → отдых. |
| **Нет PWA** | Игроки часто используют планшеты. Нет offline mode, нет push-уведомлений. | Добавить `manifest.json`, Service Worker, кэширование статики. |

### 2.2. UX для игрока

| Проблема | Решение |
|----------|---------|
| **Нет встроенного роллера костей** | Игрок вводит `rollResult` вручную в `<input type="number">`. Это **убивает атмосферу**. | Интегрировать 3D dice roller (например, `fantasy-dice-3d` или `dice-box`). Результат отправлять на сервер с подписью (anti-cheat). |
| **Нет листа персонажа (Character Sheet)** | Сейчас — разрозненные карточки. Нужен классический D&D лист: характеристики, навыки, спасброски, заклинания, инвентарь на одном экране. | Сверстать адаптивный лист. Использовать `localStorage` для draft-изменений. |
| **Нет визуального трекера хитов** | `UI.hpBar` — простой div. Нужен интерактивный: клик = урон, shift+клик = лечение, временные хиты — отдельным слоем. | Компонент с `-/+` кнопками, анимацией, цветовой индикацией (зелёный → жёлтый → красный). |
| **Нет drag-and-drop экипировки** | `EquipItem` — выбор из списка. Нужна сетка слотов: голова, плечи, грудь, руки, ноги, кольца ×2. | Реализовать `inventory-grid` с drag-and-drop (HTML5 DnD API). |
| **Нет визуальной карты боя** | Бой — текстовый список. Нужна тактическая сетка 1" = 5 футов с токенами. | Интегрировать `grid-engine` или `phaser.js` для тактической карты. |
| **Нет журнала действий (Combat Log)** | Нет истории: «Гаррус нанёс 12 урона огнём, Дракон провалил спасбросок». | Добавить `CombatLog` панель с фильтрами по типу (урон, заклинания, передвижение). |
| **Нет быстрых действий** | Каждое действие — 3 клика + ввод GUID. | Добавить hotbar (как в MMO): F1 — атака, F2 — Dash, F3 — заклинание. |
| **Нет тултипов с правилами** | «Состояние Stunned» — просто текст. Нужен тултип с описанием из SRD. | Создать `srd-tooltips.json` и рендерить по hover. |
| **Нет темной/светлой темы** | Только тёмная. Добавить переключатель. | CSS variables + `prefers-color-scheme`. |

### 2.3. UX для DM

| Проблема | Решение |
|----------|---------|
| **Нет массового управления** | Добавлять 10 гоблинов — 10 API-вызовов. | «Spawn Encounter» — загрузить шаблон (5× Goblin, 1× Goblin Boss). |
| **Нет тумана войны (Fog of War)** | Все игроки видят всю карту. | Реализовать visibility layer: DM видит всё, игроки — только `Line of Sight`. |
| **Нет скрытых бросков DM** | DM вводит `rollResult` — видно всем через WS. | Добавить `isSecretRoll` флаг. Результат видит только DM. |
| **Нет таймера хода** | Игроки затягивают. | Добавить `TurnTimer` (например, 2 минуты) с визуальным обратным отсчётом. |

---

## 🔴 3. БАЗА ДАННЫХ (PostgreSQL)

### 3.1. Производительность

| Проблема | Решение |
|----------|---------|
| **Нет партиционирования `events`** | Таблица линейно растёт. Запросы по `aggregate_id` сканируют всё. | `PARTITION BY RANGE (timestamp)` или `PARTITION BY HASH (aggregate_id)`. |
| **Индекс `idx_events_aggregate_id` — не покрывающий** | Частый запрос: `aggregate_id + version ORDER BY version`. | Создать `INCLUDE (version, event_type, data)` или отдельный индекс `(aggregate_id, version)`. |
| **Нет GIN-индекса на `data` JSONB** | Поиск событий по вложенным полям (например, `data->>'Condition' = 'Stunned'`) — seq scan. | `CREATE INDEX idx_events_data_gin ON events USING GIN (data jsonb_path_ops)`. |
| **Нет гео-индекса для позиций** | `PositionX`, `PositionY` хранятся в JSONB `events.data`. Поиск «все в радиусе 30 футов» — невозможен. | Вынести позицию в отдельную таблицу `character_positions` с `POINT` и `GiST` индексом. |
| **Нет индекса на `outbox_events`** | Outbox processor делает `SELECT * FROM outbox_events WHERE processed = false`. | `CREATE INDEX idx_outbox_unprocessed ON outbox_events(processed, created_at) WHERE processed = false`. |

### 3.2. Структура

| Проблема | Решение |
|----------|---------|
| **Нет таблицы `srd_spells`** | Заклинания — строки. Нужна нормализованная таблица с `id`, `name`, `level`, `school`, `components`, `range`, `duration`, `description`. |
| **Нет таблицы `srd_conditions`** | Состояния — строки. Нужна таблица с `name`, `description`, `effects_json` (что блокирует, какие штрафы). |
| **Нет таблицы `srd_items`** | Предметы — строки. Нужна таблица с `type` (weapon/armor/potion), `rarity`, `properties`, `damage_dice`, `ac_formula`. |
| **Нет таблицы `combat_maps`** | Бой привязан к абстрактному `CombatId`. Нужна связь с `map_id` (сетка, размер клетки). |
| **Нет аудит-логирования на уровне БД** | `users` изменяется напрямую. | Триггеры или `pg_audit` для отслеживания DDL/DML. |

### 3.3. Резервирование

| Проблема | Решение |
|----------|---------|
| **Нет репликации** | Чтение проекций идёт на мастер. | Настроить `hot_standby` + `pgBouncer` для read-only запросов к реплике. |
| **Нет WAL-архивации** | PITR (Point-in-Time Recovery) невозможен. | `archive_mode = on`, `archive_command = 'cp %p /backups/wal/%f'`. |

---

## 🔴 4. СТОРОННИЕ СЕРВИСЫ

### 4.1. Redis

| Проблема | Решение |
|----------|---------|
| **Нет graceful degradation** | Если Redis недоступен, `RedisCacheProvider` падает. | `NullCacheProvider` — fallback, но он не используется автоматически. Добавить `CacheProviderSelector` с circuit breaker. |
| **Нет кэширования SRD-данных** | `srd_spells` часто читаются. | Кэшировать в Redis с TTL 24ч. |
| **Нет distributed locking для саг** | `InMemoryLockManager` — только внутри одного инстанса. Для мультинстанса нужен `RedLock` через Redis. |

### 4.2. RabbitMQ

| Проблема | Решение |
|----------|---------|
| **Нет dead letter monitoring** | DLX есть, но нет алертов, когда сообщения попадают туда. | Интегрировать с Prometheus: `rabbitmq_queue_messages{queue="dnd.dead_letter_queue"}`. |
| **Нет message TTL** | События боя могут быть актуальны только 30 секунд. | Добавить `x-message-ttl` для `CombatEvent` очередей. |
| **Нет приоритетов** | `CombatActionTaken` важнее `CharacterMoved`. | `x-max-priority` для боевых событий. |

### 4.3. SMTP / Email

| Проблема | Решение |
|----------|---------|
| **Нет fallback email provider** | Gmail может заблокировать. | Интегрировать SendGrid / Mailgun API с retry. |
| **Нет HTML-шаблонов** | Письмо о сбросе пароля — plain text. | Шаблонизатор (Handlebars/Razor) для красивых писем. |

### 4.4. Мониторинг

| Проблема | Решение |
|----------|---------|
| **Нет алертов на бизнес-метрики** | `dnd.eventstore.concurrency_conflict` — метрика есть, но нет алерта. | Настроить Alertmanager: `rate(dnd_eventstore_concurrency_conflict[5m]) > 0.1`. |
| **Нет RUM (Real User Monitoring)** | Неизвестно, как долго рендерится бой на планшете. | Добавить `web-vitals` (LCP, FID, CLS) на фронтенд. Отправлять в Prometheus/Grafana. |
| **Нет tracing между WS и HTTP** | WebSocket запрос и REST-ответ — разные trace ID. | Пробрасывать `traceparent` через WebSocket messages. |

### 4.5. AI / Внешние API

| Проблема | Решение |
|----------|---------|
| **Monster AI — слишком простой** | Только `attack` / `flee` / `patrol`. Нет использования заклинаний, ловушек, тактики группы. | Интегрировать LLM (OpenAI GPT-4o / Claude) для «DM AI». Промпт: «Ты мастер. У монстра есть Fireball, 3 врага в радиусе 20 футов. Что делать?» |
| **Нет интеграции с D&D Beyond** | Игроки хранят персонажей там. | API импорта (если доступен) или парсинг `character.json`. |
| **Нет голосового чата** | Для онлайн-игры критичен голос. | Интегрировать Discord SDK или WebRTC (Daily.co). |

---

## 📋 План миграции (по приоритетам)

### Спринт 1 (2 недели): Критично для игры
1. Добавить **3D dice roller** на фронтенд
2. Реализовать **классический character sheet** (HTML/CSS)
3. Добавить **Advantage/Disadvantage** в домен
4. Интегрировать **SRD 5.1** (заклинания, состояния, классы)
5. Исправить **JWT валидацию** (Issuer/Audience)

### Спринт 2 (2 недели): Бой
1. Тактическая **карта сетки** (grid map)
2. **Combat Log** с фильтрами
3. Автоматический **расчёт AC** от экипировки
4. **Таймер хода** для DM
5. **Fog of War**

### Спринт 3 (2 недели): Архитектура
1. **Event versioning** + upcasters
2. **Партиционирование** `events`
3. **RedLock** для distributed саг
4. **E2E тесты** (Playwright)
5. **PWA** (offline mode)

### Спринт 4 (2 недели): Полировка
1. **DM AI** через LLM
2. **Голосовой чат** интеграция
3. **Мультиклассирование**
4. **Exhaustion** механика
5. **RUM** мониторинг

---

1)

## 1. БЭКЕНД

### 1.1. Добавить `AbilityModifiers` в `CharacterDto`

**Файл:** `application/projections/character_dto.cs`

```csharp
public record CharacterDto
{
    // ... существующие поля ...

    /// <summary>Значения характеристик (Сила, Ловкость и т.д.).</summary>
    public Dictionary<string, int> AbilityScores { get; init; } = [];

    /// <summary>Модификаторы характеристик (автоматически вычисляются).</summary>
    public Dictionary<string, int> AbilityModifiers { get; init; } = [];

    // ... остальные поля ...
}
```

### 1.2. Вычислять модификаторы в проекции

**Файл:** `application/projections/character_projection.cs`

При создании персонажа инициализируем модификаторы:

```csharp
public void Apply(CharacterCreated e)
{
    lock (_syncRoot)
    {
        var dto = new CharacterDto
        {
            Id = e.CharacterId,
            Name = e.Name,
            MaxHitPoints = e.MaxHitPoints,
            HitPoints = e.MaxHitPoints,
            HitDiceRemaining = new Dictionary<int, int> { { 8, 1 } },
            MaxHitDice = new Dictionary<int, int> { { 8, 1 } },
            // Инициализируем характеристики и модификаторы
            AbilityScores = new Dictionary<string, int>
            {
                {"Strength", 10}, {"Dexterity", 10}, {"Constitution", 10},
                {"Intelligence", 10}, {"Wisdom", 10}, {"Charisma", 10}
            },
            AbilityModifiers = new Dictionary<string, int>
            {
                {"Strength", 0}, {"Dexterity", 0}, {"Constitution", 0},
                {"Intelligence", 0}, {"Wisdom", 0}, {"Charisma", 0}
            }
        };
        _state[e.CharacterId] = dto;
    }
    InvalidateCache(e.CharacterId);
}
```

При изменении характеристики пересчитываем модификатор:

```csharp
public void Apply(AbilityScoreSet e)
{
    ArgumentNullException.ThrowIfNull(e);
    lock (_syncRoot)
    {
        if (_state.TryGetValue(e.CharacterId, out var dto))
        {
            var scores = new Dictionary<string, int>(dto.AbilityScores) { [e.Ability] = e.Score };
            var modifiers = new Dictionary<string, int>(dto.AbilityModifiers);
            modifiers[e.Ability] = ModifierCalculator.Calculate(e.Score);
            
            _state[e.CharacterId] = dto with 
            { 
                AbilityScores = scores, 
                AbilityModifiers = modifiers 
            };
        }
    }
    InvalidateCache(e.CharacterId);
}
```

> **Важно:** `ModifierCalculator` уже импортирован в проект (`using dnd_game.domain.value_objects;`), так что дополнительных `using` не требуется.

### 1.3. (Опционально) Добавить вычисляемое свойство в агрегат

**Файл:** `domain/aggregates/character_aggregate.cs`

Это полезно, если другие доменные сервисы (например, расчёт AC или инициативы) будут обращаться к модификаторам:

```csharp
public class CharacterAggregate : AggregateRoot
{
    // ... существующие поля ...

    public Dictionary<string, int> AbilityScores { get; private set; } = new()
    {
        {"Strength", 10}, {"Dexterity", 10}, {"Constitution", 10},
        {"Intelligence", 10}, {"Wisdom", 10}, {"Charisma", 10}
    };

    /// <summary>Вычисленные модификаторы характеристик (только для чтения).</summary>
    public Dictionary<string, int> AbilityModifiers => AbilityScores.ToDictionary(
        kvp => kvp.Key,
        kvp => ModifierCalculator.Calculate(kvp.Value)
    );

    // ... остальной код ...
}
```

---

## 2. ФРОНТЕНД

### 2.1. Убрать дублирование расчёта в `sheet.js`

**Файл:** `wwwroot/js/views/sheet.js`

**Вкладка «Характеристики»** — заменить ручной расчёт на данные из API:

```javascript
// Было:
const score = scores[a] ?? scores[a.toLowerCase()] ?? 10;
const mod = Math.floor((score - 10) / 2);

// Стало:
const score = scores[a] ?? scores[a.toLowerCase()] ?? 10;
const mod = char.abilityModifiers?.[a] 
    ?? char.abilityModifiers?.[a.toLowerCase()] 
    ?? Math.floor((score - 10) / 2); // fallback если API ещё не обновлён
```

### 2.2. Добавить отображение модификаторов на карточках персонажей

**Файл:** `wwwroot/js/views/characters.js`

В `cardHtml` добавить строку с ключевыми модификаторами (Сила, Ловкость, Телосложение — самые часто используемые в бою):

```javascript
function cardHtml(c) {
    const isDead = c.isDead === true || c.isAlive === false;
    const level = c.level ?? '?';
    const race = c.race || '—';
    const className = c.class || '';
    const armorClass = c.armorClass ?? '—';
    
    // Получаем модификаторы из API (или считаем fallback)
    const mods = c.abilityModifiers || {};
    const strMod = mods.Strength ?? 0;
    const dexMod = mods.Dexterity ?? 0;
    const conMod = mods.Constitution ?? 0;

    return `
        <div class="char-card" data-id="${UI.esc(c.id)}" role="button" tabindex="0">
            <h3>${UI.esc(c.name)} ${isDead ? '💀' : ''}</h3>
            <div class="small muted">Ур. ${level} · ${UI.esc(race)} ${UI.esc(className)}</div>
            ${UI.hpBar(c.hitPoints ?? 0, c.maxHitPoints ?? 0)}
            <div class="row small muted" style="margin-top:8px;gap:6px">
                ${UI.pill('AC ' + armorClass)}
                ${UI.pill('СИЛ ' + (strMod >= 0 ? '+' : '') + strMod)}
                ${UI.pill('ЛОВ ' + (dexMod >= 0 ? '+' : '') + dexMod)}
                ${UI.pill('ТЕЛ ' + (conMod >= 0 ? '+' : '') + conMod)}
            </div>
        </div>`;
}
```

### 2.3. Автоматический расчёт бонусов навыков

**Файл:** `wwwroot/js/views/sheet.js`

**Вкладка «Навыки и спасброски»** — сейчас там просто список владений без итоговых бонусов. Добавить отображение:

```javascript
// Сопоставление навыков → характеристики (SRD 5e)
const SKILL_ABILITIES = {
    Acrobatics: 'Dexterity', AnimalHandling: 'Wisdom', Arcana: 'Intelligence',
    Athletics: 'Strength', Deception: 'Charisma', History: 'Intelligence',
    Insight: 'Wisdom', Intimidation: 'Charisma', Investigation: 'Intelligence',
    Medicine: 'Wisdom', Nature: 'Intelligence', Perception: 'Wisdom',
    Performance: 'Charisma', Persuasion: 'Charisma', Religion: 'Intelligence',
    SleightOfHand: 'Dexterity', Stealth: 'Dexterity', Survival: 'Wisdom'
};

function getSkillTotal(skill) {
    const ability = SKILL_ABILITIES[skill] || 'Strength';
    const abilityMod = char.abilityModifiers?.[ability] ?? 0;
    const profBonus = char.proficiencyBonus ?? 2;
    const isProficient = Array.isArray(char.skillProficiencies) && char.skillProficiencies.includes(skill);
    return abilityMod + (isProficient ? profBonus : 0);
}

// В drawSkills при рендере списка:
${skillProf.map(s => {
    const total = getSkillTotal(s);
    const sign = total >= 0 ? '+' : '';
    return `<span class="tag">${UI.esc(s)} <strong>${sign}${total}</strong><button data-action="rm-skill" data-name="${UI.esc(s)}">✕</button></span>`;
}).join('') || '<span class="muted small">Нет владений</span>'}
```

Аналогично для спасбросков:

```javascript
function getSaveTotal(ability) {
    const abilityMod = char.abilityModifiers?.[ability] ?? 0;
    const profBonus = char.proficiencyBonus ?? 2;
    const isProficient = Array.isArray(char.savingThrowProficiencies) && char.savingThrowProficiencies.includes(ability);
    return abilityMod + (isProficient ? profBonus : 0);
}
```

---

## 3. БАЗА ДАННЫХ

Изменения **не требуются**. `AbilityModifiers` — это **вычисляемое поле** (derived data), которое строится проекцией в памяти из событий `AbilityScoreSet`. В Event Sourcing нет необходимости хранить его отдельно — оно восстанавливается при ребилде проекции.

Если в будущем появится SQL-реплика (read model в PostgreSQL), тогда добавить вычисляемый столбец:

```sql
ALTER TABLE character_read_models 
ADD COLUMN ability_modifiers JSONB GENERATED ALWAYS AS (
    jsonb_build_object(
        'Strength', (ability_scores->>'Strength')::int / 2 - 5,
        'Dexterity', (ability_scores->>'Dexterity')::int / 2 - 5,
        -- ...
    )
) STORED;
```

---

## 4. СТОРОННИЕ СЕРВИСЫ

Изменения **не требуются**. Это чисто внутренняя доменная логика.

---

## Итоговый чек-лист для разработчика

| Шаг | Файл | Действие |
|-----|------|----------|
| 1 | `character_dto.cs` | Добавить `Dictionary<string, int> AbilityModifiers` |
| 2 | `character_projection.cs` | В `Apply(CharacterCreated)` инициализировать нулями. В `Apply(AbilityScoreSet)` пересчитывать через `ModifierCalculator.Calculate` |
| 3 | `character_aggregate.cs` | (Опц.) Добавить вычисляемое свойство `AbilityModifiers` |
| 4 | `characters.js` | В `cardHtml` добавить отображение СИЛ/ЛОВ/ТЕЛ модификаторов |
| 5 | `sheet.js` | В `drawAbilities` использовать `char.abilityModifiers` вместо ручного `Math.floor` |
| 6 | `sheet.js` | В `drawSkills` добавить `SKILL_ABILITIES` маппинг и отображать итоговый бонус каждого навыка |
| 7 | `sheet.js` | В `drawSkills` аналогично отображать итоговый бонус спасбросков |

После этих изменений игрок видит модификатор **сразу** при открытии листа персонажа, не считает в уме `(14-10)/2`, а DM видит ключевые модификаторы в списке персонажей без необходимости заходить в каждый лист.
