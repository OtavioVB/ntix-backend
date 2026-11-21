-- Índices dos ingressos (REQ8).
-- Idempotente: pode ser executado mais de uma vez.

-- O código é o conteúdo do QR: único no sistema inteiro (RN7).
CREATE UNIQUE INDEX IF NOT EXISTS ux_ticket_code ON orders.ticket (code);

-- Consulta da conta do cliente e check-in por evento (REQ13).
CREATE INDEX IF NOT EXISTS ix_ticket_id_customer_id_event ON orders.ticket (id_customer, id_event);

CREATE INDEX IF NOT EXISTS ix_ticket_id_order ON orders.ticket (id_order);
