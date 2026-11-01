# Padrões de código

Extraídos dos projetos de referência indicados pelo usuário (`C:\Users\otavi\OneDrive\Desktop\Tech.Aquisition.Customers` e `C:\Users\otavi\OneDrive\Desktop\ntix`). Consulte os arquivos deles quando precisar de um exemplo completo, e avalie se a estrutura é necessária antes de trazê-la. No ntix-backend os projetos se chamam `WebApi`, `Application`, `Domain` e `Infra`, e o namespace segue a pasta (`Application.UseCases.CriarPedido.Inputs`).

## Geral

- Código e logs em inglês. Mensagens de notificação ao usuário em português.
- Namespace de arquivo (`namespace X;`), classes `sealed` por padrão, campos `_camelCase`, dependências por construtor.
- Argumentos nomeados em chamadas com vários parâmetros.
- Constantes em `SCREAMING_SNAKE_CASE`. Enums de negócio também, com valores explícitos começando em 1.

## Estrutura de pastas

```
Domain/
  BoundedContexts/<Nome>Context/{Entities,Enums,Events}/
  CrossCutting/{MethodResultContext,NotificationContext}/
  ValueObjects/<Nome>ValueObject.cs
Application/
  UseCases/
    Base/{AuditableInfoLogScopeExtensions.cs,Interfaces/IUseCase.cs}
    <Acao>/{<Acao>UseCase.cs,<Acao>Log.cs,Inputs/<Acao>UseCaseInput.cs}
    ServiceCollectionExtensions.cs
Infra/
  <Tecnologia>/Base/<Componente>/{<Componente>.cs,Interfaces/,ServiceCollectionExtensions.cs}
  <Tecnologia>/Base/Configuration/<Tecnologia>Configuration.cs
  <Tecnologia>/HealthChecks/
WebApi/
  Controllers/<Recurso>V1Controller.cs
  Controllers/Payloads/<Acao>V1Request.cs
Workers/
  Consumers/, Hubs/<Contexto>Context/
```

## Resultado e erros

Regras de fluxo não usam exceção: tudo retorna `MethodResult` ou `MethodResult<TOutput>`, com `Notification[]`.

- `Notification.Success|Information|Error(code, message)`.
- Código e mensagem são constantes privadas da classe que os emite: `<REGRA>_NOTIFICATION_CODE` e `<REGRA>_NOTIFICATION_MESSAGE`.
- `MethodResult.Factory(params MethodResult[])` agrega resultados e suas notificações.

## Value objects

`readonly struct <Nome>ValueObject`, construído por `Build`, que devolve o objeto válido ou inválido em vez de lançar exceção:

- `IsValid`, `Value` (lança se inválido) e `MethodResult` (sempre legível, com as notificações quando inválido).
- `Build` aceita `null` e o trata como vazio.
- Construtor privado, `MAX_LENGTH` público quando houver limite.
- Coleta as notificações numa lista, uma por regra violada, e guarda o valor normalizado só quando válido.
- `implicit operator` a partir de `string?`, para o controller passar o payload direto. Em campo opcional, use `cond ? (EmailValueObject?)null : EmailValueObject.Build(value)`: sem o cast, o `null` passa pela conversão implícita e vira um value object inválido, sem erro de compilação.
- Comportamento fica junto do dado e devolve outra instância, sem alterar a atual: `slug.BuildRandomVersion()`.
- Dado pessoal expõe `GetObfuscatedValue()`, com formato fixo: email `ma***@ntix.com`, CPF `***.982.247-**`. Em log, use sempre esse método, nunca `Value`.

### AuditableInfoValueObject

Agrega `CorrelationId`, `OriginService`, `ExecutionUser` e `RequestedAt`. `Build` recebe os valores brutos dos headers, reporta todos os problemas de uma vez e expõe as chaves dos headers e o formato como constantes. Não gera `CorrelationId` no servidor: a requisição sem ele é recusada. `RequestedAt` é UTC em `yyyy-MM-dd'T'HH:mm:ss.fff'Z'`, com tolerância de 5 minutos para o futuro.

## Entidades

