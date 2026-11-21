# RFC 0009: PgAdmin para acesso ao banco de produção

- **Status**: Aceita
- **Data**: 2026-11-21
- **Requisito**: acesso à base de dados da Ntix pelo navegador, em database.ntix.com.br

## Contexto

O PostgreSQL da VPS escuta só em `127.0.0.1`. Consultar a base de produção exige um túnel SSH e um cliente instalado na máquina, o que limita quem pode investigar dados e torna o acesso dependente da chave do servidor.

## Decisão

Contêiner `dpage/pgadmin4:9.19.0` (amd64 e arm64) no `devops/docker-compose.yml`, no profile `pgadmin`, publicado em `127.0.0.1:5050` e exposto pelo nginx em `https://database.ntix.com.br`, no mesmo certificado Let's Encrypt dos demais nomes.

Três barreiras:

1. O nginx só aceita os IPs do secret `VPS_PGADMIN_ALLOWED_IPS`; os demais recebem 403.
2. Login próprio do PgAdmin (`VPS_PGADMIN_EMAIL` e `VPS_PGADMIN_PASSWORD`).
3. Conexão com o usuário `ntix_pgadmin`, membro de `pg_read_all_data` e com sessões read-only por padrão: lê todos os schemas, inclusive tabelas futuras, sem alterar dados nem estrutura. O usuário `ntix` da API não é usado.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Não adicionar dependência (túnel SSH + cliente local) | Exige acesso SSH ao servidor para cada pessoa que consulta a base |
| Publicar a porta 5432 com lista de IPs | Expõe o protocolo do PostgreSQL à internet e ainda exige cliente instalado |
| Adminer, CloudBeaver | O PgAdmin é a ferramenta oficial do PostgreSQL, com imagem mantida pelo projeto e suporte a ARM64 |

## Justificativa

Imagem oficial do projeto pgAdmin, licença PostgreSQL, multi-arquitetura (a VPS é ARM64) e sem impacto no código .NET: nada muda no build, nos testes ou na API.

## Consequências

- Um contêiner a mais na VPS, com volume `ntix-pgadmin-volume` para usuários e preferências do PgAdmin.
- O deploy passa a exigir os secrets `VPS_PGADMIN_*` e o registro DNS de `database.ntix.com.br`.
- `apply-gateway.sh` mantém a lista de IPs em `/etc/nginx/ntix/database-allowed-ips.conf`, fora de `conf.d`, porque o deploy do frontend também aplica o gateway.
- Mudar o IP de quem acessa exige atualizar o secret e rodar o deploy.

## Como reverter

Remover o serviço `ntix-pgadmin` e o volume do compose, `--profile pgadmin` do workflow, `gateway/ntix-database.conf`, `database.ntix.com.br` de `CERT_DOMAINS` e o bloco do usuário `ntix_pgadmin` de `init-database.sh`. Na VPS, `DROP ROLE ntix_pgadmin`, `docker volume rm devops_ntix-pgadmin-volume` e apagar `/etc/nginx/ntix/`.
