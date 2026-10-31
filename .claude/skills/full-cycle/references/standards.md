# Padrões do ntix-backend

Fonte da verdade dos padrões. Registre aqui apenas o que o usuário estipulou ou o que o repositório já mostra, nunca uma preferência sua. Ao receber uma instrução nova, atualize este arquivo no mesmo turno.

## Estipulado pelo usuário

### Papéis
- O usuário define regras de negócio e definições de tecnologia. Claude implementa de ponta a ponta.

### Arquitetura
Clean Architecture, seguida sempre à risca, com esta direção de referências:

```
Presentation (WebApi, Workers, Console etc.) -> Application -> Infra
                                                            -> Domain
```

| Camada | Conteúdo |
|---|---|
| Presentation | Apresentação: `WebApi`, `Workers`, `Console` etc. |
| Application | Casos de uso: as regras de negócio de orquestração e a chamada de serviços de domínio, de infraestrutura e externos |
| Infra | Configurações de dependências externas, repositórios etc. |
| Domain | Informações de domínio, objetos de valor, entidades e as regras de cada conceito do domínio |

Regra que pertence a um conceito do domínio, e que outras regras de negócio podem reutilizar, fica no domínio, não no caso de uso. Exemplo: gerar a versão do slug é do `SlugValueObject`; o caso de uso só verifica na base se ela está livre e tenta gravar.

Toda nova aplicação de apresentação referencia apenas `Application`. `Domain` e `Infra` chegam a ela por transitividade.

`Infra` não referencia `Domain`: o repositório troca dados por um modelo de persistência próprio, com tipos primitivos, e a `Application` converte entre a entidade e esse modelo.

### Persistência
- SQL Server ([RFC 0001](../../../../docs/rfcs/0001-sql-server-como-banco-de-dados.md)), acessado com SQL nativo, sem EF Core, porque performance é critério de implementação ([RFC 0004](../../../../docs/rfcs/0004-sql-nativo-no-lugar-do-ef-core.md)).
- Tabelas e índices ficam em scripts na pasta `data/`.
- Unit of Work: o caso de uso faz rollback em qualquer exceção e só faz commit quando a regra de negócio permite a criação.
- Evite consultas com `LIKE`. Prefira consultas exatas sobre índices.

### Observabilidade
- Logs, traces e métricas via OpenTelemetry, visualizados no Grafana ([RFC 0005](../../../../docs/rfcs/0005-observabilidade-com-opentelemetry.md)).
- Caso de uso de negócio tem funil em métricas do Prometheus e dashboard próprio: um contador de solicitações e um contador de desfechos.
- Nenhum sinal pode expor dado pessoal completo, nem em mensagem de exceção copiada para um span.

### Integração contínua
- GitHub Actions: build, testes unitários e integrados em paralelo e, depois, testes funcionais.
- Valores sensíveis da esteira vêm de secrets do repositório, nunca do arquivo do workflow.

### Nomes
- Use o nome mais simples que descreva o negócio: `Customer`, não `CustomerAccount`.

### Regras de negócio
- Não invente regra que não foi estipulada, mesmo que pareça uma proteção técnica, como um limite de tentativas.
- Slug único: o primeiro usa só o nome; se já existir, gera-se uma versão aleatória de 5 letras ou números, verifica-se na base e usa-se a primeira livre.

### Padrões de código
- Seguir os padrões dos projetos de referência `Tech.Aquisition.Customers` e `ntix`, resumidos em [code-patterns.md](code-patterns.md).
- **Antes de adicionar uma estrutura, avalie se ela é realmente necessária.** Um padrão existir na referência não é motivo para trazê-lo: só entra o que o requisito pede ou o que o usuário estipulou. Na dúvida, pergunte. Foi assim que uma camada de feature flag entrou e teve de sair.

### Auditabilidade
- Toda requisição traz as informações auditáveis (`AuditableInfoValueObject`), e o caso de uso as recebe como parâmetro secundário, além do input: `ExecuteAsync(input, auditableInfo, cancellationToken)`.
- Campos: `CorrelationId`, `OriginService`, `ExecutionUser` e `RequestedAt`.

