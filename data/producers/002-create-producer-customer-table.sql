-- Vínculo entre o cliente e cada produtor. Fica depois de customers na ordem de caminho, por causa das chaves estrangeiras.
-- Idempotente: pode ser executado mais de uma vez.

IF OBJECT_ID('producers.ProducerCustomer', 'U') IS NULL
BEGIN
    CREATE TABLE producers.ProducerCustomer (
        id_producer_customer   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProducerCustomer PRIMARY KEY CLUSTERED,
        producer_id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ProducerCustomer_Producer REFERENCES producers.Producer (id_producer),
        customer_id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ProducerCustomer_Customer REFERENCES customers.Customer (id_customer),
        created_at             DATETIME2(7)     NOT NULL,
        created_correlation_id UNIQUEIDENTIFIER NOT NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProducerCustomer_CustomerId_ProducerId' AND object_id = OBJECT_ID('producers.ProducerCustomer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_ProducerCustomer_CustomerId_ProducerId ON producers.ProducerCustomer (customer_id, producer_id);
GO
