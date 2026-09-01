-- 001_Initial.sql

-- Таблица событий (Event Store)
CREATE TABLE IF NOT EXISTS events (
    id BIGSERIAL PRIMARY KEY,
    event_id UUID NOT NULL UNIQUE,
    aggregate_id UUID NOT NULL,
    aggregate_type TEXT NOT NULL,
    version INT NOT NULL,
    event_type TEXT NOT NULL,
    data JSONB NOT NULL,
    user_id UUID NOT NULL,
    session_id UUID NOT NULL,
    custom_headers JSONB,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (aggregate_id, version)
);

-- Таблица снапшотов
CREATE TABLE IF NOT EXISTS snapshots (
    aggregate_id UUID NOT NULL,
    version INT NOT NULL,
    data BYTEA NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (aggregate_id, version)
);

-- Таблица пользователей
CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY,
    username TEXT UNIQUE NOT NULL,
    email TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    global_role TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    campaign_roles JSONB DEFAULT '{}'::jsonb
);

-- Таблица refresh-токенов
CREATE TABLE IF NOT EXISTS refresh_tokens (
    token_hash TEXT PRIMARY KEY,
    user_id UUID NOT NULL,
    device_info TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    is_revoked BOOLEAN NOT NULL DEFAULT FALSE
);

-- Таблица рецептов крафта
CREATE TABLE IF NOT EXISTS crafting_recipes (
    recipe_id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    item_id TEXT NOT NULL,
    item_name TEXT NOT NULL,
    gold_cost INT NOT NULL DEFAULT 0,
    crafting_time_hours INT NOT NULL,
    required_tool TEXT,
    required_proficiency_level INT NOT NULL DEFAULT 0,
    is_magical BOOLEAN NOT NULL DEFAULT FALSE,
    required_spell_id TEXT,
    difficulty_class INT NOT NULL DEFAULT 10,
    associated_skill TEXT,
    components JSONB NOT NULL DEFAULT '[]'::jsonb
);

-- Таблица процессов крафта
CREATE TABLE IF NOT EXISTS crafting_processes (
    process_id UUID PRIMARY KEY,
    character_id UUID NOT NULL,
    recipe_id UUID NOT NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    total_hours INT NOT NULL,
    elapsed_hours INT NOT NULL DEFAULT 0,
    estimated_completion TIMESTAMPTZ
);

-- Таблица определений триггеров
CREATE TABLE IF NOT EXISTS trigger_definitions (
    trigger_id UUID PRIMARY KEY,
    event_name TEXT NOT NULL,
    conditions JSONB NOT NULL DEFAULT '[]'::jsonb,
    actions JSONB NOT NULL DEFAULT '[]'::jsonb,
    is_one_shot BOOLEAN NOT NULL DEFAULT TRUE,
    cooldown_seconds INT NOT NULL DEFAULT 0,
    delay_seconds INT NOT NULL DEFAULT 0,
    priority INT NOT NULL DEFAULT 0
);

-- Таблица состояний триггеров
CREATE TABLE IF NOT EXISTS trigger_states (
    trigger_id UUID PRIMARY KEY,
    has_been_triggered BOOLEAN NOT NULL DEFAULT FALSE,
    last_triggered_utc TIMESTAMPTZ,
    cooldown_ends_utc TIMESTAMPTZ
);

-- Таблица webhook-подписок
CREATE TABLE IF NOT EXISTS webhook_subscriptions (
    id UUID PRIMARY KEY,
    event_type TEXT NOT NULL,
    url TEXT NOT NULL,
    secret TEXT,
    max_retries INT NOT NULL DEFAULT 3,
    timeout_seconds INT NOT NULL DEFAULT 10,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Таблица состояний саг
CREATE TABLE IF NOT EXISTS saga_states (
    saga_id UUID PRIMARY KEY,
    correlation_id UUID NOT NULL,
    status TEXT NOT NULL,
    version INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ,
    state_json JSONB NOT NULL
);

-- Таблица торговых предложений
CREATE TABLE IF NOT EXISTS trade_offers (
    offer_id UUID PRIMARY KEY,
    from_character_id UUID NOT NULL,
    to_character_id UUID NOT NULL,
    offered_items JSONB NOT NULL DEFAULT '[]'::jsonb,
    offered_gold INT NOT NULL DEFAULT 0,
    requested_items JSONB NOT NULL DEFAULT '[]'::jsonb,
    requested_gold INT NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'Pending',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Таблица связей квестов и участников
CREATE TABLE IF NOT EXISTS quest_participants (
    quest_id UUID NOT NULL,
    character_id UUID NOT NULL,
    joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (quest_id, character_id)
);

-- Таблица требуемых предметов для квестов
CREATE TABLE IF NOT EXISTS quest_required_items (
    quest_id UUID NOT NULL,
    item_id TEXT NOT NULL,
    PRIMARY KEY (quest_id, item_id)
);

-- Таблица привязки квестов к кампаниям
CREATE TABLE IF NOT EXISTS quest_campaigns (
    quest_id UUID PRIMARY KEY,
    campaign_id UUID NOT NULL
);

-- Базовые индексы
CREATE INDEX IF NOT EXISTS idx_events_aggregate_id ON events(aggregate_id);
CREATE INDEX IF NOT EXISTS idx_events_type ON events(event_type);
CREATE INDEX IF NOT EXISTS idx_events_session ON events(session_id);
CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON refresh_tokens(user_id);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_expires_at ON refresh_tokens(expires_at);