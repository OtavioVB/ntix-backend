-- Backfill e reconciliação das vendas (REQ14, RN17), a cada aplicação dos scripts.
-- 1. Itens de pedidos pagos que ainda não estão em sale_entry (pagos antes do REQ14) entram com o paid_at do pedido.
-- 2. Os contadores são recalculados de sale_entry.
-- A trava exclusiva segura as efetivações durante o recálculo, para nenhum incremento ser sobrescrito.
-- Idempotente: rodar de novo dá o mesmo resultado.

BEGIN;

LOCK TABLE orders.sale_entry, orders.ticket_batch_sales, orders.event_sales IN EXCLUSIVE MODE;

INSERT INTO orders.sale_entry (id_order_item, id_order, id_producer, id_event, id_ticket_batch, quantity, unit_price, paid_at, created_correlation_id)
SELECT item.id_order_item, o.id_order, o.id_producer, o.id_event, item.id_ticket_batch, item.quantity, item.unit_price, o.paid_at, o.updated_correlation_id
FROM orders.orders o
JOIN orders.order_item item ON item.id_order = o.id_order
WHERE o.status = 'PAID' AND o.paid_at IS NOT NULL
ON CONFLICT ON CONSTRAINT pk_sale_entry DO NOTHING;

INSERT INTO orders.ticket_batch_sales (id_ticket_batch, id_producer, id_event, sold_quantity, gross_amount, paid_orders, updated_at)
SELECT id_ticket_batch, id_producer, id_event, SUM(quantity), SUM(quantity * unit_price), COUNT(DISTINCT id_order), now()
FROM orders.sale_entry
GROUP BY id_ticket_batch, id_producer, id_event
ON CONFLICT ON CONSTRAINT pk_ticket_batch_sales DO UPDATE
SET sold_quantity = EXCLUDED.sold_quantity, gross_amount = EXCLUDED.gross_amount, paid_orders = EXCLUDED.paid_orders, updated_at = EXCLUDED.updated_at
WHERE (orders.ticket_batch_sales.sold_quantity, orders.ticket_batch_sales.gross_amount, orders.ticket_batch_sales.paid_orders)
      IS DISTINCT FROM (EXCLUDED.sold_quantity, EXCLUDED.gross_amount, EXCLUDED.paid_orders);

INSERT INTO orders.event_sales (id_event, id_producer, sold_quantity, gross_amount, paid_orders, updated_at)
SELECT id_event, id_producer, SUM(quantity), SUM(quantity * unit_price), COUNT(DISTINCT id_order), now()
FROM orders.sale_entry
GROUP BY id_event, id_producer
ON CONFLICT ON CONSTRAINT pk_event_sales DO UPDATE
SET sold_quantity = EXCLUDED.sold_quantity, gross_amount = EXCLUDED.gross_amount, paid_orders = EXCLUDED.paid_orders, updated_at = EXCLUDED.updated_at
WHERE (orders.event_sales.sold_quantity, orders.event_sales.gross_amount, orders.event_sales.paid_orders)
      IS DISTINCT FROM (EXCLUDED.sold_quantity, EXCLUDED.gross_amount, EXCLUDED.paid_orders);

COMMIT;
