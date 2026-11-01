-- Usuário do produtor. Idempotente: pode ser executado mais de uma vez.

IF OBJECT_ID('producers.ProducerUser', 'U') IS NULL
BEGIN
    CREATE TABLE producers.ProducerUser (
        id_producer_user       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProducerUser PRIMARY KEY CLUSTERED,
        producer_id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ProducerUser_Producer REFERENCES producers.Producer (id_producer),
        name                   NVARCHAR(256)    NOT NULL,
        email                  NVARCHAR(256)    NOT NULL,
        password_hash          BINARY(32)       NOT NULL,
        password_salt          BINARY(16)       NOT NULL,
        user_type              VARCHAR(16)      NOT NULL,
        status                 VARCHAR(16)      NOT NULL,
        created_at             DATETIME2(7)     NOT NULL,
        created_correlation_id UNIQUEIDENTIFIER NOT NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProducerUser_Email' AND object_id = OBJECT_ID('producers.ProducerUser'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_ProducerUser_Email ON producers.ProducerUser (email);
GO
