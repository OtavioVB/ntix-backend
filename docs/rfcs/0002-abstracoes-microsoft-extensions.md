# RFC 0002: Abstrações Microsoft.Extensions nas camadas internas

- **Status**: Aceita
- **Data**: 2026-10-31
- **Requisito**: Cadastro de conta de clientes na plataforma Ntix

## Contexto

`Application` e `Infra` são bibliotecas de classes e não enxergam o framework do ASP.NET Core. Pelo padrão de código adotado, cada módulo registra as próprias dependências (`ServiceCollectionExtensions`), lê configuração tipada e escreve logs estruturados.

## Decisão

| Dependência | Versão | Onde |
|---|---|---|
| `Microsoft.Extensions.Configuration.Abstractions` | 10.0.14 | `src/Application` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.14 | `src/Application` |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | `src/Application` |
| `Microsoft.Extensions.Configuration.Binder` | 10.0.14 | `src/Infra` |

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| `FrameworkReference` para `Microsoft.AspNetCore.App` | Prende as camadas internas ao ASP.NET Core, o que atrapalharia workers e aplicações de console |
| Abstrações próprias de log e configuração | Reinventa o padrão da plataforma, sem benefício |

## Justificativa

São pacotes oficiais da Microsoft, com licença MIT, só de abstrações, e é o mesmo uso do projeto de referência. A versão acompanha a do EF Core.

## Consequências

As versões precisam acompanhar as atualizações do .NET 10. Não há impacto em execução nem em `docker-compose.yml`.

## Como reverter

Remover os pacotes e os `ServiceCollectionExtensions`, passando o registro de dependências para o `Program` de cada aplicação.
