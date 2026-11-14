-- Schema e tabelas das integrações de pagamento do produtor (REQ4).
-- Idempotente: pode ser executado mais de uma vez.

CREATE SCHEMA IF NOT EXISTS payments;

-- Sem chave estrangeira para producers.producer: os scripts são aplicados em ordem de caminho, e data/payments
-- vem antes de data/producers. O id_producer só é gravado pela API, a partir do produtor lido pelo slug do token.

-- Catálogo de fornecedores, mantido pela Ntix por script (003-seed-payment-providers.sql). O produtor não cria fornecedor.
CREATE TABLE IF NOT EXISTS payments.provider (
    id_provider            UUID          NOT NULL CONSTRAINT pk_provider PRIMARY KEY,
    code                   VARCHAR(32)   NOT NULL CONSTRAINT ux_provider_code UNIQUE,
    name                   VARCHAR(64)   NOT NULL,
    status                 VARCHAR(32)   NOT NULL CONSTRAINT ck_provider_status CHECK (status IN ('ENABLED', 'DISABLED'))
);

CREATE TABLE IF NOT EXISTS payments.provider_payment_method (
    id_provider            UUID          NOT NULL CONSTRAINT fk_provider_payment_method_provider REFERENCES payments.provider (id_provider),
    payment_method         VARCHAR(32)   NOT NULL CONSTRAINT ck_provider_payment_method_payment_method CHECK (payment_method IN ('PIX', 'BOLETO', 'CREDIT_CARD')),
    CONSTRAINT pk_provider_payment_method PRIMARY KEY (id_provider, payment_method)
);

CREATE TABLE IF NOT EXISTS payments.provider_credential_key (
    id_provider            UUID          NOT NULL CONSTRAINT fk_provider_credential_key_provider REFERENCES payments.provider (id_provider),
    credential_key         VARCHAR(64)   NOT NULL,
    is_secret              BOOLEAN       NOT NULL,
    is_required            BOOLEAN       NOT NULL,
    CONSTRAINT pk_provider_credential_key PRIMARY KEY (id_provider, credential_key)
);

-- Integração não é excluída, só inativada (RN11): o pagamento que ela processou continua apontando para ela.
CREATE TABLE IF NOT EXISTS payments.producer_integration (
    id_producer_integration UUID         NOT NULL CONSTRAINT pk_producer_integration PRIMARY KEY,
    id_producer            UUID          NOT NULL,
    id_provider            UUID          NOT NULL CONSTRAINT fk_producer_integration_provider REFERENCES payments.provider (id_provider),
    name                   VARCHAR(64)   NOT NULL,
    status                 VARCHAR(32)   NOT NULL CONSTRAINT ck_producer_integration_status CHECK (status IN ('ACTIVE', 'INACTIVE')),
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL,
    updated_at             TIMESTAMPTZ   NOT NULL,
    updated_correlation_id UUID          NOT NULL,
    -- Alvo da chave estrangeira composta da rota: a rota só aponta para integração do mesmo produtor (RN1).
    CONSTRAINT ux_producer_integration_id_producer UNIQUE (id_producer_integration, id_producer)
);

-- encrypted_value: nonce (12 bytes) + tag (16 bytes) + texto cifrado (AES-256-GCM). Nunca o valor em claro (RN9).
CREATE TABLE IF NOT EXISTS payments.producer_integration_credential (
    id_producer_integration UUID         NOT NULL CONSTRAINT fk_producer_integration_credential_producer_integration REFERENCES payments.producer_integration (id_producer_integration),
    credential_key         VARCHAR(64)   NOT NULL,
    encrypted_value        BYTEA         NOT NULL CONSTRAINT ck_producer_integration_credential_encrypted_value_length CHECK (octet_length(encrypted_value) > 28),
    value_hint             VARCHAR(4)    NULL,
    updated_at             TIMESTAMPTZ   NOT NULL,
    updated_correlation_id UUID          NOT NULL,
    CONSTRAINT pk_producer_integration_credential PRIMARY KEY (id_producer_integration, credential_key)
);

CREATE TABLE IF NOT EXISTS payments.producer_payment_route (
    id_producer_payment_route UUID       NOT NULL CONSTRAINT pk_producer_payment_route PRIMARY KEY,
    id_producer            UUID          NOT NULL,
    payment_method         VARCHAR(32)   NOT NULL CONSTRAINT ck_producer_payment_route_payment_method CHECK (payment_method IN ('PIX', 'BOLETO', 'CREDIT_CARD')),
    priority               SMALLINT      NOT NULL CONSTRAINT ck_producer_payment_route_priority CHECK (priority >= 1),
    id_producer_integration UUID         NOT NULL,
    updated_at             TIMESTAMPTZ   NOT NULL,
    updated_correlation_id UUID          NOT NULL,
    CONSTRAINT fk_producer_payment_route_producer_integration FOREIGN KEY (id_producer_integration, id_producer)
        REFERENCES payments.producer_integration (id_producer_integration, id_producer)
);
