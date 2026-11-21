-- Ingressos emitidos para pedidos pagos (REQ8). Um por unidade comprada, com código público único (RN7).
-- Idempotente: pode ser executado mais de uma vez.

CREATE TABLE IF NOT EXISTS orders.ticket (
    id_ticket              UUID          NOT NULL CONSTRAINT pk_ticket PRIMARY KEY,
    id_order_item          UUID          NOT NULL CONSTRAINT fk_ticket_order_item REFERENCES orders.order_item (id_order_item),
    id_order               UUID          NOT NULL CONSTRAINT fk_ticket_order REFERENCES orders.orders (id_order),
    id_customer            UUID          NOT NULL CONSTRAINT fk_ticket_customer REFERENCES customers.customer (id_customer),
    id_event               UUID          NOT NULL,
    code                   VARCHAR(12)   NOT NULL,
    holder_name            VARCHAR(256)  NOT NULL,
    status                 VARCHAR(16)   NOT NULL CONSTRAINT ck_ticket_status CHECK (status IN ('VALID', 'USED')),
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL
);