### Logs
- Todo caso de uso registra logs semânticos, com informações relevantes para o negócio (o que foi criado, o que foi recusado e por quê).
- Dado pessoal em log aparece só parcialmente, para atender à LGPD. A ofuscação é um método do value object do dado (`EmailValueObject.GetObfuscatedValue()`, `CpfValueObject.GetObfuscatedValue()`), para o padrão ficar fixo no domínio.

### Escopo de uma entrega
- Endpoint, presentation, casos de uso, infraestrutura e domínio, conforme os padrões estipulados.
- Casos de uso são orquestradores de serviços de domínio, de infraestrutura e externos.
- Testes unitários, integrados, funcionais e de carga.
- Os testes são escritos, não executados. Quem os valida é o usuário.

### Testes
- Unitários: xUnit, padrão AAA, nomes `Given_[Cenário]_When_[Comportamento]_Should_[ResultadoEsperado]`.
- Integrados: xUnit, usando as dependências criadas, padrão AAA, mesma nomenclatura.
- Funcionais: Reqnroll e Gherkin em português (`# language: pt`), com definições semânticas ao negócio.
- Carga: k6. O cadastro de conta usa o perfil moderado: até 50 usuários virtuais, p95 abaixo de 500 ms e menos de 1% de erros.

### Dependências
- Dependência nova é responsabilidade de Claude, com explicação prévia e RFC que a justifique.
- As RFCs ficam em `docs/rfcs/`.

## Observado no repositório

- .NET 10 (`net10.0`), `Nullable` e `ImplicitUsings` habilitados, solução em `ntix.slnx`.
- Projetos: `src/WebApi`, `src/Application`, `src/Domain`, `src/Infra`.
- Testes em `tsts/` (é esse o nome da pasta): `UnitTests`, `IntegratedTests`, `FunctionalTests`.
- `WebApi` usa controllers (`AddControllers`/`MapControllers`) e `AddOpenApi`.
- Versões atuais: xUnit 2.9.3, Reqnroll.xUnit 3.3.4, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4.
- `devops/docker-compose.yml` sobe as dependências locais; os segredos ficam em `devops/.env`, ignorado pelo Git, com modelo em `devops/.env.example`.
- Bibliotecas de teste NSubstitute e `Mvc.Testing`, asserções com o `Assert` do xUnit ([RFCs](../../../../docs/rfcs)).
- O Dockerfile de uma aplicação copia o `.csproj` de cada projeto de que ela depende antes do `dotnet restore`.

## Interpretações a confirmar

Assumidas por Claude. Valem até o usuário dizer o contrário; ao confirmar, mova para "Estipulado".

- "As dependências criadas" dos testes integrados são as dependências reais do requisito (banco etc.), não dublês.
- Não executar os testes não impede compilar: `dotnet build` é permitido.
- Testes de carga ficam em `tsts/LoadTests/`, que ainda não existe. Ela não entra na solução porque não é um projeto .NET.
- Referências dos testes: `UnitTests` e `IntegratedTests` referenciam `Application`; `FunctionalTests` referencia `WebApi`. As demais camadas chegam por transitividade.
- Headers auditáveis: `X-Correlation-Id`, `X-Origin-Service` e `X-Requested-At` são obrigatórios; `X-Execution-User` é opcional, como o usuário na referência, porque o cadastro é anônimo.
- `ExecutionUser` é um identificador e aparece sem ofuscação nos logs, então quem chama não deve enviar nele um dado pessoal, como um email.
- Slug não aparece nos logs, porque revela o nome completo do cliente.
- Enquanto só houver um endpoint, o controller lê os headers com `[FromHeader]` e o caso de uso valida o `AuditableInfoValueObject`, sem middleware.

## Decisões em aberto

Pergunte ao usuário quando um requisito depender de uma delas. Não presuma a resposta.

1. **Carga em novos fluxos**: o perfil moderado foi definido só para o cadastro de conta. Pergunte o perfil de cada novo fluxo.
