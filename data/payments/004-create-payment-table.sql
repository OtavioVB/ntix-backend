-- Pagamentos dos pedidos (REQ7). Cada linha é uma cobrança Pix gerada no fornecedor da integração que a processou.
-- Idempotente: pode ser executado mais de uma vez.

CREATE TABLE IF NOT EXISTS payments.payment (
    id_payment              UUID          NOT NULL CONSTRAINT pk_payment PRIMARY KEY,
    id_order                UUID          NOT NULL CONSTRAINT fk_payment_order REFERENCES orders.orders (id_order),
    id_producer             UUID          NOT NULL,
    id_producer_integration UUID          NOT NULL,
    payment_method          VARCHAR(32)   NOT NULL CONSTRAINT ck_payment_payment_method CHECK (payment_method IN ('PIX')),
    amount                  NUMERIC(18,2) NOT NULL CONSTRAINT ck_payment_amount CHECK (amount > 0),
    status                  VARCHAR(32)   NOT NULL CONSTRAINT ck_payment_status CHECK (status IN ('PENDING', 'PAID', 'EXPIRED')),
    provider_reference      VARCHAR(64)   NOT NULL,
    pix_copy_paste          TEXT          NOT NULL,
    pix_qr_code_image       TEXT          NOT NULL,
    expires_at              TIMESTAMPTZ   NOT NULL,
    paid_at                 TIMESTAMPTZ   NULL,
    created_at              TIMESTAMPTZ   NOT NULL,
    created_correlation_id  UUID          NOT NULL,
    updated_at              TIMESTAMPTZ   NOT NULL,
    updated_correlation_id  UUID          NOT NULL,
    -- A integração que processou é do mesmo produtor do pedido; ela nunca é excluída, só inativada (REQ4, RN11).
    CONSTRAINT fk_payment_producer_integration FOREIGN KEY (id_producer_integration, id_producer)
        REFERENCES payments.producer_integration (id_producer_integration, id_producer)
);
