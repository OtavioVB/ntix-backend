-- Documento opcional da produtora (CPF ou CNPJ), único quando informado.
-- Idempotente: pode ser executado mais de uma vez.

IF COL_LENGTH('producers.Producer', 'document') IS NULL
    ALTER TABLE producers.Producer ADD document VARCHAR(14) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Producer_Document' AND object_id = OBJECT_ID('producers.Producer'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Producer_Document ON producers.Producer (document) WHERE document IS NOT NULL;
GO
