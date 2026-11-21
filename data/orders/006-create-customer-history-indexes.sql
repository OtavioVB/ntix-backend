-- Índices das listas do comprador (REQ13). Garantidos aqui de forma idempotente, mesmo que o REQ6 e o REQ8 já tenham criado.
-- Idempotente: pode ser executado mais de uma vez.

-- Meus pedidos: pedidos do cliente do mais recente ao mais antigo (RN3).
CREATE INDEX IF NOT EXISTS ix_orders_id_customer_created_at ON orders.orders (id_customer, created_at DESC);

-- Meus ingressos: ingressos do cliente por evento (REQ8), usados pela junção com o evento.
CREATE INDEX IF NOT EXISTS ix_ticket_id_customer_id_event ON orders.ticket (id_customer, id_event);
