-- Índices únicos de e-mail e slug da produtora e de e-mail do usuário.
-- Idempotente: pode ser executado mais de uma vez.

CREATE UNIQUE INDEX IF NOT EXISTS ux_producer_email ON producers.producer (email);

CREATE UNIQUE INDEX IF NOT EXISTS ux_producer_slug ON producers.producer (slug);

CREATE UNIQUE INDEX IF NOT EXISTS ux_producer_user_email ON producers.producer_user (email);

CREATE INDEX IF NOT EXISTS ix_producer_user_id_producer ON producers.producer_user (id_producer);
