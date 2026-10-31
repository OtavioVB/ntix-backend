-- Índices únicos que garantem, no banco, que email, documento e slug identificam um único cliente.
-- Idempotente: pode ser executado mais de uma vez.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Customer_Email' AND object_id = OBJECT_ID('customers.Customer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Customer_Email ON customers.Customer (email);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Customer_Document' AND object_id = OBJECT_ID('customers.Customer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Customer_Document ON customers.Customer (document);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Customer_Slug' AND object_id = OBJECT_ID('customers.Customer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Customer_Slug ON customers.Customer (slug);
GO
