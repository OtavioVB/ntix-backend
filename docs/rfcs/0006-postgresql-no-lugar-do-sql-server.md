# RFC 0006: PostgreSQL no lugar do SQL Server

- **Status**: Aceita. Substitui a escolha de banco da [RFC 0001](0001-sql-server-como-banco-de-dados.md) e o driver da [RFC 0004](0004-sql-nativo-no-lugar-do-ef-core.md)
- **Data**: 2026-11-02
- **Requisito**: Implantação na VPS ARM64

## Contexto

A VPS de produção é ARM64, e a Microsoft só publica a imagem do SQL Server para Linux em amd64. Emular amd64 com QEMU não é suportado pelo SQL Server e não serve para produção. O usuário decidiu trocar o banco por PostgreSQL.

As garantias da RFC 0001 continuam valendo: unicidade de email, documento e slug no próprio banco e transação ACID por cadastro. A RFC 0004 também continua valendo: SQL nativo, sem ORM.

## Decisão

| Dependência | Versão | Onde |
|---|---|---|
| PostgreSQL (imagem `postgres`) | `18.1-alpine` | `devops/docker-compose.yml`, CI |
| `Npgsql` | 10.0.3 | `src/Infra` |

Saem `Microsoft.Data.SqlClient`, a fixação do `System.Security.Cryptography.Pkcs`, que só existia por causa dele, e a imagem `mcr.microsoft.com/mssql/server`.

- Os índices únicos continuam sendo a garantia definitiva contra duplicidade. O repositório traduz o erro `23505` (`unique_violation`) pelo nome da constraint.
- No PostgreSQL, um erro invalida a transação inteira. Antes do `INSERT`, o repositório cria um *savepoint* e, se houver conflito, volta só até ele, para a nova tentativa de slug continuar na mesma transação.
- `created_at` usa `timestamptz`, porque as datas da aplicação são UTC.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Manter o SQL Server em uma VPS x86_64 | Exigiria trocar a VPS. O usuário preferiu trocar o banco |
| SQL Server gerenciado fora da VPS | Custo de licença e dependência de outro serviço |
| Azure SQL Edge | Tinha imagem ARM64, mas foi descontinuado |
| MySQL ou MariaDB | Também rodam em ARM64, mas o PostgreSQL tem um provedor .NET mais maduro e garantias equivalentes às da RFC 0001 |

## Justificativa

O PostgreSQL atende às mesmas garantias de consistência que motivaram o SQL Server: ACID, índices únicos e transações como modo normal de trabalho. Ele tem imagem oficial para ARM64, licença livre, inclusive em produção, e consome menos memória, o que importa em uma VPS pequena. O `Npgsql` é o provedor .NET de referência para PostgreSQL, com licença PostgreSQL (permissiva).

## Consequências

- Os scripts de `data/` foram reescritos para a sintaxe do PostgreSQL. Bancos SQL Server existentes não são migrados: não havia dados de produção.
- Nomes não citados no PostgreSQL ficam em minúsculas, e o nome da constraint que o repositório compara está em minúsculas.
- A variável de conexão passa a ser `PostgreSqlConfiguration__ConnectionString`, e os secrets do banco mudam de nome. Ver `docs/README.md`.
- A aplicação usa o usuário dono do banco. Um usuário com permissões mínimas é uma melhoria pendente.

## Como reverter

Voltar o driver para `Microsoft.Data.SqlClient`, reescrever os scripts de `data/` e o repositório, trocar o tratamento de `23505` pelo erro 2601 e remover o savepoint. O servidor precisa ser x86_64.
