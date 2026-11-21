# ntix

## Rodando localmente

1. Copie `devops/.env.example` para `devops/.env` e defina a senha local do PostgreSQL e a chave privada das senhas dos clientes.
2. Suba o banco. O serviço `ntix-postgresql-init` cria o banco `ntix` e aplica os scripts de `data/`:

   ```bash
   docker compose -f devops/docker-compose.yml up -d
   ```

3. Informe a conexão à aplicação e aos testes, com a mesma senha:

   ```powershell
   $env:PostgreSqlConfiguration__ConnectionString = "Host=localhost;Port=5432;Database=ntix;Username=ntix;Password=<senha>"
   ```

4. Suba a API:

   ```bash
   dotnet run --project src/WebApi --launch-profile https
   ```

## Testes

```bash
dotnet test tsts/UnitTests
```

Os testes integrados e funcionais usam o PostgreSQL do passo 2 e a variável do passo 3. Cada execução cria um banco próprio, aplica os scripts de `data/` e o remove no final:

```bash
dotnet test tsts/IntegratedTests
```

```bash
dotnet test tsts/FunctionalTests
```

O teste de carga precisa da API no ar:

```bash
k6 run -e BASE_URL=http://localhost:5001 tsts/LoadTests/customers-create-tests.js
```

Para gerar tráfego em cada ramo do funil de cadastro, use `tsts/LoadTests/customers-funnel-tests.js`, descrito em [observabilidade.md](observabilidade.md).

## Integração contínua

O workflow `.github/workflows/github-workflows.yml` roda em push e pull request para `main` e pode ser disparado manualmente em **Actions > CI > Run workflow**: build, testes unitários e integrados em paralelo, análise estática e, depois, testes funcionais. Os testes de carga não fazem parte da esteira.

Configure em **Settings > Secrets and variables > Actions** o secret `POSTGRES_PASSWORD`. Ele é a senha de um PostgreSQL descartável, criado para cada job, e não pode conter `;`, porque compõe a connection string.

## Análise estática

O CI chama o workflow `.github/workflows/static-analysis.yml` depois dos testes unitários e integrados e antes dos testes funcionais, em todo commit na `main` e em pull requests. Ele também pode ser disparado manualmente. O workflow compila a solução, roda os testes unitários e integrados com cobertura e envia o resultado ao SonarQube Cloud. Se a execução falhar, os testes funcionais e o deploy não rodam. A cobertura não inclui os testes funcionais.

Em **Settings > Secrets and variables > Actions**, além do secret `POSTGRES_PASSWORD`, configure:

| Tipo | Nome | Conteúdo |
|---|---|---|
| Secret | `SONAR_TOKEN` | API Key gerada no SonarQube Cloud |
| Variável | `SONAR_PROJECT_KEY` | Chave do projeto no SonarQube Cloud |
| Variável | `SONAR_ORGANIZATION` | Chave da organização no SonarQube Cloud |

No SonarQube Cloud, desligue a análise automática em **Administration > Analysis Method**.

Os parâmetros ficam no bloco `env` do workflow:

| Variável | Parâmetro do Sonar | Efeito |
|---|---|---|
| `SONAR_COVERAGE_EXCLUSIONS` | `sonar.coverage.exclusions` | Arquivos fora do cálculo de cobertura |
| `SONAR_EXCLUSIONS` | `sonar.exclusions` | Arquivos fora da identificação de issues |
| `SONAR_IGNORED_RULE` e `SONAR_IGNORED_RULE_FILES` | `sonar.issue.ignore.multicriteria` | Regra ignorada em um conjunto de arquivos |
| `SONAR_CPD_EXCLUSIONS` | `sonar.cpd.exclusions` | Arquivos fora da detecção de duplicação |

## Implantação na VPS

Os jobs de build, testes e análise estática rodam no environment **Staging** do GitHub, e o job **Implantar na VPS** roda no environment **Production**. Os dois são criados automaticamente na primeira execução e podem ser configurados em **Settings > Environments**, com aprovação manual antes do deploy, restrição à `main` e secrets próprios de cada ambiente.

Depois dos testes funcionais, em push para `main` ou disparo manual em `main`, o job **Implantar na VPS** constrói a imagem da API com `src/WebApi/Dockerfile`, envia a imagem e a configuração por SSH, gera `devops/.env` no servidor e sobe todos os serviços em `~/ntix`.

A cada deploy, `devops/vps/ensure-dependencies.sh` verifica `curl`, `tar`, `gzip`, Docker e Docker Compose no servidor e instala o que faltar. Para instalar, o usuário de SSH precisa ser root ou ter sudo sem senha. Também é preciso ter acesso à internet e um sistema com `apt`, `dnf` ou `yum`. Todas as imagens, inclusive a do PostgreSQL, existem para amd64 e arm64.

A imagem da API é construída para a arquitetura detectada no servidor, x86_64 ou ARM64.

