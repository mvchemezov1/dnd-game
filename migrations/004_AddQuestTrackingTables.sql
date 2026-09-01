-- Связи персонажей с активными квестами
CREATE TABLE IF NOT EXISTS quest_participants (
    quest_id UUID NOT NULL,
    character_id UUID NOT NULL,
    joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (quest_id, character_id)
);

-- Отдельной таблицы квестов в read-модели нет (квесты ведутся через event
-- sourcing), поэтому FK на неё не добавляем.

-- Связи квестов с требуемыми предметами (для ItemAcquired)
CREATE TABLE IF NOT EXISTS quest_required_items (
    quest_id UUID NOT NULL,
    item_id TEXT NOT NULL,
    PRIMARY KEY (quest_id, item_id)
);

-- Индексы для быстрого поиска по персонажу и предмету
CREATE INDEX IF NOT EXISTS idx_quest_participants_char ON quest_participants(character_id);
CREATE INDEX IF NOT EXISTS idx_quest_required_items_item ON quest_required_items(item_id);