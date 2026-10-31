# RFC 0003: Bibliotecas de teste

- **Status**: Aceita
- **Data**: 2026-10-31
- **Requisito**: Cadastro de conta de clientes na plataforma Ntix

## Contexto

Os testes unitários dos casos de uso precisam isolar o repositório, e os testes funcionais precisam acionar a API real. Os projetos de teste só tinham xUnit e Reqnroll.

## Decisão

| Dependência | Versão | Onde | Para quê |
|---|---|---|---|
| `NSubstitute` | 6.2.0 | `tsts/UnitTests` | Dublês das interfaces consumidas pelos casos de uso |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.14 | `tsts/FunctionalTests` | Subir a `WebApi` real, em memória, sem porta nem processo separado |
| `Microsoft.Extensions.Configuration` | 10.0.14 | `tsts/IntegratedTests` | Montar a configuração que os `ServiceCollectionExtensions` recebem |
| `Microsoft.Extensions.DependencyInjection` | 10.0.14 | `tsts/IntegratedTests` | Montar o contêiner de dependências; vinha do EF Core até a RFC 0004 |
| `Microsoft.Extensions.Logging` | 10.0.14 | `tsts/IntegratedTests` | `AddLogging`; vinha do EF Core até a RFC 0004 |

As asserções usam o `Assert` do xUnit.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Moq | A versão 4.20 gerou desconfiança na comunidade ao incluir o SponsorLink, e a sintaxe é mais verbosa |
| Dublês escritos à mão | Cada novo cenário exigiria mais código de teste, sem ganho |
| FluentAssertions | O `Assert` do xUnit basta, e as versões recentes dessa biblioteca têm licença comercial |
| Testcontainers | Os testes integrados e funcionais usam as dependências criadas em `devops/docker-compose.yml`, como foi estipulado, sem precisar de um pacote a mais |
| Testes funcionais via HTTP contra uma API já rodando | Exige gerenciar um processo à parte e dificulta apontar para um banco de teste |

## Justificativa

NSubstitute tem licença BSD e sintaxe enxuta. `Mvc.Testing` e `Microsoft.Extensions.Configuration` são pacotes oficiais da Microsoft, com licença MIT, e não entram em produção.

## Consequências

Os testes integrados e funcionais precisam do SQL Server do `docker-compose` no ar e da variável `SqlServerConfiguration__ConnectionString`. Cada execução cria e remove o próprio banco de teste.

## Como reverter

Remover os pacotes dos `.csproj` dos testes e reescrever os dublês e a inicialização da API de teste.
