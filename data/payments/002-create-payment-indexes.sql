-- Índices das integrações e das rotas de pagamento do produtor.
-- Idempotente: pode ser executado mais de uma vez.

-- RN5: nome da integração único por produtor.
CREATE UNIQUE INDEX IF NOT EXISTS ux_producer_integration_name ON payments.producer_integration (id_producer, name);

-- RN6: cada prioridade e cada integração aparecem uma vez no método.
CREATE UNIQUE INDEX IF NOT EXISTS ux_producer_payment_route_priority ON payments.producer_payment_route (id_producer, payment_method, priority);

CREATE UNIQUE INDEX IF NOT EXISTS ux_producer_payment_route_integration ON payments.producer_payment_route (id_producer, payment_method, id_producer_integration);

CREATE INDEX IF NOT EXISTS ix_producer_integration_id_producer ON payments.producer_integration (id_producer);