`sealed record` em `BoundedContexts/<Nome>Context/Entities`, cujas propriedades são value objects.

## Casos de uso

- `IUseCase<TInput>`, com `ExecuteAsync(input, auditableInfo, cancellationToken)`.
- O caso de uso orquestra: abre o escopo de log auditável, valida input e `auditableInfo` juntos, abre a transação (só se gravar ou precisar de lock), chama domínio e repositório, faz commit só quando a regra permite, registra os logs e devolve a notificação.
- Toda recusa de negócio depois de abrir a transação faz rollback. O `catch` faz rollback em qualquer exceção, sem deixar uma falha do rollback esconder a original, registra o log de erro e o desfecho e relança a exceção para o controller; cancelamento não é relançado: depois do rollback, devolve uma notificação de cancelamento.
- Input: `sealed record` com value objects dos dados do negócio, construtor privado, `static Create(...)` e `GetInputValidation()`, que agrega os `MethodResult` deles. Os dados auditáveis não entram no input.

## Logs

- Cada caso de uso tem seu `<Acao>Log`: `internal static partial class` com `[LoggerMessage]`, `EventId` explícito e mensagem que descreve o fato de negócio no formato `[{MethodName}] Customer authentication has been failed. Input = {@Input}.`.
- A assinatura recebe `string methodName` e `object input`. Quem chama passa `nameof(<Classe>)` e um objeto anônimo: `input: new { CustomerId = customer.Id.Value, Email = customer.Email.GetObfuscatedValue() }`. Log de exceção leva no `Input` só o tipo dela, e a exceção vai no parâmetro `exception`.
- Sucesso em `Information`, recusa de negócio em `Warning`, falha inesperada em `Error`.
- Os campos auditáveis não se repetem nas mensagens: `logger.BeginAuditableScope(auditableInfo)` abre um escopo com eles, e `Logging:Console:IncludeScopes` fica ligado no `appsettings`.
- Só `GetObfuscatedValue()` para dado pessoal, e nada que o revele indiretamente, como o nome completo ou o slug.

## Métricas e traces

- Cada caso de uso tem seu `<Acao>Diagnostics`, com `ActivitySource` e `Meter` chamados `Ntix.<Dominio>`, cobertos pelo curinga `Ntix.*` do `TelemetryExtensions`.
- Funil: `ntix.<dominio>.<fluxo>.requests` na entrada e `ntix.<dominio>.<fluxo>.outcomes` com a tag `outcome` em cada desfecho. Tags sempre de baixa cardinalidade.
- Inicie os contadores com `Add(0)` no construtor. O `increase()` do Prometheus ignora a primeira amostra de uma série, e sem isso o primeiro evento se perderia.
- Em span de erro, registre o tipo da exceção, nunca a mensagem. Span de banco é manual no repositório, sem valores de parâmetros.
- Dashboards ficam em `devops/observability/grafana/dashboards` e são provisionados automaticamente.

## Endpoints

- `sealed` controller com `[ApiController]` e `[Route("api/v1/<recurso>")]`.
- Caso de uso injetado no método: `[FromServices] IUseCase<Input> useCase`.
- Headers auditáveis com `[FromHeader]`, convertidos em `AuditableInfoValueObject.Build(...)` e passados ao caso de uso.
- Payload `sealed record <Acao>V1Request` em `Controllers/Payloads`.
- O controller só converte payload em input, chama o caso de uso e traduz: notificação de cancelamento vira 499; `IsError` vira `BadRequest(result.Notifications)`; sucesso vira `Ok(result.Notifications)`. Exceção não é capturada no controller e vira 500.
- `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` fica ligado no `Program`: campo ausente chega ao value object e vira notificação, em vez de `ProblemDetails` do framework.

## Injeção de dependência e configuração

- Cada módulo expõe `ServiceCollectionExtensions` com `Add<Modulo>Configuration(this IServiceCollection services, IConfiguration configuration)`, retornando `services`.
- O `Program` da camada de apresentação compõe esses métodos.
- Configuração tipada: `sealed record <Tecnologia>Configuration`, lida com `configuration.GetRequiredSection("<Tecnologia>Configuration").Get<T>()!`.

