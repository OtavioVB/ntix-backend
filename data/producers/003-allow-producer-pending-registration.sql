-- Cadastro progressivo: o tipo só é exigido em PENDING_APPROVAL, e o telefone passa ao formato E.164.
-- Idempotente: pode ser executado mais de uma vez.

ALTER TABLE producers.Producer ALTER COLUMN producer_type VARCHAR(16) NULL;
GO

ALTER TABLE producers.Producer ALTER COLUMN phone VARCHAR(16) NOT NULL;
GO
