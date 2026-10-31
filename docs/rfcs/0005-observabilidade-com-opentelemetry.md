# RFC 0005: Observabilidade com OpenTelemetry e stack Grafana

- **Status**: Aceita
- **Data**: 2026-10-31
- **Requisito**: Observabilidade do serviço e funil de acompanhamento do cadastro de cliente

## Contexto

O usuário quer ver logs, traces e métricas em um único Grafana, acompanhar o funil de negócio do cadastro de cliente em um dashboard alimentado pelo Prometheus e acompanhar a saúde do serviço (memória, GC etc.). O projeto de referência `ntix` já usa essa stack.

## Decisão

| Dependência | Versão | Onde |
|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.20.1 | `src/WebApi` |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.20.1 | `src/WebApi` |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.20.1 | `src/WebApi` |
| `Microsoft.Extensions.Diagnostics` | 10.0.14 | `src/Application` |
| OpenTelemetry Collector contrib | 0.128.0 | `devops/docker-compose.yml` |
| Prometheus | v3.4.1 | `devops/docker-compose.yml` |
| Grafana Tempo | 2.8.1 | `devops/docker-compose.yml` |
| Grafana Loki | 3.5.1 | `devops/docker-compose.yml` |
| Grafana | 12.0.2 | `devops/docker-compose.yml` |

A aplicação envia os três sinais via OTLP ao collector, que distribui para Tempo, Prometheus e Loki. As versões das imagens são as do projeto de referência.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| `OpenTelemetry.Instrumentation.SqlClient` | Em falhas, copia a mensagem da `SqlException` para o span, e o erro 2601 traz o valor duplicado. Email ou CPF completos poderiam chegar ao Tempo, contrariando a regra de LGPD. O repositório abre spans manuais, sem valores |
| `OpenTelemetry.Instrumentation.Http` | O serviço não faz chamadas HTTP de saída |
| `OpenTelemetry.Instrumentation.Runtime` | O .NET 10 já publica o meter `System.Runtime` |
| Serilog, ou Prometheus raspando a aplicação | Exigiriam um caminho diferente para cada sinal, e o OTLP único é o que a referência usa |
| Serviços gerenciados de observabilidade | Custo e dependência de fornecedor, sem necessidade neste momento |

## Justificativa

OpenTelemetry é o padrão aberto e neutro de fornecedor, e os componentes são open source. Um único protocolo permite trocar o destino sem mexer na aplicação. A correlação entre métrica, trace e log fica pronta no Grafana.

## Consequências

- O ambiente local fica mais pesado, com cinco contêineres a mais.
- O exportador adiciona um custo pequeno por requisição, ainda não medido.
- O Grafana local usa acesso anônimo como Admin, publicado só em `127.0.0.1`. Isso não serve para produção.
- `ExecutionUser` chega aos logs e traces sem ofuscação, então não deve conter dado pessoal.

## Como reverter

Remover `AddTelemetry` do `Program`, os pacotes, os serviços do `docker-compose` e a pasta `devops/observability`. O `CreateCustomerDiagnostics` usa apenas APIs do .NET e não precisa mudar.
