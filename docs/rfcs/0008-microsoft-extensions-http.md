# RFC 0008: Microsoft.Extensions.Http para chamar o fornecedor de pagamento

- **Status**: Aceita
- **Data**: 2026-11-20
- **Requisito**: REQ7 - Pagamento de pedidos com Pix

## Contexto

O REQ7 faz a Ntix chamar a API do AbacatePay (`POST /v2/transparents/create`) com a chave do produtor, com timeout por tentativa e fallback entre integrações. A `Infra` precisa de um `HttpClient` configurável por injeção de dependência, sem criar `HttpClient` à mão a cada chamada (esgota sockets) nem manter um estático (ignora mudança de DNS).

## Decisão

| Dependência | Versão | Onde |
|---|---|---|
| `Microsoft.Extensions.Http` | 10.0.14 | `src/Infra` |

Ela traz `IHttpClientFactory` e `AddHttpClient<TInterface, TImplementation>()`, com o `HttpClient` tipado do `AbacatePayClient`, `BaseAddress` e timeout vindos de `AbacatePayConfiguration`.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| `new HttpClient()` por chamada | Esgota sockets sob carga |
| `HttpClient` estático em `Infra` | Ignora a troca de DNS e foge do padrão de registro por `ServiceCollectionExtensions` |
| Polly / resiliência | O fallback é entre integrações, não retentativa na mesma, e cabe num laço com `CancellationTokenSource` por tentativa (REQ7, RN7). Fora do escopo |
| SDK do AbacatePay | Não há SDK oficial para .NET, e a chamada é um único endpoint |

## Justificativa

Pacote oficial da Microsoft, licença MIT, da mesma família das abstrações da RFC 0002 e com a mesma versão. É o mecanismo padrão do .NET para clientes HTTP.

## Consequências

A versão acompanha as atualizações do .NET 10. A VPS precisa de saída HTTPS para `api.abacatepay.com`. A esteira não chama o fornecedor: os testes substituem o `IPixChargeClient`.

## Como reverter

Remover o pacote e o `AddHttpClient`, e trocar o `AbacatePayClient` por outro cliente HTTP. O restante da aplicação depende só de `IPixChargeClient`.
