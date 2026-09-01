#nullable enable
using System;
using System.Collections.Generic;

namespace dnd_game.infrastructure.config
{
    /// <summary>
    /// Корневой объект конфигурации приложения DnD.
    /// Содержит все настройки, необходимые для функционирования игры.
    /// </summary>
    public class Settings
    {
        // ---------- Подключения ----------

        /// <summary>Строка подключения к основной базе данных.</summary>
        public string DbConnectionString { get; set; } = string.Empty;

        /// <summary>Хост RabbitMQ (по умолчанию localhost).</summary>
        public string RabbitMqHost { get; set; } = "localhost";

        /// <summary>Порт RabbitMQ (по умолчанию 5672).</summary>
        public int RabbitMqPort { get; set; } = 5672;

        /// <summary>Строка подключения к Redis для кэширования и хранения состояний саг.</summary>
        public string RedisConnectionString { get; set; } = string.Empty;

        // ---------- Хранилище событий ----------

        /// <summary>Настройки Event Store.</summary>
        public EventStoreSettings EventStore { get; set; } = new();

        // ---------- Игровые правила ----------

        /// <summary>Настройки игровых правил, используемые по умолчанию.</summary>
        public GameRulesSettings GameRules { get; set; } = new();

        // ---------- Поведение AI ----------

        /// <summary>Настройки искусственного интеллекта.</summary>
        public AiSettings Ai { get; set; } = new();

        // ---------- Безопасность и аутентификация ----------

        /// <summary>Настройки безопасности (JWT, CORS, webhook-подписи).</summary>
        public SecuritySettings Security { get; set; } = new();

        // ---------- Уведомления и вебхуки ----------

        /// <summary>Настройки уведомлений и внешних интеграций.</summary>
        public NotificationSettings Notifications { get; set; } = new();

        // ---------- Логирование и аудит ----------

        /// <summary>Настройки логирования и сбора метрик.</summary>
        public LoggingSettings Logging { get; set; } = new();

        // ---------- UI / Отображение ----------

        /// <summary>Настройки пользовательского интерфейса.</summary>
        public UiSettings Ui { get; set; } = new();

        // ---------- Технические лимиты ----------

        /// <summary>Технические ограничения для защиты от чрезмерной нагрузки.</summary>
        public TechnicalLimits Limits { get; set; } = new();
    }

    /// <summary>
    /// Настройки Event Store.
    /// </summary>
    public class EventStoreSettings
    {
        /// <summary>Провайдер хранилища событий: Postgres, EventStoreDB, InMemory.</summary>
        public string Provider { get; set; } = "Postgres";

        /// <summary>Включить ли создание снимков состояния (snapshots).</summary>
        public bool EnableSnapshotting { get; set; } = true;

        /// <summary>Интервал создания снимков (каждые N событий).</summary>
        public int SnapshotInterval { get; set; } = 100;

        /// <summary>Максимальный размер страницы при чтении событий.</summary>
        public int MaxReadPageSize { get; set; } = 500;
    }

    /// <summary>Настройки SMTP-сервера для отправки почты.</summary>
    public class SmtpSettings
    {
        /// <summary>Хост SMTP-сервера (например, smtp.gmail.com).</summary>
        public string Host { get; set; } = "localhost";

        /// <summary>Порт SMTP-сервера (обычно 587 для TLS).</summary>
        public int Port { get; set; } = 587;

        /// <summary>Использовать SSL/TLS.</summary>
        public bool EnableSsl { get; set; } = true;

        /// <summary>Логин для аутентификации на SMTP-сервере.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Пароль для аутентификации на SMTP-сервере.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Email отправителя.</summary>
        public string FromEmail { get; set; } = "noreply@dndgame.local";

        /// <summary>Отображаемое имя отправителя.</summary>
        public string FromName { get; set; } = "DnD Game";
    }

    /// <summary>
    /// Настройки игровых правил по умолчанию (могут быть переопределены конкретной кампанией).
    /// </summary>
    public class GameRulesSettings
    {
        /// <summary>Количество очков для покупки характеристик по стандартной системе point buy.</summary>
        public int StandardPointBuyPoints { get; set; } = 27;

        /// <summary>Максимальный уровень персонажа.</summary>
        public int MaxLevel { get; set; } = 20;

        /// <summary>Включена ли механика переноски груза (encumbrance).</summary>
        public bool EncumberanceEnabled { get; set; } = false;

        /// <summary>Даёт ли фланкирование преимущество при атаке (опциональное правило).</summary>
        public bool FlankingAdvantage { get; set; } = false;

        /// <summary>Требуется ли дополнительная стоимость за диагональное перемещение.</summary>
        public bool DiagonalMovementCostExtra { get; set; } = false;

        /// <summary>Способ определения инициативы: DexterityCheck, GroupInitiative, D20DexMod.</summary>
        public string InitiativeResolution { get; set; } = "DexterityCheck";

        /// <summary>Используется ли веховое повышение уровня (Milestone Leveling).</summary>
        public bool MilestoneLeveling { get; set; } = false;

        /// <summary>Использовать ли вариант правил с очками заклинаний.</summary>
        public bool SpellPointsVariant { get; set; } = false;

        /// <summary>База для пассивной внимательности (обычно 10).</summary>
        public int PassivePerceptionBase { get; set; } = 10;

