-- Índices dos eventos do produtor.
-- Idempotente: pode ser executado mais de uma vez.

-- RN4: o slug é global, porque a rota do hub é /eventos/{slug}.
CREATE UNIQUE INDEX IF NOT EXISTS ux_event_slug ON events.event (slug);

-- Lista do painel: eventos do produtor por data.
CREATE INDEX IF NOT EXISTS ix_event_id_producer_starts_at ON events.event (id_producer, starts_at);
