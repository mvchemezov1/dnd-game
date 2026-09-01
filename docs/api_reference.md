## 📘 Документация по API – ключевые сценарии

Ниже приведены примеры основных взаимодействий с REST API сервера DnD Game. Для полноценного описания всех эндпоинтов используйте Swagger (доступен по адресу `/swagger`). В этом руководстве описаны наиболее частые сценарии с примерами запросов и ответов.

---

### Базовые сведения

- **Базовый URL:** `http://localhost:5000/api` (замените на ваш хост)
- **Формат данных:** JSON
- **Аутентификация:** JWT Bearer Token (получается при входе). Токен передаётся в заголовке `Authorization: Bearer <token>`.
- **Ошибки:** возвращаются в формате `{ "error": "сообщение" }` с соответствующим HTTP-кодом.

---

### 1. Аутентификация и получение токена

#### Регистрация

```http
POST /api/auth/register
Content-Type: application/json

{
  "username": "player1",
  "email": "player1@example.com",
  "password": "Test123!",
  "role": "Player"
}
```

**Ответ (успех):**

```json
{
  "success": true,
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4=",
  "userId": "11111111-1111-1111-1111-111111111111",
  "expiresAt": "2026-08-18T12:00:00Z"
}
```

#### Вход

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "player1",
  "password": "Test123!"
}
```

**Ответ:** аналогичен регистрации (возвращает токен).

---

### 2. Управление персонажами

#### Создание персонажа

```http
POST /api/characters
Authorization: Bearer <токен>
Content-Type: application/json

{
  "characterId": "22222222-2222-2222-2222-222222222222",
  "name": "Aria",
  "maxHitPoints": 24
}
```

**Ответ:** `200 OK` (без тела).

#### Получение списка персонажей

```http
GET /api/characters
Authorization: Bearer <токен>
```

**Ответ (пример):**

```json
[
  {
    "id": "22222222-2222-2222-2222-222222222222",
    "name": "Aria",
    "hitPoints": 24,
    "maxHitPoints": 24,
    "level": 1,
    "class": "",
    "race": ""
  }
]
```

#### Нанесение урона

```http
POST /api/characters/{characterId}/damage
Authorization: Bearer <токен>
Content-Type: application/json

{
  "amount": 5,
  "damageType": "slashing"
}
```

**Ответ:** `200 OK`

---

### 3. Кампании и квесты

#### Создание кампании

```http
POST /api/campaign
Authorization: Bearer <токен>
Content-Type: application/json

{
  "campaignId": "33333333-3333-3333-3333-333333333333",
  "name": "Lost Mine of Phandelver",
  "gameMasterId": "44444444-4444-4444-4444-444444444444"
}
```

**Ответ:** `200 OK`

#### Создание квеста

```http
POST /api/campaign/{campaignId}/quests
Authorization: Bearer <токен>
Content-Type: application/json

{
  "questId": "55555555-5555-5555-5555-555555555555",
  "title": "Slay the Dragon",
  "objectives": [
    {
      "description": "Find the dragon's lair",
      "requiredProgress": 1
    },
    {
      "description": "Defeat the dragon",
      "requiredProgress": 1
    }
  ],
  "rewards": [
    {
      "description": "Gold reward",
      "gold": 100,
      "experiencePoints": 50
    }
  ],
  "participantIds": ["22222222-2222-2222-2222-222222222222"]
}
```

**Ответ:** `200 OK`

#### Принятие квеста

```http
POST /api/campaign/{campaignId}/quests/{questId}/accept
Authorization: Bearer <токен>
```

**Ответ:** `200 OK`

---

### 4. Бой

#### Начало боя

```http
POST /api/combat
Authorization: Bearer <токен>
Content-Type: application/json

{
  "combatId": "66666666-6666-6666-6666-666666666666",
  "participants": [
    "22222222-2222-2222-2222-222222222222",
    "77777777-7777-7777-7777-777777777777"
  ]
}
```

**Ответ:** `200 OK`

#### Бросок инициативы

```http
POST /api/combat/{combatId}/initiative
Authorization: Bearer <токен>
Content-Type: application/json

{
  "participantId": "22222222-2222-2222-2222-222222222222",
  "initiativeRoll": 15,
  "dexterityModifier": 2
}
```

**Ответ:** `200 OK`

#### Стандартное действие (атака)

```http
POST /api/combat/{combatId}/actions/standard
Authorization: Bearer <токен>
Content-Type: application/json

