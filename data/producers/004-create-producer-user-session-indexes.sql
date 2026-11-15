-- Índices das sessões do usuário da produtora (REQ5).
-- Idempotente: pode ser executado mais de uma vez.

-- Busca exata pelo hash do refresh token na renovação e no logout.
CREATE UNIQUE INDEX IF NOT EXISTS ux_producer_user_session_refresh_token_hash ON producers.producer_user_session (refresh_token_hash);

-- Revogação da família inteira no logout e no reuso.
CREATE INDEX IF NOT EXISTS ix_producer_user_session_id_family ON producers.producer_user_session (id_family);

CREATE INDEX IF NOT EXISTS ix_producer_user_session_id_producer_user ON producers.producer_user_session (id_producer_user);
