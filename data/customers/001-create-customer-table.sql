-- Schema e tabela do cadastro de clientes.
-- Idempotente: pode ser executado mais de uma vez.

IF SCHEMA_ID('customers') IS NULL
    EXEC('CREATE SCHEMA customers');
GO

IF OBJECT_ID('customers.Customer', 'U') IS NULL
BEGIN
    CREATE TABLE customers.Customer (
        id_customer            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Customer PRIMARY KEY CLUSTERED,
        full_name              NVARCHAR(256)    NOT NULL,
        email                  NVARCHAR(256)    NOT NULL,
        document               CHAR(11)         NOT NULL,
        slug                   VARCHAR(262)     NOT NULL,
        created_at             DATETIME2(7)     NOT NULL,
        created_correlation_id UNIQUEIDENTIFIER NOT NULL
    );
END;
GO
