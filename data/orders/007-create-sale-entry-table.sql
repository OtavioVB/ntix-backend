-- Vendas efetivadas (REQ14, RN13 e RN14): uma linha por item de pedido pago, gravada na mesma transação da efetivação (REQ8).
-- Tabela de fatos que só cresce: particionada por mês de pagamento, para o gráfico de vendas por dia ler só os meses pedidos.
-- Idempotente: pode ser executado mais de uma vez.

-- A chave primária de uma tabela particionada inclui a chave de partição.
CREATE TABLE IF NOT EXISTS orders.sale_entry (
    id_order_item          UUID          NOT NULL,
    id_order               UUID          NOT NULL,
    id_producer            UUID          NOT NULL,
    id_event               UUID          NOT NULL,
    id_ticket_batch        UUID          NOT NULL,
    quantity               INTEGER       NOT NULL CONSTRAINT ck_sale_entry_quantity CHECK (quantity > 0),
    unit_price             NUMERIC(18,2) NOT NULL CONSTRAINT ck_sale_entry_unit_price CHECK (unit_price > 0),
    paid_at                TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL,
    CONSTRAINT pk_sale_entry PRIMARY KEY (id_order_item, paid_at),
    CONSTRAINT fk_sale_entry_order_item FOREIGN KEY (id_order_item) REFERENCES orders.order_item (id_order_item)
) PARTITION BY RANGE (paid_at);

-- Um mês por partição, de janeiro de 2026 a dezembro de 2028. As do ano seguinte entram num script novo antes de dezembro de 2028.
DO $$
DECLARE
    month_start DATE := DATE '2026-01-01';
BEGIN
    WHILE month_start < DATE '2029-01-01' LOOP
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS orders.%I PARTITION OF orders.sale_entry FOR VALUES FROM (%L) TO (%L)',
            'sale_entry_' || to_char(month_start, 'YYYY_MM'),
            month_start::timestamp AT TIME ZONE 'UTC',
            (month_start + INTERVAL '1 month')::timestamp AT TIME ZONE 'UTC');

        month_start := (month_start + INTERVAL '1 month')::date;
    END LOOP;
END
$$;

-- Rede de segurança: venda fora dos meses criados não falha. A API avisa quando grava aqui (ntix.sales.default_partition.rows).
CREATE TABLE IF NOT EXISTS orders.sale_entry_default PARTITION OF orders.sale_entry DEFAULT;

-- Gráfico de vendas por dia (RN11): o evento e o intervalo de pagamento, sem ler a linha inteira.
CREATE INDEX IF NOT EXISTS ix_sale_entry_id_event_paid_at ON orders.sale_entry (id_event, paid_at) INCLUDE (quantity, unit_price, id_order);
