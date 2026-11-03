-- Schema e tabela do cadastro de clientes.
-- Idempotente: pode ser executado mais de uma vez.

CREATE SCHEMA IF NOT EXISTS customers;

CREATE TABLE IF NOT EXISTS customers.customer (
    id_customer            UUID          NOT NULL CONSTRAINT pk_customer PRIMARY KEY,
    full_name              VARCHAR(256)  NOT NULL,
    email                  VARCHAR(256)  NOT NULL,
    document               CHAR(11)      NOT NULL,
    slug                   VARCHAR(262)  NOT NULL,
    password_hash          BYTEA         NOT NULL CONSTRAINT ck_customer_password_hash_length CHECK (octet_length(password_hash) = 32),
    password_salt          BYTEA         NOT NULL CONSTRAINT ck_customer_password_salt_length CHECK (octet_length(password_salt) = 16),
    phone_number           CHAR(11)      NULL,
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL
);
