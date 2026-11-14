-- Catálogo de fornecedores de pagamento (RN3). Idempotente: o que já existe não é alterado.
--
-- AbacatePay (https://docs.abacatepay.com/pages/reference/introduction):
--   - API_KEY: chave da API v2, enviada como "Authorization: Bearer <chave>". O ambiente vem da chave
--     (abc_dev_... em teste, abc_prod_... em produção). Secreta e obrigatória.
--   - WEBHOOK_SECRET: segredo escolhido pelo produtor ao criar o webhook, devolvido pelo AbacatePay em
--     X-Webhook-Secret. Secreto e opcional até o REQ8 (efetivação por webhook).
--   - Métodos: PIX, BOLETO e CARD (CREDIT_CARD na Ntix; a tradução fica no adaptador do REQ7).

INSERT INTO payments.provider (id_provider, code, name, status)
VALUES ('0b6e7c1e-5d0a-4a43-9b8e-2f6a3c1d9e01', 'ABACATEPAY', 'AbacatePay', 'ENABLED')
ON CONFLICT DO NOTHING;

INSERT INTO payments.provider_payment_method (id_provider, payment_method)
VALUES
    ('0b6e7c1e-5d0a-4a43-9b8e-2f6a3c1d9e01', 'PIX'),
    ('0b6e7c1e-5d0a-4a43-9b8e-2f6a3c1d9e01', 'BOLETO'),
    ('0b6e7c1e-5d0a-4a43-9b8e-2f6a3c1d9e01', 'CREDIT_CARD')
ON CONFLICT DO NOTHING;

INSERT INTO payments.provider_credential_key (id_provider, credential_key, is_secret, is_required)
VALUES
    ('0b6e7c1e-5d0a-4a43-9b8e-2f6a3c1d9e01', 'API_KEY', TRUE, TRUE),
    ('0b6e7c1e-5d0a-4a43-9b8e-2f6a3c1d9e01', 'WEBHOOK_SECRET', TRUE, FALSE)
ON CONFLICT DO NOTHING;
