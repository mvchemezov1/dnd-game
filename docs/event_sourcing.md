<!-- docs/event_sourcing.md -->
# Event Sourcing в Dnd_game

Этот документ объясняет, как в проекте устроено хранение состояния через события: что такое агрегат,
как события записываются и читаются, как работают снапшоты, согласованность версий, саги и проекции.
Если вы хотите добавить новую доменную операцию — начните отсюда.

---

## 1. Идея

Вместо того чтобы хранить в базе данных текущее состояние персонажа/боя/кампании (одна строка = один
объект), система хранит **последовательность неизменяемых событий**: `CharacterCreated`, `DamageDealt`,
`LevelUpApplied`, `CombatStarted`, `InitiativeRolled` и т. д. Текущее состояние — это результат
последовательного применения всех событий агрегата, от первого до последнего.

Преимущества, которые это даёт проекту:

- **Полный аудит** — известно не только "сколько HP сейчас", но и "почему" (какой урон, от кого, когда).
- **Time travel / отладка** — можно восстановить состояние на любой момент, просто остановившись на
  нужной версии.
- **Гибкие проекции** — из одного и того же потока событий можно строить разные read-модели
  (`CharacterProjection`, `CombatProjection`, `CampaignProjection`) без изменения источника истины.
- **Undo/Redo для Мастера** — раз каждое действие явно, его проще отменить или повторить
  (см. `infrastructure/undo/undo_command.cs`, `presentation/dm_tools/undo_manager.cs`).

---

## 2. Агрегаты (`domain/aggregates`)

Все агрегаты наследуются от `AggregateRoot` (`domain/aggregates/base.cs`), который даёт:

| Член | Назначение |
|---|---|
| `Id` | идентификатор агрегата (`CharacterId`, `CombatId`, `CampaignId`) |
| `Version` / `OriginalVersion` | текущая версия и версия, с которой агрегат был загружен — используется для optimistic concurrency |
| `ApplyChange(event)` | **главный метод**: применяет событие к состоянию (`ApplyEvent`), проверяет инварианты (`EnsureInvariants`), кладёт событие в список несохранённых и увеличивает `Version` |
| `ApplyEvent(event)` (abstract) | переключатель `switch (event) { case FooEvent e: ...}`, который каждый конкретный агрегат реализует сам — здесь и только здесь меняются приватные поля состояния |
| `LoadFromHistory(events)` | восстанавливает агрегат, прогоняя все прошлые события через `ApplyEvent` (без повторной записи) |
| `GetUncommittedEvents()` / `ClearUncommittedEvents()` | события, которые ещё не сохранены в Event Store — так `PostgresEventStore` понимает, что именно писать |
| `EnsureInvariants()` | точка расширения для правил D&D (HP не может быть отрицательным, инициатива 1–20+модификатор и т. п.) |

В проекте три агрегата:

- `CharacterAggregate` — персонаж/монстр/NPC: HP, характеристики, инвентарь, заклинания, состояния,
  спасброски смерти, золото, опыт/уровень.
- `CombatAggregate` — боевая сцена: участники, инициатива, раунды, ходы, действия, урон/лечение,
  состояния в бою, концентрация.
- `CampaignAggregate` — кампания: время суток, погода, обнаруженные регионы, глобальные флаги, квесты.

Правило, которое **нельзя нарушать**: команда → обработчик команды загружает агрегат → вызывает на нём
доменный метод → метод вызывает `ApplyChange(new SomeEvent(...))` → обработчик сохраняет агрегат через
`IEventStore.SaveWithMetadata`. Обработчики никогда не пишут в БД напрямую и не меняют поля агрегата
в обход `ApplyChange`.

---

## 3. Команды и обработчики (`domain/commands`, `application/command_handlers`)

Команда — это неизменяемый `record`, описывающий намерение ("нанести урон", "начать бой",
"выучить заклинание"). Она **не** содержит логику — только данные. Обработчик (`*_handler.cs` в
`application/command_handlers`) получает команду через `ICommandBus`, загружает нужный агрегат через
`IEventStore.Load<T>`, вызывает доменный метод и сохраняет результат.

```
HTTP/WebSocket -> ICommandBus.SendAsync(command, context)
                       |
                       v
              CommandHandler<TCommand>
                       |  IEventStore.Load<T>(id)
                       v
                 Aggregate.DoSomething(...)
                       |  ApplyChange(event) x N
                       v
              IEventStore.SaveWithMetadata(aggregate, metadata)
```

