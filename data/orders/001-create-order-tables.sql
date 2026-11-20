-- Pedidos de ingressos e seus itens (REQ6).
-- Idempotente: pode ser executado mais de uma vez.

CREATE SCHEMA IF NOT EXISTS orders;

-- O id_producer é desnormalizado do evento, para o isolamento por produtor (RN14); a FK composta garante que é o do evento.
CREATE TABLE IF NOT EXISTS orders.orders (
    id_order               UUID          NOT NULL CONSTRAINT pk_orders PRIMARY KEY,
    code                   VARCHAR(10)   NOT NULL,
    id_producer            UUID          NOT NULL,
    id_event               UUID          NOT NULL,
    id_customer            UUID          NOT NULL CONSTRAINT fk_orders_customer REFERENCES customers.customer (id_customer),
    sales_channel          VARCHAR(16)   NOT NULL CONSTRAINT ck_orders_sales_channel CHECK (sales_channel IN ('HUB', 'STORE')),
    total_amount           NUMERIC(18,2) NOT NULL CONSTRAINT ck_orders_total_amount CHECK (total_amount > 0),
    status                 VARCHAR(32)   NOT NULL CONSTRAINT ck_orders_status CHECK (status IN ('PENDING_PAYMENT', 'PAID', 'EXPIRED')),
    expires_at             TIMESTAMPTZ   NOT NULL,
    paid_at                TIMESTAMPTZ   NULL,
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL,
    updated_at             TIMESTAMPTZ   NOT NULL,
    updated_correlation_id UUID          NOT NULL,
    CONSTRAINT fk_orders_event FOREIGN KEY (id_event, id_producer) REFERENCES events.event (id_event, id_producer)
);

-- Preço e nome do lote ficam congelados no item (RN7).
CREATE TABLE IF NOT EXISTS orders.order_item (
    id_order_item   UUID          NOT NULL CONSTRAINT pk_order_item PRIMARY KEY,
    id_order        UUID          NOT NULL CONSTRAINT fk_order_item_order REFERENCES orders.orders (id_order),
    id_ticket_batch UUID          NOT NULL CONSTRAINT fk_order_item_ticket_batch REFERENCES events.ticket_batch (id_ticket_batch),
    batch_name      VARCHAR(64)   NOT NULL,
    quantity        INTEGER       NOT NULL CONSTRAINT ck_order_item_quantity CHECK (quantity > 0),
    unit_price      NUMERIC(18,2) NOT NULL CONSTRAINT ck_order_item_unit_price CHECK (unit_price > 0),
    CONSTRAINT ux_order_item_order_batch UNIQUE (id_order, id_ticket_batch)
);
