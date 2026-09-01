-- Частичный индекс для неотозванных токенов
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_active
    ON refresh_tokens(expires_at)
    WHERE is_revoked = FALSE;
ALTER TABLE refresh_tokens
    ADD CONSTRAINT fk_refresh_tokens_user
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;