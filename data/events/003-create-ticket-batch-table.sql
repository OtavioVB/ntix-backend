-- Lotes de ingressos dos eventos (REQ10).
-- Idempotente: pode ser executado mais de uma vez.

-- Chave alternativa (id_event, id_producer), alvo da FK composta do lote: o lote só aponta para evento do mesmo produtor.
CREATE UNIQUE INDEX IF NOT EXISTS ux_event_id_event_id_producer ON events.event (id_event, id_producer);

CREATE TABLE IF NOT EXISTS events.ticket_batch (
    id_ticket_batch        UUID          NOT NULL CONSTRAINT pk_ticket_batch PRIMARY KEY,
    id_event               UUID          NOT NULL,
    id_producer            UUID          NOT NULL,
    name                   VARCHAR(64)   NOT NULL,
    note                   VARCHAR(140)  NULL,
    price                  NUMERIC(18,2) NOT NULL CONSTRAINT ck_ticket_batch_price CHECK (price >= 1.00),
    quantity               INTEGER       NOT NULL CONSTRAINT ck_ticket_batch_quantity CHECK (quantity > 0),
    sales_starts_at        TIMESTAMPTZ   NOT NULL,
    sales_ends_at          TIMESTAMPTZ   NOT NULL,
    status                 VARCHAR(32)   NOT NULL CONSTRAINT ck_ticket_batch_status CHECK (status IN ('ACTIVE', 'INACTIVE')),
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL,
    updated_at             TIMESTAMPTZ   NOT NULL,
    updated_correlation_id UUID          NOT NULL,
    CONSTRAINT fk_ticket_batch_event FOREIGN KEY (id_event, id_producer) REFERENCES events.event (id_event, id_producer),
    CONSTRAINT ck_ticket_batch_sales_period CHECK (sales_ends_at > sales_starts_at)
);