        /// <summary>
        /// Таблица порогов опыта для повышения уровня (ключ — целевой уровень, значение — необходимый опыт).
        /// </summary>
        public Dictionary<int, int> ExperienceThresholds { get; set; } = new()
        {
            {2, 300}, {3, 900}, {4, 2700}, {5, 6500}, {6, 14000},
            {7, 23000}, {8, 34000}, {9, 48000}, {10, 64000},
            {11, 85000}, {12, 100000}, {13, 120000}, {14, 140000},
            {15, 165000}, {16, 195000}, {17, 225000}, {18, 265000},
            {19, 305000}, {20, 355000}
        };
    }

    /// <summary>
    /// Настройки искусственного интеллекта.
    /// </summary>
    public class AiSettings
    {
        /// <summary>Включить ли ИИ для монстров.</summary>
        public bool EnableMonsterAi { get; set; } = true;

        /// <summary>Включить ли деревья поведения для NPC.</summary>
        public bool EnableNpcBehaviorTrees { get; set; } = true;

        /// <summary>Периодичность тика ИИ в миллисекундах.</summary>
        public int AiTickIntervalMs { get; set; } = 500;

        /// <summary>Интервал обновления восприятия в миллисекундах.</summary>
        public int PerceptionRefreshIntervalMs { get; set; } = 2000;

        /// <summary>Порог низкого здоровья (доля от максимума) для смены тактики.</summary>
        public float LowHealthThreshold { get; set; } = 0.25f;

        /// <summary>Порог критического здоровья для бегства/отчаяния.</summary>
        public float CriticalHealthThreshold { get; set; } = 0.10f;

        /// <summary>Время жизни факта на доске объявлений по умолчанию (в секундах).</summary>
        public int BlackboardDefaultFactExpirationSeconds { get; set; } = 60;

        /// <summary>Время хранения памяти (в минутах).</summary>
        public int BlackboardMemoryRetentionMinutes { get; set; } = 30;
    }

    /// <summary>
    /// Настройки безопасности.
    /// </summary>
    public class SecuritySettings
    {
        /// <summary>Секретный ключ для подписи JWT. В production обязательно заменить!</summary>
        public string JwtSecret { get; set; } = "change-me-in-production";

        /// <summary>Время жизни JWT-токена в минутах.</summary>
        public int JwtExpirationMinutes { get; set; } = 1440;

        /// <summary>Включить ли HMAC-подпись для webhook-уведомлений.</summary>
        public bool EnableHmacWebhookSigning { get; set; } = true;

        /// <summary>Допустимое расхождение времени при проверке подписи (в секундах).</summary>
        public int WebhookSignatureToleranceSeconds { get; set; } = 300;

        /// <summary>Разрешённые источники CORS.</summary>
        public string[] AllowedOrigins { get; set; } = ["http://localhost:3000"];
    }

    /// <summary>
    /// Настройки уведомлений и интеграций.
    /// </summary>
    public class NotificationSettings
    {
        /// <summary>Включить ли внутриигровые уведомления.</summary>
        public bool EnableInGameNotifications { get; set; } = true;

        /// <summary>Включить ли push-уведомления.</summary>
        public bool EnablePushNotifications { get; set; } = false;

        /// <summary>API-ключ для push-сервиса.</summary>
        public string PushApiKey { get; set; } = string.Empty;

        /// <summary>SMTP-хост для отправки email.</summary>
        public string EmailSmtpHost { get; set; } = string.Empty;

        /// <summary>SMTP-порт.</summary>
        public int EmailSmtpPort { get; set; } = 587;

        /// <summary>Максимальное количество повторных попыток отправки webhook.</summary>
        public int WebhookMaxRetries { get; set; } = 3;

        /// <summary>Таймаут одного запроса webhook в секундах.</summary>
        public int WebhookTimeoutSeconds { get; set; } = 10;
    }

    /// <summary>
    /// Настройки логирования и сбора метрик.
    /// </summary>
    public class LoggingSettings
    {
        /// <summary>Вести ли журнал боевых действий.</summary>
        public bool EnableCombatLog { get; set; } = true;

        /// <summary>Собирать ли подробные метрики.</summary>
        public bool EnableDetailedMetrics { get; set; } = true;

        /// <summary>Логировать ли все доменные события (только для отладки).</summary>
        public bool LogAllDomainEvents { get; set; } = false;

        /// <summary>Экспортёр метрик (например, Prometheus).</summary>
        public string MetricsExporter { get; set; } = "Prometheus";
    }

    /// <summary>
    /// Настройки пользовательского интерфейса.
    /// </summary>
    public class UiSettings
    {
        /// <summary>Показывать ли результаты бросков костей.</summary>
        public bool ShowDiceRolls { get; set; } = true;

        /// <summary>Использовать тёмную тему по умолчанию.</summary>
        public bool DarkModeByDefault { get; set; } = true;

        /// <summary>Интервал автосохранения персонажа (в минутах).</summary>
        public int AutoSaveCharacterIntervalMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Технические ограничения (защита от злоупотреблений и перегрузок).
    /// </summary>
    public class TechnicalLimits
    {
        /// <summary>Максимальная длина имени персонажа.</summary>
        public int MaxCharacterNameLength { get; set; } = 50;

        /// <summary>Максимальное количество предметов в инвентаре.</summary>
        public int MaxInventoryItems { get; set; } = 500;

        /// <summary>Максимальное количество известных заклинаний.</summary>
        public int MaxSpellsKnown { get; set; } = 300;

        /// <summary>Максимальное количество участников боя.</summary>
        public int MaxParticipantsPerCombat { get; set; } = 50;

        /// <summary>Максимальное количество активных состояний на персонаже.</summary>
        public int MaxActiveConditionsPerCharacter { get; set; } = 20;

        /// <summary>Максимальное количество предметов в одном торговом предложении.</summary>
        public int MaxTradeItemsPerOffer { get; set; } = 20;
    }
}