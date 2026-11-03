-- Schema e tabelas do cadastro de produtoras e dos usuários delas.
-- Idempotente: pode ser executado mais de uma vez.

CREATE SCHEMA IF NOT EXISTS producers;

-- producer_type, document e custom_domain nascem nulos e passam a ser exigidos no envio para aprovação.
CREATE TABLE IF NOT EXISTS producers.producer (
    id_producer            UUID          NOT NULL CONSTRAINT pk_producer PRIMARY KEY,
    name                   VARCHAR(256)  NOT NULL,
    email                  VARCHAR(256)  NOT NULL,
    phone                  VARCHAR(16)   NOT NULL,
    slug                   VARCHAR(63)   NOT NULL,
    status                 VARCHAR(32)   NOT NULL CONSTRAINT ck_producer_status CHECK (status IN ('PENDING', 'PENDING_APPROVAL', 'ACTIVE')),
    producer_type          VARCHAR(32)   NULL,
    document               VARCHAR(14)   NULL,
    custom_domain          VARCHAR(253)  NULL,
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL
);

CREATE TABLE IF NOT EXISTS producers.producer_user (
    id_producer_user       UUID          NOT NULL CONSTRAINT pk_producer_user PRIMARY KEY,
    id_producer            UUID          NOT NULL CONSTRAINT fk_producer_user_producer REFERENCES producers.producer (id_producer),
    name                   VARCHAR(256)  NOT NULL,
    email                  VARCHAR(256)  NOT NULL,
    password_hash          BYTEA         NOT NULL CONSTRAINT ck_producer_user_password_hash_length CHECK (octet_length(password_hash) = 32),
    password_salt          BYTEA         NOT NULL CONSTRAINT ck_producer_user_password_salt_length CHECK (octet_length(password_salt) = 16),
    user_type              VARCHAR(32)   NOT NULL CONSTRAINT ck_producer_user_user_type CHECK (user_type IN ('ADMINISTRATOR')),
    status                 VARCHAR(32)   NOT NULL CONSTRAINT ck_producer_user_status CHECK (status IN ('PENDING', 'ACTIVE', 'BLOCKED')),
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL
);
