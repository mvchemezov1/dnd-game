-- Рецепты крафта
CREATE TABLE IF NOT EXISTS crafting_recipes (
    recipe_id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    item_id TEXT NOT NULL,
    item_name TEXT NOT NULL,
    gold_cost INT NOT NULL DEFAULT 0,
    crafting_time_hours INT NOT NULL DEFAULT 0,
    required_tool TEXT NOT NULL DEFAULT '',
    required_proficiency_level INT NOT NULL DEFAULT 0,
    is_magical BOOLEAN NOT NULL DEFAULT FALSE,
    required_spell_id TEXT,
    difficulty_class INT NOT NULL DEFAULT 10,
    associated_skill TEXT,
    components JSONB NOT NULL DEFAULT '[]'::jsonb
);

-- Процессы крафта
CREATE TABLE IF NOT EXISTS crafting_processes (
    process_id UUID PRIMARY KEY,
    character_id UUID NOT NULL,
    recipe_id UUID NOT NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    total_hours INT NOT NULL,
    elapsed_hours INT NOT NULL DEFAULT 0,
    estimated_completion TIMESTAMPTZ
);

-- Определения триггеров
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

-- Состояния триггеров
CREATE TABLE IF NOT EXISTS trigger_states (
    trigger_id UUID PRIMARY KEY,
    has_been_triggered BOOLEAN NOT NULL DEFAULT FALSE,
    last_triggered_utc TIMESTAMPTZ,
    cooldown_ends_utc TIMESTAMPTZ
);

-- Webhook подписки
CREATE TABLE IF NOT EXISTS webhook_subscriptions (
    id UUID PRIMARY KEY,
    event_type TEXT NOT NULL,
    url TEXT NOT NULL,
    secret TEXT,
    max_retries INT NOT NULL DEFAULT 3,
    timeout_seconds INT NOT NULL DEFAULT 10,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Состояния саг
CREATE TABLE IF NOT EXISTS saga_states (
    saga_id UUID PRIMARY KEY,
    correlation_id UUID NOT NULL,
    status TEXT NOT NULL,
    version INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ,
    state_json JSONB NOT NULL
);

-- Торговые предложения
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

-- Данные торговли NPC (предметы)
CREATE TABLE IF NOT EXISTS trade_items (
    item_id TEXT PRIMARY KEY,
    item_name TEXT NOT NULL,
    base_price_gold INT NOT NULL,
    is_magical BOOLEAN NOT NULL DEFAULT FALSE,
    rarity INT NOT NULL DEFAULT 1
);

-- Множители торговли NPC
CREATE TABLE IF NOT EXISTS trade_multipliers (
    npc_id UUID NOT NULL,
    character_id UUID NOT NULL,
    buy_multiplier REAL NOT NULL DEFAULT 1.0,
    sell_multiplier REAL NOT NULL DEFAULT 0.5,
    PRIMARY KEY (npc_id, character_id)
);

-- Связи квест-участники
CREATE TABLE IF NOT EXISTS quest_participants (
    quest_id UUID NOT NULL,
    character_id UUID NOT NULL,
    joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (quest_id, character_id)
);

-- Требуемые предметы квестов
CREATE TABLE IF NOT EXISTS quest_required_items (
    quest_id UUID NOT NULL,
    item_id TEXT NOT NULL,
    PRIMARY KEY (quest_id, item_id)
);

-- Привязка квестов к кампаниям
CREATE TABLE IF NOT EXISTS quest_campaigns (
    quest_id UUID PRIMARY KEY,
    campaign_id UUID NOT NULL
);

-- Диалоговые узлы
CREATE TABLE IF NOT EXISTS dialogue_nodes (
    node_id UUID PRIMARY KEY,
    dialogue_id UUID NOT NULL,
    npc_text TEXT NOT NULL,
    is_exit_node BOOLEAN NOT NULL DEFAULT FALSE,
    options JSONB NOT NULL DEFAULT '[]'::jsonb
);

-- Корневые узлы диалогов
CREATE TABLE IF NOT EXISTS dialogue_roots (
    dialogue_id UUID PRIMARY KEY,
    root_node_id UUID NOT NULL REFERENCES dialogue_nodes(node_id)
);

-- Скрипты ИИ
CREATE TABLE IF NOT EXISTS scripts (
    script_name TEXT PRIMARY KEY,
    description TEXT NOT NULL DEFAULT '',
    commands JSONB NOT NULL DEFAULT '[]'::jsonb
);

-- Состояния диалогов
CREATE TABLE IF NOT EXISTS dialogue_states (
    dialogue_id UUID PRIMARY KEY,
    npc_id UUID NOT NULL,
    character_id UUID NOT NULL,
    current_node_id UUID NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    visited_node_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
    pending_option_id UUID
);

-- Индексы для часто используемых полей
CREATE INDEX IF NOT EXISTS idx_crafting_recipes_name ON crafting_recipes(name);
CREATE INDEX IF NOT EXISTS idx_crafting_processes_character ON crafting_processes(character_id);
CREATE INDEX IF NOT EXISTS idx_trigger_defs_event ON trigger_definitions(event_name);
CREATE INDEX IF NOT EXISTS idx_webhook_subs_event ON webhook_subscriptions(event_type);
CREATE INDEX IF NOT EXISTS idx_saga_states_correlation ON saga_states(correlation_id);
CREATE INDEX IF NOT EXISTS idx_trade_offers_characters ON trade_offers(from_character_id, to_character_id);
CREATE INDEX IF NOT EXISTS idx_quest_participants_char ON quest_participants(character_id);
CREATE INDEX IF NOT EXISTS idx_quest_required_items_item ON quest_required_items(item_id);
CREATE INDEX IF NOT EXISTS idx_dialogue_nodes_dialogue ON dialogue_nodes(dialogue_id);