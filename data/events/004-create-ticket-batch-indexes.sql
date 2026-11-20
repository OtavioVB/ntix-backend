-- Índices dos lotes de ingressos.
-- Idempotente: pode ser executado mais de uma vez.

-- RN2: nome do lote único dentro do evento.
CREATE UNIQUE INDEX IF NOT EXISTS ux_ticket_batch_event_name ON events.ticket_batch (id_event, name);

CREATE INDEX IF NOT EXISTS ix_ticket_batch_id_event ON events.ticket_batch (id_event);
