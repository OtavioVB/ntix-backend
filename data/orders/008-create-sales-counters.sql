-- Contadores de venda (REQ14, RN15): somados na mesma transação da efetivação (REQ8), para o resumo não ler pedidos.
-- Por lote (detalhe da página do evento) e por evento (card e resumo; pedidos com itens de vários lotes contam uma vez).
-- Idempotente: pode ser executado mais de uma vez.

CREATE TABLE IF NOT EXISTS orders.ticket_batch_sales (
    id_ticket_batch UUID          NOT NULL CONSTRAINT pk_ticket_batch_sales PRIMARY KEY
                                           CONSTRAINT fk_ticket_batch_sales_ticket_batch REFERENCES events.ticket_batch (id_ticket_batch),
    id_producer     UUID          NOT NULL,
    id_event        UUID          NOT NULL,
    sold_quantity   INTEGER       NOT NULL CONSTRAINT ck_ticket_batch_sales_sold_quantity CHECK (sold_quantity >= 0),
    gross_amount    NUMERIC(18,2) NOT NULL CONSTRAINT ck_ticket_batch_sales_gross_amount CHECK (gross_amount >= 0),
    paid_orders     INTEGER       NOT NULL CONSTRAINT ck_ticket_batch_sales_paid_orders CHECK (paid_orders >= 0),
    updated_at      TIMESTAMPTZ   NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_ticket_batch_sales_id_event ON orders.ticket_batch_sales (id_event) INCLUDE (sold_quantity, gross_amount, paid_orders);

CREATE TABLE IF NOT EXISTS orders.event_sales (
    id_event      UUID          NOT NULL CONSTRAINT pk_event_sales PRIMARY KEY,
    id_producer   UUID          NOT NULL,
    sold_quantity INTEGER       NOT NULL CONSTRAINT ck_event_sales_sold_quantity CHECK (sold_quantity >= 0),
    gross_amount  NUMERIC(18,2) NOT NULL CONSTRAINT ck_event_sales_gross_amount CHECK (gross_amount >= 0),
    paid_orders   INTEGER       NOT NULL CONSTRAINT ck_event_sales_paid_orders CHECK (paid_orders >= 0),
    updated_at    TIMESTAMPTZ   NOT NULL,
    CONSTRAINT fk_event_sales_event FOREIGN KEY (id_event, id_producer) REFERENCES events.event (id_event, id_producer)
);

-- Lista do painel (RN10): os resumos dos eventos de uma produtora numa leitura só do índice.
CREATE INDEX IF NOT EXISTS ix_event_sales_id_producer ON orders.event_sales (id_producer) INCLUDE (id_event, sold_quantity, gross_amount, paid_orders);