`CommandContext` (см. `presentation/api/rest_api.cs`, `GameControllerBase.CreateContext`) переносит
`UserId`, `GameSessionId` и `CancellationToken` из HTTP/WebSocket-запроса в обработчик — это то, что
попадает в `EventMetadata` каждого сохранённого события (кто и в какой игровой сессии это сделал).

---

## 4. Хранилище событий (`infrastructure/event_store`)

`IEventStore` — контракт с двумя реализациями:

- `PostgresEventStore` — основная реализация, использует таблицы `events` и `snapshots` в PostgreSQL
  (см. `infrastructure/migrations/001_Initial.sql`, `002_AddCampaignTable.sql`, `003_AddIndexes.sql`).
- `InMemoryRepositories` (`infrastructure/common`) — реализация в памяти для юнит-тестов и быстрой
  локальной разработки без базы.

### Таблица `events`

Каждая строка — одно событие: `event_id` (уникален), `aggregate_id`, `aggregate_type`, `version`
(строго возрастает для данного агрегата, `UNIQUE(aggregate_id, version)` защищает от гонок), `event_type`
(полное CLR-имя класса события — используется для десериализации), `data` (JSONB — сериализованное тело
события), `user_id`, `session_id`, `custom_headers` (JSONB, опционально), `timestamp`.

### Запись (`SaveWithMetadata`)

1. `IConsistencyManager.EnforceConsistencyAsync` проверяет версию, блокировки и инварианты
   (см. раздел 5 ниже).
2. Все несохранённые события агрегата пишутся в одной транзакции, версия у каждого строго увеличивается
   на 1; уникальный индекс `(aggregate_id, version)` защищает от параллельной записи в одну и ту же
   версию — конфликт превращается в исключение `PostgresException` с кодом `23505`, которое
   транслируется в `StateConflictException`.
3. При конфликте версии выполняется **retry с экспоненциальной задержкой** (до 3 попыток): агрегат
   перезагружается, несохранённые события переигрываются поверх свежей версии, попытка сохранения
   повторяется.
4. После успешной записи вызывается `ISnapshotStore.ShouldCreateSnapshotAsync` — если да, создаётся
   снапшот (см. ниже).

### Чтение (`Load<T>`)

1. Берётся последний снапшот (`ISnapshotStore.GetLatestSnapshotAsync`), если он есть.
2. Из таблицы `events` читаются события с версией больше версии снапшота (`FromVersion`).
3. Если снапшота нет — агрегат создаётся с нуля и все события проигрываются через `LoadFromHistory`.
4. Если снапшот есть — агрегат восстанавливается из него (`SnapshotStore.RestoreAggregateFromSnapshot`),
   а недостающий хвост событий применяется поверх через `ApplyChange`.

### Снапшоты (`infrastructure/event_store/snapshot_store.cs`)

Снапшот — сериализованное состояние агрегата на определённой версии. Нужен, чтобы не переигрывать
тысячи событий у долгоживущих агрегатов (например, персонаж, который прожил всю кампанию). Решение о
том, создавать ли снапшот, принимает `ISnapshotStore` (обычно по счётчику версий, например каждые N
событий).

---

## 5. Согласованность (`infrastructure/event_store/consistency_manager.cs`)

`ConsistencyManager` — слой между обработчиком команды и физической записью, отвечающий за:

- **Optimistic concurrency** — сверяет `aggregate.OriginalVersion` с текущей версией в хранилище;
  расхождение -> `ConsistencyResult.VersionConflict`.
- **Распределённые блокировки** (`infrastructure/coordination/distributed_lock.cs`,
  `in_memory_lock_manager.cs`) — на время записи блокирует агрегат, чтобы исключить гонки между
  параллельными запросами к одному и тому же персонажу/бою.
- **Проверку инвариантов и глобальных правил** — `ConsistencyResult.InvariantViolation` /
  `GlobalRuleViolation` для случаев, когда состояние агрегата само по себе валидно, но нарушает
  более широкое правило игры (например, два одновременных боя с одним и тем же персонажем).

Каждый `ConsistencyResult`, отличный от `Success`, превращается в конкретное доменное исключение
(`StateConflictException`, `RuleViolation`, `InvalidOperationException`) и всплывает до
`GlobalExceptionHandler`, который отвечает клиенту понятным HTTP-статусом/сообщением.

---

## 6. Проекции (`application/projections`)

