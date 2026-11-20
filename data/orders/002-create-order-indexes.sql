-- Índices dos pedidos (REQ6).
-- Idempotente: pode ser executado mais de uma vez.

-- RN10: código do pedido único.
CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_code ON orders.orders (code);

-- Pedidos do cliente, do mais recente para o mais antigo (REQ13).
CREATE INDEX IF NOT EXISTS ix_orders_id_customer_created_at ON orders.orders (id_customer, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_orders_status_expires_at ON orders.orders (status, expires_at);

-- Soma de vendidos e reservados por lote (RN8): o INCLUDE evita ler a tabela dos itens.
CREATE INDEX IF NOT EXISTS ix_order_item_id_ticket_batch ON orders.order_item (id_ticket_batch) INCLUDE (id_order, quantity);
