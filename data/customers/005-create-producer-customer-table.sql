-- Vínculo comprador-produtor (REQ6, RN13): criado no primeiro pedido do cliente com o produtor, a base de compradores do produtor.
-- Sem chave estrangeira para producers.producer: os scripts são aplicados em ordem de caminho, e data/customers vem antes de data/producers.
-- O id_producer só é gravado pela API, a partir do evento do pedido.
-- Idempotente: pode ser executado mais de uma vez.

CREATE TABLE IF NOT EXISTS customers.producer_customer (
    id_producer            UUID        NOT NULL,
    id_customer            UUID        NOT NULL CONSTRAINT fk_producer_customer_customer REFERENCES customers.customer (id_customer),
    created_at             TIMESTAMPTZ NOT NULL,
    created_correlation_id UUID        NOT NULL,
    CONSTRAINT pk_producer_customer PRIMARY KEY (id_producer, id_customer)
);
