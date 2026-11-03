-- Outbox: eventos de integração gravados na mesma transação da operação que os origina.
-- Idempotente: pode ser executado mais de uma vez.

CREATE SCHEMA IF NOT EXISTS outbox;

CREATE TABLE IF NOT EXISTS outbox.outbox_event (
    id_outbox_event        UUID          NOT NULL CONSTRAINT pk_outbox_event PRIMARY KEY,
    event_type             VARCHAR(128)  NOT NULL,
    aggregate_id           UUID          NOT NULL,
    payload                JSONB         NOT NULL,
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL,
    processed_at           TIMESTAMPTZ   NULL
);

-- Leitura dos pendentes pelo publicador, em ordem de criação.
CREATE INDEX IF NOT EXISTS ix_outbox_event_pending ON outbox.outbox_event (created_at) WHERE processed_at IS NULL;
