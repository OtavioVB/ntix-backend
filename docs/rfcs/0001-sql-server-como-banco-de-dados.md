# RFC 0001: SQL Server como banco de dados

- **Status**: Substituída pela [RFC 0006](0006-postgresql-no-lugar-do-sql-server.md): o banco passou a ser PostgreSQL, porque a VPS é ARM64
- **Data**: 2026-10-31
- **Requisito**: Cadastro de conta de clientes na plataforma Ntix

## Contexto

O cadastro de conta precisa guardar nome completo, email, documento e slug, com três garantias:

1. Email, documento e slug são únicos por cliente, inclusive quando dois cadastros chegam ao mesmo tempo.
2. Um cadastro é gravado por inteiro ou não é gravado, sem conta pela metade.
3. Depois de confirmado, o cadastro é lido igual por todas as instâncias da aplicação.

São dados cadastrais de clientes: estrutura estável, relacionamentos previsíveis e custo alto para qualquer inconsistência. Uma conta duplicada ou um documento associado a dois clientes é um problema de negócio, não só técnico. A solução ainda não tinha banco de dados.

## Decisão

| Dependência | Versão | Onde |
|---|---|---|
| SQL Server (imagem `mcr.microsoft.com/mssql/server`) | `2025-CU10-ubuntu-24.04`, edição Developer | `devops/docker-compose.yml` |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.14 | `src/Infra` |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.14 (só em desenvolvimento) | `src/Infra` |
| `System.Security.Cryptography.Pkcs` | 10.0.14 | `src/Infra` |

O `Microsoft.Data.SqlClient` 6.1.7, usado pelo EF Core, traz o `System.Security.Cryptography.Pkcs` 9.0.11, que tem vulnerabilidades conhecidas (GHSA-846f-f764-76mx, de gravidade alta, e GHSA-2fmv-rv2c-59x7, moderada). A referência direta fixa uma versão corrigida e pode sair quando o `SqlClient` trouxer uma versão sem essas vulnerabilidades.

As garantias ficam no banco, não só no código:

- Os índices únicos em `Email`, `Document` e `Slug` são a proteção definitiva contra duplicidade. A verificação feita pelo caso de uso antes de gravar serve para responder cedo, mas não basta sozinha quando há concorrência.
- O `INSERT` é uma transação ACID: ou a linha entra respeitando todos os índices, ou nada é gravado.
- O esquema é versionado em migrations do EF Core.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| MongoDB | Não atende tão diretamente ao requisito. Ver a análise abaixo |
| PostgreSQL | Tecnicamente atenderia às três garantias. É a alternativa mais próxima. Ver a análise abaixo |
| MySQL | Também é relacional, mas não oferece vantagem sobre o SQL Server neste requisito e tem integração menos direta com o ecossistema .NET |
| Azure Cosmos DB, DynamoDB | Modelos pensados para escala horizontal, com unicidade limitada por partição. O cadastro não precisa dessa escala e perderia a garantia simples de unicidade global |
| Redis | Armazenamento em memória, adequado a cache, não a fonte da verdade de dados cadastrais |

### Por que não MongoDB

O MongoDB é uma boa escolha para documentos de estrutura variável e leitura de agregados. O cadastro de clientes tem a necessidade oposta:

- **Esquema**: os dados são estáveis e tabulares. O esquema flexível traz pouco ganho aqui e passa para a aplicação a responsabilidade de garantir o formato, que no SQL Server é do próprio banco (`NOT NULL`, tamanho, chave primária).
- **Unicidade**: o MongoDB também tem índices únicos, mas em coleções fragmentadas (*sharded*) o índice único precisa incluir a chave de fragmentação. Três unicidades independentes (email, documento e slug) não cabem nesse modelo sem coleções auxiliares ou lógica na aplicação. No SQL Server são três índices e nada mais.
- **Transações**: o MongoDB oferece transações com várias operações desde a versão 4.0, mas exige *replica set* e as trata como exceção ao modelo de documento. No SQL Server, a transação é o modo normal de trabalho. Isso pesa nos próximos requisitos, quando operações tocarem várias tabelas.
- **Relacionamentos futuros**: dados de cliente tendem a se relacionar com outros (pedidos, por exemplo), e chave estrangeira e `JOIN` são naturais no modelo relacional.

### Por que SQL Server e não PostgreSQL

Aqui a diferença é menor, e a decisão não se apoia em garantia de consistência: o PostgreSQL também é ACID e tem índices únicos. Os motivos são de encaixe com o projeto:

- **Ecossistema .NET**: provedor do EF Core mantido pela própria Microsoft, junto com o runtime e o `Microsoft.Data.SqlClient`.
- **Ferramentas e operação**: SQL Server Management Studio, Query Store, planos de execução e, se a plataforma for para o Azure, Azure SQL como caminho gerenciado sem mudança de motor.
- **Concorrência**: com `READ_COMMITTED_SNAPSHOT`, leituras não bloqueiam escritas, o que ajuda na mistura de leituras e inserções do cadastro.

O ponto a favor do PostgreSQL é o custo: é livre de licença, enquanto o SQL Server é pago em produção. Ver Consequências.

## Justificativa

O requisito é dominado por consistência transacional e unicidade, que são o ponto forte de um banco relacional. Dentro dessa família, o SQL Server é o que melhor se integra a uma solução .NET 10, e o EF Core reduz o custo de acesso a dados e de versionamento de esquema em relação a escrever SQL à mão com Dapper. O EF Core tem licença MIT.

## Consequências

- **Custo**: a edição Developer é gratuita, mas só para desenvolvimento e teste. Produção exige licença (Standard ou Enterprise) ou Azure SQL, e a edição Express é gratuita, porém limitada. Esse custo precisa entrar na decisão de hospedagem.
- **Licença de uso**: subir o contêiner aceita o EULA do SQL Server (`ACCEPT_EULA=Y` no `docker-compose.yml`).
- **Ambiente**: para rodar a aplicação e os testes integrados e funcionais, é preciso ter Docker e subir o contêiner, que usa bastante memória (mínimo de 2 GB).
- **Escala**: a escrita escala principalmente na vertical. Não é uma limitação para o volume previsto de cadastros, mas vale reavaliar se esse volume mudar de ordem de grandeza.
- **Arquitetura**: como `Infra` não referencia `Domain`, o EF Core mapeia um modelo de persistência próprio (`CustomerAccountModel`), e a `Application` converte a entidade para esse modelo.

## Como reverter

Trocar o provedor do EF Core (`UseSqlServer`) e a tradução dos erros de índice único em `CustomerAccountRepository`, gerar novas migrations e substituir o serviço no `docker-compose.yml`. Para um banco relacional, o caso de uso e o domínio não mudam. Para um banco de documentos, o repositório precisa ser reescrito.