| Secret | Conteúdo |
|---|---|
| `VPS_HOST` | Endereço do servidor |
| `VPS_PORT` | Porta SSH. Opcional, o padrão é 22 |
| `VPS_USER` | Usuário de SSH |
| `VPS_SSH_PRIVATE_KEY` | Chave privada exclusiva para o deploy |
| `VPS_KNOWN_HOSTS` | Chave pública do servidor, gerada com `ssh-keyscan -p <porta> <host>` e conferida antes de salvar |
| `VPS_POSTGRES_PASSWORD` | Senha do PostgreSQL da VPS, sem `;` e sem `'` |
| `VPS_PASSWORD_PRIVATE_KEY` | Chave privada combinada ao hash das senhas dos clientes, sem `'`. Gere com `openssl rand -base64 32` |
| `VPS_PAYMENT_CREDENTIAL_ENCRYPTION_KEY` | Chave AES-256 que cifra as credenciais das integrações de pagamento (REQ4): 32 bytes em Base64. Gere com `openssl rand -base64 32` e guarde um backup fora do GitHub |
| `VPS_JWT_SIGNING_KEY` | Chave que assina o access token do usuário do produtor (REQ5): 32 bytes ou mais em Base64. Gere com `openssl rand -base64 32` |
| `VPS_PGADMIN_EMAIL` | E-mail de login do PgAdmin em database.ntix.com.br, sem `'` |
| `VPS_PGADMIN_PASSWORD` | Senha inicial de login do PgAdmin, sem `'` |
| `VPS_PGADMIN_DATABASE_PASSWORD` | Senha do usuário somente leitura `ntix_pgadmin` do PostgreSQL, sem `'`. Gere com `openssl rand -base64 24` |
| `VPS_PGADMIN_ALLOWED_IPS` | IPs ou faixas CIDR que podem abrir database.ntix.com.br, separados por vírgula ou espaço. Ex.: `203.0.113.7, 2001:db8::/64` |

A connection string da API, o endpoint de telemetria e as demais configurações são resolvidos pelo `docker-compose.yml` a partir desse `.env`, a cada deploy.

O nginx da VPS publica a API, o frontend e o PgAdmin. Os contêineres escutam em `127.0.0.1`. Para abrir o Grafana, use um túnel e acesse <http://localhost:3000>:

```bash
ssh -L 3000:localhost:3000 <usuario>@<host>
```

A senha do PostgreSQL é definida quando o volume é criado. Depois disso, trocar o secret sem alterar a senha no banco faz a API perder o acesso.

Na primeira implantação com PostgreSQL em um servidor que já tinha o SQL Server, o deploy remove os contêineres antigos, mas o volume `devops_ntix-sqlserver-volume` continua ocupando disco. Como não havia dados de produção, ele pode ser apagado com `docker volume rm devops_ntix-sqlserver-volume`.

Trocar `VPS_PASSWORD_PRIVATE_KEY` invalida as senhas já cadastradas, porque o hash depende dela.

Trocar ou perder `VPS_PAYMENT_CREDENTIAL_ENCRYPTION_KEY` deixa ilegíveis as credenciais de pagamento já gravadas: nenhuma cobrança sai até os produtores cadastrarem as credenciais de novo. A rotação da chave ainda não existe. Trocar `VPS_JWT_SIGNING_KEY` só derruba as sessões abertas.

Para rodar a API fora do contêiner, defina as mesmas chaves por user secrets do projeto `src/WebApi`:

```bash
dotnet user-secrets set "PaymentCredentialConfiguration:EncryptionKey" "$(openssl rand -base64 32)" --project src/WebApi
```

```bash
dotnet user-secrets set "JwtConfiguration:SigningKey" "$(openssl rand -base64 32)" --project src/WebApi
```

## Banco de dados

O esquema fica em scripts idempotentes em `data/<schema>/NNN-descricao.sql`, aplicados em ordem de caminho. Para mudar o esquema, adicione um novo script.

### PgAdmin

Em produção, o PgAdmin fica em <https://database.ntix.com.br>, com o servidor `ntix` já cadastrado. Antes do primeiro deploy:

1. Crie no DNS o registro de `database.ntix.com.br` apontando para a VPS, sem proxy de CDN: o certbot valida o nome pela porta 80 e o nginx filtra pelo IP real de quem acessa. Sem o registro, a emissão do certificado falha e o deploy para.
2. Cadastre os secrets `VPS_PGADMIN_*` da tabela acima.

Só os IPs de `VPS_PGADMIN_ALLOWED_IPS` chegam ao login; os demais recebem 403. Quando o seu IP mudar, atualize o secret e rode o deploy de novo (**Actions > CI > Run workflow** na `main`).

O PgAdmin conecta com o usuário `ntix_pgadmin`, somente leitura em todo o banco, inclusive em tabelas futuras: ele consulta, mas não altera dados nem estrutura. Na primeira conexão, informe a senha de `VPS_PGADMIN_DATABASE_PASSWORD`. O usuário é recriado a cada deploy, então trocar o secret troca a senha.

A senha de login do PgAdmin (`VPS_PGADMIN_PASSWORD`) só vale na criação do volume `ntix-pgadmin-volume`. Depois, troque-a pela tela do PgAdmin; trocar o secret não tem efeito.

Localmente, preencha as variáveis `PGADMIN_*` em `devops/.env`, suba com o profile e acesse <http://localhost:5050>:

```bash
docker compose -f devops/docker-compose.yml --profile pgadmin up -d
```

## Observabilidade

Logs, traces, métricas e dashboards ficam no Grafana, em <http://localhost:3000>. Veja [observabilidade.md](observabilidade.md).

## Decisões técnicas

As justificativas das dependências estão em [rfcs](rfcs).
