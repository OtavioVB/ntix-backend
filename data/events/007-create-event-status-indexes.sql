-- Índice do filtro de situação do painel (REQ14, RN3): DRAFT, PUBLISHED e ENDED são comparados com ends_at.
-- Idempotente: pode ser executado mais de uma vez.

CREATE INDEX IF NOT EXISTS ix_event_id_producer_status_ends_at ON events.event (id_producer, status, ends_at);