## Persistência (SQL nativo + SQL Server)

```
data/<schema>/NNN-descricao.sql
Infra/SqlServer/
  Base/{Configuration,UnitOfWork}/
  Repositories/<Agregado>s/
    <Agregado>Repository.cs, ServiceCollectionExtensions.cs
    {Interfaces,Models,Enums}/
```

- Scripts idempotentes, aplicados em ordem de caminho por `devops/sqlserver/init-database.sh` e pelos testes. Tabela em `<schema>.<Entidade>`, colunas em `snake_case`, chave `id_<entidade>`.
- `IUnitOfWork` tem `BeginTransactionAsync`, `CommitAsync` e `RollbackAsync`. Quem grava cria comandos com `SqlServerUnitOfWork.CreateCommand`, sempre dentro da transação. Descartar o `UnitOfWork` sem commit desfaz a transação. Consulta sem necessidade de lock, fora de um fluxo transacional, abre a própria conexão no repositório, com `SqlServerConfiguration`, e o caso de uso não recebe `IUnitOfWork`.
- Parâmetros sempre tipados, com o tipo e o tamanho da coluna (`SqlDbType.VarChar, 262`), para evitar conversão implícita que impede o uso do índice. Nada de `LIKE` quando uma consulta exata resolve.
- `<Agregado>Model` é um `sealed record` de tipos primitivos. A `Application` faz a conversão a partir da entidade.
- Unicidade é garantida por índice único nomeado (`UX_<Tabela>_<Coluna>`). A consulta prévia só antecipa a resposta. O repositório traduz o erro 2601 do SQL Server para um enum de resultado, em vez de propagar a exceção. Esse erro desfaz só a instrução, então a transação continua utilizável.
- Connection string nunca fica em `appsettings`: vem de `SqlServerConfiguration__ConnectionString` ou de user secrets.

## Infraestrutura

- Toda integração tem interface em `Interfaces/`, e é ela que o caso de uso consome.
- Health checks implementam `IHealthCheck`, expostos em `/health/ready` e `/health/live` nos workers.
- Mensageria (`EventBase<T>`, `ConsumerHandlerBase<TEvent>`, publisher, DLQ opcional) e `NAMESPACE` como variável de ambiente de origem: veja o projeto de referência quando o requisito precisar. Use `ConsumerHandlerBase` como base de um consumer em `Workers/Consumers`.

## Não copiar da referência

Defeitos encontrados nela, que não devem ser repetidos no ntix:

- **Validação nunca chega ao usuário.** `MethodResult` de um value object inválido lança exceção, então `GetInputValidation()` cai no `catch` genérico do caso de uso. Além disso, `MethodResult.IsValid` é sempre `true`, e o `if (!inputValidationResult.IsValid)` nunca dispara. No ntix: `MethodResult` do value object sempre legível e verificação por `IsError`.
- **`MethodResult<TOutput>.Success/Error` descartam o `output`** ao chamar `Factory` sem repassá-lo. `Output` sai sempre `default`.
- **`Build` não trata `null`** (`email.Length`, `firstName.Length`), e entrada vazia em e-mail gera também erro de formato. Em `FirstNameValueObject`, vazio reutiliza a notificação de tamanho excedido: cada regra precisa da própria notificação.
- **Evento de domínio com namespace errado**: `AquisitionCustomerRequestedEvent` está em `Domain`, com namespace `Workers.Consumers`.
- **Feature flag**: `FeaturedUseCase` e `FeatureManagement` não foram pedidos no ntix-backend. Não traga essa camada sem o usuário pedir.
- **Dados pessoais em log**: o `catch` do caso de uso registra nome e sobrenome. Registre identificadores e dados ofuscados.
- **`scope` não descartado** em `ConsumerHandlerBase`, e header `"X-Event-Id"` escrito à mão em vez de `EventTag.Id`.
- **Teste de carga com URL e token fixos no script**: leia de variáveis de ambiente.
- **Chaves privadas versionadas** em `devops/rabbitmq/ssl`: não versione segredos.
