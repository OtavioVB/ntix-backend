-- Índices da vitrine pública (REQ11): consultas exatas por situação, data, UF, cidade e categoria.
-- Idempotente: pode ser executado mais de uma vez.

CREATE INDEX IF NOT EXISTS ix_event_status_starts_at ON events.event (status, starts_at);

CREATE INDEX IF NOT EXISTS ix_event_status_state_city ON events.event (status, state, city_normalized);

CREATE INDEX IF NOT EXISTS ix_event_status_category ON events.event (status, category);