Проекции — это read-модели, которые CQRS-часть системы строит из событий отдельно от записи.
`CharacterProjection`, `CombatProjection`, `CampaignProjection` подписываются на события через шину
(`application/event_handlers`) и поддерживают быстрые для чтения представления (`CharacterDto`,
`CombatDto` и т. д.), которые отдают REST API и WebSocket. Материализованные представления
(`application/projections/materialized_views/combat_status.cs`, `player_overview.cs`) — более "толстые"
агрегированные вьюхи для дашбордов (например, `wwwroot/dev/dashboard.html`).

Важно: проекции **никогда не являются источником истины** — если их потерять или испортить, их можно
полностью перестроить, заново прогнав все события из Event Store с начала.

---

## 7. Обработчики событий (`application/event_handlers`)

После того как событие сохранено, `IEventBus` публикует его подписчикам:

| Обработчик | Что делает |
|---|---|
| `logging_handler.cs` | пишет структурированный лог по каждому событию |
| `metric_handler.cs` | инкрементирует метрики (`infrastructure/monitoring/metrics_collector.cs`) |
| `notification_handler.cs` | триггерит уведомления (например, для WebSocket-подписчиков) |
| `webhook_handler.cs` | вызывает внешние вебхуки для интеграций |
| `trigger_handler.cs` | скриптовые триггеры мира (ловушки, катсцены) через `infrastructure/ai/script_engine.cs` |
| `ai_handler.cs` | реагирует на события, запуская поведение NPC/монстров (`infrastructure/ai`) |
| `replay_handler.cs` | используется при воспроизведении истории (отладка, восстановление проекций) |

Обработчики **не** должны изменять состояние агрегатов напрямую — они реагируют на уже случившееся
событие (побочные эффекты, аналитика, интеграции), а не решают судьбу игровой механики.

---

## 8. Саги (`domain/sagas`, `infrastructure/coordination/saga_coordinator.cs`)

Сага — координатор долгоживущего процесса, который сам реагирует на события и рассылает новые команды.
Пример — `CombatSaga`:

1. `CombatStarted` -> сага рассылает `RollInitiative` каждому участнику.
2. Когда все откатали инициативу (`InitiativeRolled` от всех) -> отправляет `StartRound`.
3. `CombatRoundStarted` -> сортирует участников по инициативе, строит `TurnOrder`, отправляет `NextTurn`.
4. `CombatTurnEnded` -> либо следующий ход, либо `EndRound` + новый `StartRound`.
5. `CharacterDied` / `ParticipantRemovedFromCombat` -> проверяет условие конца боя (одна из сторон
   выбита) и завершает сагу, отправляя `EndCombat`.

Аналогично устроены `LevelUpSaga`, `QuestSaga`, `TradeSaga`. `SagaCoordinator` хранит состояние саг,
диспатчит им входящие события и переживает рестарт процесса (см. `SagaCoordinatorRecoveryTests.cs`)
— состояние саги, как и агрегата, можно восстановить из истории.

---

## 9. Порядок добавления новой механики

Чтобы добавить новую игровую операцию (например, "накладывать проклятие"):

1. **Событие** — добавить `record CurseApplied(...) : IDomainEvent` в `domain/events/*.cs`.
2. **Команда** — добавить `record ApplyCurse(...) : ICommand` в `domain/commands/*.cs`.
3. **Агрегат** — добавить публичный метод `ApplyCurse(...)`, который проверяет правила и вызывает
   `ApplyChange(new CurseApplied(...))`; добавить `case CurseApplied e:` в `ApplyEvent`.
4. **Обработчик команды** — в соответствующем `*_handler.cs`: загрузить агрегат, вызвать метод, сохранить.
5. **Проекция** (если нужно отдавать состояние наружу) — обновить DTO и логику построения read-модели.
6. **REST/WebSocket эндпоинт** — добавить маршрут в `presentation/api/rest_api.cs` и, если нужно,
   поддержку в `websocket_handler.cs`.
7. **Тест** — юнит-тест агрегата в `tests/unit`, проверяющий, что нужное событие появляется в
   `GetUncommittedEvents()` после вызова метода.

---

## 10. Ограничения текущей реализации

- Подписка `PostgresEventStore.SubscribeAsync` — заглушка (события не транслируются через LISTEN/NOTIFY
  или polling); реал-тайм доставка сейчас идёт через `IEventBus`/WebSocket, а не через сам Event Store.
- `EventMetadata.UserId`/`GameSessionId` в базовом `Save<T>` — пустые заглушки; используйте
  `SaveWithMetadata` с реальным контекстом запроса, если нужен корректный аудит.
- `GetAllEvents()` читает всю таблицу без пагинации — годится для отладки/дампов, не для прод-нагрузки.
