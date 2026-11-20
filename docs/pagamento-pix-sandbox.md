# Roteiro manual: Pix no sandbox do AbacatePay (REQ7)

A esteira nunca chama o AbacatePay: os testes substituem o `IPixChargeClient`. Este roteiro valida a integração de verdade, uma vez por mudança no adaptador, com uma conta de **desenvolvimento** do AbacatePay (chave `abc_dev_...`). Nada aqui cobra dinheiro real.

## Pré-requisitos

- API no ar (`dotnet run --project src/WebApi`, perfil `https`) com saída HTTPS para `api.abacatepay.com`.
- Uma conta AbacatePay em modo de desenvolvimento e a chave `abc_dev_...` dela.
- Um produtor com a integração `ABACATEPAY` (campo `API_KEY` com a chave de desenvolvimento) e a rota `PIX` apontando para ela (REQ4), um evento publicado com um lote em venda (REQ9, REQ10) e um cliente cadastrado (REQ12).

## Passo a passo

1. Entre como o cliente (`POST /api/v1/customers/sessions`) e guarde o `accessToken`.
2. Crie o pedido: `POST /api/v1/orders` com o slug do evento e 1 ou 2 ingressos. Guarde o `code` (`NX...`) e confira o `expiresAt` (15 minutos).
3. `GET /api/v1/orders/{code}/payment-methods` deve listar só `PIX`.
4. `POST /api/v1/orders/{code}/payments` com `{ "method": "PIX" }`:
   - **201**, com `pix.copyPasteCode` e `pix.qrCodeImage`;
   - o `expiresAt` do Pix vale 2 minutos a menos que o do pedido.
5. Repita o `POST`: **200** com o mesmo `id` e o mesmo `copyPasteCode`. No painel do AbacatePay deve existir **uma** cobrança só.
6. Simule o pagamento no sandbox: `POST https://api.abacatepay.com/v2/transparents/simulate-payment`, com `Authorization: Bearer abc_dev_...` e o `id` da cobrança (`pix_char_...`, a coluna `provider_reference` de `payments.payment`).
7. A confirmação do pedido (status `PAID`, ingressos) é o REQ8: aqui só se confere que a cobrança foi gerada e que o `GET /api/v1/orders/{code}` traz o bloco `payment` com o Pix.

## O que conferir nos sinais

- Log `Pix {PaymentId} gerado para o pedido ...` (evento `7101`), sem `API_KEY`, `brCode`, CPF nem e-mail.
- Dashboard **Ntix Pagamentos**: `created` sobe em "Geração do Pix", e as chamadas ao fornecedor aparecem como `ABACATEPAY created`.
- Segundo clique: `reused` sobe, e as chamadas ao fornecedor não.

## Fallback e falhas (opcional)

Para ver o fallback, cadastre uma segunda integração, na rota depois da primeira, e deixe a primeira com uma chave inválida: o `POST` deve devolver 201 pela segunda, com `ntix_payments_fallback_used_total` somando 1 e uma chamada `http_401` da primeira no dashboard. Para ver a recusa de todas, deixe as duas inválidas: 503 com `PAYMENT_PROVIDERS_UNAVAILABLE`, sem gravar pagamento.

## Pontos a confirmar no sandbox

- ~~Se o AbacatePay aceita a criação sem `cellphone`~~: **não aceita**. O campo é obrigatório (texto), sem validação de formato; vazio é aceito, ausente ou nulo dão 422. A Ntix envia `""` quando o cliente não tem celular. Conferir que a conta de produção aceita o vazio.
- A validade mínima do Pix: o sandbox aceitou `expiresIn` de 60, 120 e 300 s; a documentação diverge (60 s no sandbox, 300 s na criação) e a Ntix mantém 300 s por prudência.
- O formato do celular que o fornecedor aceita (`(81) 99999-0000` é o que a Ntix envia).

## Contrato automatizado (na máquina, fora da esteira)

`AbacatePaySandboxTests` exercita o `AbacatePayClient` real contra o sandbox: Pix com e sem celular, descrição com acentos e emoji, valor mínimo, valor abaixo do mínimo, CPF inválido e chave inválida. Roda só com a chave de desenvolvimento no ambiente da execução, sem gravá-la em arquivo:

```powershell
$env:ABACATEPAY_SANDBOX_API_KEY = "abc_dev_..."; dotnet test tsts/IntegratedTests --filter "FullyQualifiedName~AbacatePaySandboxTests"
```

Sem a variável, os testes aparecem como ignorados. Rode-os sempre que mudar o corpo da requisição ou a leitura da resposta.

## Diagnóstico de uma recusa

O log `7102` ("Tentativa N falhou na integração ...") traz o motivo curto (`http_4xx`, `http_401`, `timeout`...) e, em recusa 4xx, o texto que o fornecedor devolveu em `error`. Exemplos reais do sandbox: `Property 'data.customer.cellphone' is missing` (campo obrigatório ausente), `Expected number to be greater or equal to 100` (valor abaixo de R$ 1,00), `Invalid taxId` (CPF inválido) e `Disallowed character in description` (caractere não permitido na descrição).
