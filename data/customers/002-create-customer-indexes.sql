-- Índices únicos que garantem, no banco, que email, documento e slug identificam um único cliente.
-- Idempotente: pode ser executado mais de uma vez.

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_email ON customers.customer (email);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_document ON customers.customer (document);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_slug ON customers.customer (slug);
