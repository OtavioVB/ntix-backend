-- Eventos recebidos dos webhooks dos fornecedores (REQ8). Registro de auditoria e deduplicação (RN5).
-- Idempotente: pode ser executado mais de uma vez.

CREATE TABLE IF NOT EXISTS payments.payment_webhook_event (
    id_payment_webhook_event UUID         NOT NULL CONSTRAINT pk_payment_webhook_event PRIMARY KEY,
    provider_code            VARCHAR(32)  NOT NULL,
    provider_event_id        VARCHAR(128) NOT NULL,
    event_type               VARCHAR(128) NOT NULL,
    id_payment               UUID         NULL CONSTRAINT fk_payment_webhook_event_payment REFERENCES payments.payment (id_payment),
    outcome                  VARCHAR(32)  NOT NULL CONSTRAINT ck_payment_webhook_event_outcome
                                 CHECK (outcome IN ('CONFIRMED', 'ALREADY_PAID', 'IGNORED', 'NOT_PAID_AT_SOURCE', 'AMOUNT_MISMATCH', 'PAYMENT_NOT_FOUND')),
    received_at              TIMESTAMPTZ  NOT NULL,
    created_correlation_id   UUID         NOT NULL,
    CONSTRAINT ux_payment_webhook_event_provider_event UNIQUE (provider_code, provider_event_id)
);
