# RFC 0004: SQL nativo no lugar do EF Core

- **Status**: Aceita. O SQL nativo continua; o driver `Microsoft.Data.SqlClient` foi substituído pelo `Npgsql` na [RFC 0006](0006-postgresql-no-lugar-do-sql-server.md)
- **Data**: 2026-10-31
- **Requisito**: Cadastro de cliente na plataforma Ntix

## Contexto

Performance é um critério da implementação, e o usuário definiu que o acesso a dados deve usar SQL nativo. O cadastro executa poucas consultas simples, mas muito frequentes, em que a tradução de LINQ, o rastreamento de mudanças e a infraestrutura do EF Core não trazem benefício. Com SQL nativo também fica explícito o que chega ao banco: consultas exatas sobre os índices únicos e parâmetros com o mesmo tipo das colunas, que evitam conversões implícitas.

Nenhuma medição comparativa foi feita. A decisão parte da diretriz do usuário e da redução de camadas, não de um benchmark.

## Decisão

| Dependência | Versão | Onde |
|---|---|---|
| `Microsoft.Data.SqlClient` | 7.1.1 | `src/Infra` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.14 | `src/Infra` |
| `System.Security.Cryptography.Pkcs` | 10.0.14 | `src/Infra` |

O `Microsoft.Data.SqlClient` 7.1.1 traz o `System.Security.Cryptography.Pkcs` 9.0.18, afetado pelas vulnerabilidades GHSA-846f-f764-76mx (alta) e GHSA-2fmv-rv2c-59x7 (moderada). A referência direta fixa uma versão corrigida e pode sair quando o driver trouxer uma versão sem elas.

Saem `Microsoft.EntityFrameworkCore.SqlServer` e `Microsoft.EntityFrameworkCore.Design`.

- O esquema passa a ser mantido em scripts SQL idempotentes em `data/`, aplicados pelo serviço `ntix-sqlserver-init` do `docker-compose`.
- A transação é controlada por `IUnitOfWork`, com begin, commit e rollback chamados pelo caso de uso.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Manter o EF Core | Contraria a definição do usuário |
| Dapper | Mapeamento mais cômodo, porém com mais uma dependência. Com poucas consultas, o ADO.NET direto basta |
| Stored procedures, como no projeto `ntix` | Levariam a regra de orquestração para o banco, e o usuário pediu que o commit seja decidido pelo caso de uso |

## Consequências

- O mapeamento de parâmetros e colunas é manual. Tamanhos de coluna ficam repetidos entre os scripts e o repositório.
- Não há mais `EnableRetryOnFailure`: uma falha transitória ou um deadlock vira erro para quem chamou, e fica a cargo dele tentar de novo.
- Mudanças de esquema exigem um novo script em `data/`, e produção precisa de um processo para aplicá-los.

## Como reverter

Reintroduzir o EF Core no repositório e no `IUnitOfWork`, mapear o `CustomerModel` e gerar uma migration inicial a partir do esquema existente.
