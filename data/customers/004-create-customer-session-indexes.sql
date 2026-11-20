-- Índices das sessões do cliente (REQ12).
-- Idempotente: pode ser executado mais de uma vez.

-- Busca exata pelo hash do refresh token na renovação e no logout.
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_session_refresh_token_hash ON customers.customer_session (refresh_token_hash);

-- Revogação da família inteira no logout e no reuso.
CREATE INDEX IF NOT EXISTS ix_customer_session_id_family ON customers.customer_session (id_family);

CREATE INDEX IF NOT EXISTS ix_customer_session_id_customer ON customers.customer_session (id_customer);
