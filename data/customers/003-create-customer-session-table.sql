-- Sessões do cliente (REQ12): um refresh token por linha, gravado só como SHA-256, como as do produtor (REQ5).
-- Cada renovação revoga a linha usada e grava outra da mesma família (id_family), que agrupa as renovações de um login.
-- Idempotente: pode ser executado mais de uma vez.

CREATE TABLE IF NOT EXISTS customers.customer_session (
    id_customer_session      UUID         NOT NULL CONSTRAINT pk_customer_session PRIMARY KEY,
    id_customer              UUID         NOT NULL CONSTRAINT fk_customer_session_customer REFERENCES customers.customer (id_customer),
    id_family                UUID         NOT NULL,
    refresh_token_hash       BYTEA        NOT NULL CONSTRAINT ck_customer_session_refresh_token_hash_length CHECK (octet_length(refresh_token_hash) = 32),
    expires_at               TIMESTAMPTZ  NOT NULL,
    created_at               TIMESTAMPTZ  NOT NULL,
    created_correlation_id   UUID         NOT NULL,
    revoked_at               TIMESTAMPTZ  NULL,
    id_replaced_by           UUID         NULL CONSTRAINT fk_customer_session_replaced_by REFERENCES customers.customer_session (id_customer_session),
    -- Substituída é sempre revogada: a renovação grava as duas colunas juntas.
    CONSTRAINT ck_customer_session_replaced_is_revoked CHECK (id_replaced_by IS NULL OR revoked_at IS NOT NULL)
);