{
  "participantId": "22222222-2222-2222-2222-222222222222",
  "actionType": "Attack",
  "targetId": "77777777-7777-7777-7777-777777777777"
}
```

**Ответ:** `200 OK`

#### Получение статуса боя

```http
GET /api/combat/{combatId}
Authorization: Bearer <токен>
```

**Ответ:** полный объект `CombatStatusDto`.

---

### 5. Крафт

#### Получение доступных рецептов

```http
GET /api/crafting/recipes?characterId={characterId}
Authorization: Bearer <токен>
```

**Ответ:** список рецептов.

#### Начало крафта

```http
POST /api/crafting/start
Authorization: Bearer <токен>
Content-Type: application/json

{
  "characterId": "22222222-2222-2222-2222-222222222222",
  "recipeId": "88888888-8888-8888-8888-888888888888"
}
```

**Ответ:**

```json
{
  "processId": "99999999-9999-9999-9999-999999999999",
  "estimatedCompletion": "2026-08-18T14:00:00Z"
}
```

---

### 6. Торговля

#### Создание предложения обмена

```http
POST /api/trade/offer
Authorization: Bearer <токен>
Content-Type: application/json

{
  "fromCharacterId": "22222222-2222-2222-2222-222222222222",
  "toCharacterId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "offeredItems": [
    { "itemId": "sword1", "itemName": "Iron Sword", "quantity": 1 }
  ],
  "offeredGold": 10,
  "requestedItems": [
    { "itemId": "shield1", "itemName": "Wooden Shield", "quantity": 1 }
  ],
  "requestedGold": 5
}
```

**Ответ:**

```json
{
  "offerId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "status": "Pending"
}
```

#### Принятие предложения

```http
POST /api/trade/accept
Authorization: Bearer <токен>
Content-Type: application/json

{
  "offerId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
}
```

**Ответ:** `200 OK`

---

### 7. Диалоги

#### Начало диалога с NPC

```http
POST /api/dialog/start
Authorization: Bearer <токен>
Content-Type: application/json

{
  "dialogueId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "npcId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "characterId": "22222222-2222-2222-2222-222222222222"
}
```

**Ответ:** состояние диалога (текущий узел).

#### Выбор варианта ответа

```http
POST /api/dialog/option
Authorization: Bearer <токен>
Content-Type: application/json

{
  "dialogueId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "optionId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
}
```

**Ответ:** обновлённое состояние диалога.

---

### 8. Путешествия

#### Перемещение на тактической карте

```http
POST /api/travel/move
Authorization: Bearer <токен>
Content-Type: application/json

{
  "characterId": "22222222-2222-2222-2222-222222222222",
  "targetX": 5,
  "targetY": 10
}
```

**Ответ:** `200 OK`

#### Использовать Dash

```http
POST /api/travel/dash
Authorization: Bearer <токен>
Content-Type: application/json

{
  "characterId": "22222222-2222-2222-2222-222222222222"
}
```

**Ответ:** `200 OK`

---

### 9. WebSocket (в реальном времени)

Для получения обновлений в реальном времени (события боя, изменение HP, чат) используйте WebSocket-соединение:

```
ws://localhost:5000/ws?token=<JWT токен>
```

После подключения вы будете получать события в формате JSON:
- `{ "type": "event", "payload": { "eventType": "CharacterDamageTaken", "eventJson": "..." } }`
- `{ "type": "command_response", "payload": { "success": true, "resultJson": null } }`
- `{ "type": "error", "errorCode": "AUTH_REQUIRED", "message": "..." }`

Также можно отправлять команды через WebSocket, отправляя сообщения вида:

```json
{
  "type": "command",
  "correlationId": "optional-id",
  "payload": {
    "commandType": "dnd_game.domain.commands.DealDamage",
    "commandJson": "{\"CharacterId\":\"...\",\"Amount\":5}"
  }
}
```

---

### Коды ошибок (часто встречающиеся)

| HTTP код | Описание |
|----------|----------|
| `400` | Некорректный запрос (невалидные данные) |
| `401` | Требуется аутентификация |
| `403` | Недостаточно прав |
| `404` | Ресурс не найден |
| `409` | Конфликт версий (при одновременной записи) |
| `500` | Внутренняя ошибка сервера |

---

### Полезные ссылки

- **Swagger UI:** `/swagger` (интерактивная документация)
- **Health-check:** `/health`
- **Панель разработчика (только Admin):** `/dev/dashboard`

---

*Документация актуальна на 18 августа 2026 г. В случае расхождений с Swagger – приоритет имеет Swagger (он отражает текущую реализацию).*