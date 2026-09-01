-- 010_AddCharacterOwnership.sql
--
-- Раньше связь «персонаж → игрок-владелец» (а также «персонаж → кампания»
-- и признак NPC) хранилась только в памяти процесса
-- (CharacterOwnershipRepository, ConcurrentDictionary, AddSingleton).
-- Сами персонажи персистентны (event sourcing, таблица events), а
-- владение — нет: после любого перезапуска/передеплоя сервера все связи
-- обнулялись, и GET /api/characters у обычного игрока (не GM) возвращал
-- пустой список, хотя персонажи физически существовали.
--
-- Эта таблица делает владение персистентным наравне с остальными данными.

CREATE TABLE IF NOT EXISTS character_ownership (
    character_id UUID PRIMARY KEY,
    owner_user_id UUID NOT NULL,
    campaign_id UUID NULL,
    is_npc BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_character_ownership_owner ON character_ownership(owner_user_id);
CREATE INDEX IF NOT EXISTS idx_character_ownership_campaign ON character_ownership(campaign_id);
