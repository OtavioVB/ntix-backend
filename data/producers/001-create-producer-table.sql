-- Schema e tabela mínima do produtor, com as colunas obrigatórias do cadastro inicial.
-- Idempotente: pode ser executado mais de uma vez.

IF SCHEMA_ID('producers') IS NULL
    EXEC('CREATE SCHEMA producers');
GO

IF OBJECT_ID('producers.Producer', 'U') IS NULL
BEGIN
    CREATE TABLE producers.Producer (
        id_producer            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Producer PRIMARY KEY CLUSTERED,
        name                   NVARCHAR(256)    NOT NULL,
        email                  NVARCHAR(256)    NOT NULL,
        phone                  CHAR(11)         NOT NULL,
        producer_type          VARCHAR(16)      NOT NULL,
        slug                   VARCHAR(262)     NOT NULL,
        status                 VARCHAR(16)      NOT NULL,
        created_at             DATETIME2(7)     NOT NULL,
        created_correlation_id UNIQUEIDENTIFIER NOT NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Producer_Email' AND object_id = OBJECT_ID('producers.Producer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Producer_Email ON producers.Producer (email);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Producer_Slug' AND object_id = OBJECT_ID('producers.Producer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Producer_Slug ON producers.Producer (slug);
GO
