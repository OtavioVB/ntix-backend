-- Verificação de email do usuário do produtor. Guarda só o hash do token.
-- Idempotente: pode ser executado mais de uma vez.

IF OBJECT_ID('producers.EmailVerification', 'U') IS NULL
BEGIN
    CREATE TABLE producers.EmailVerification (
        id_email_verification  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EmailVerification PRIMARY KEY CLUSTERED,
        producer_user_id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_EmailVerification_ProducerUser REFERENCES producers.ProducerUser (id_producer_user),
        token_hash             BINARY(32)       NOT NULL,
        expires_at             DATETIME2(7)     NOT NULL,
        created_at             DATETIME2(7)     NOT NULL,
        created_correlation_id UNIQUEIDENTIFIER NOT NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_EmailVerification_TokenHash' AND object_id = OBJECT_ID('producers.EmailVerification'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_EmailVerification_TokenHash ON producers.EmailVerification (token_hash);
GO
