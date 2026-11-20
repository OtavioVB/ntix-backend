-- Índices dos pagamentos (REQ7).
-- Idempotente: pode ser executado mais de uma vez.

-- A confirmação (REQ8) encontra o pagamento pelo id do fornecedor.
CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_provider_reference ON payments.payment (provider_reference);

CREATE INDEX IF NOT EXISTS ix_payment_id_order_created_at ON payments.payment (id_order, created_at DESC);

-- RN4, garantia final: um pedido tem no máximo um Pix PENDING. O vencido é gravado EXPIRED antes de gerar o novo.
CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_order_pending ON payments.payment (id_order) WHERE status = 'PENDING';
