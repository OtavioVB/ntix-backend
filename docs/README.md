# ntix

## Rodando localmente

1. Copie `devops/.env.example` para `devops/.env` e defina a senha local do SQL Server e a chave privada das senhas dos clientes.
2. Suba o banco. O serviço `ntix-sqlserver-init` cria o banco `ntix` e aplica os scripts de `data/`:

   ```bash
   docker compose -f devops/docker-compose.yml up -d
   ```

3. Informe a conexão à aplicação e aos testes, com a mesma senha:

   ```powershell
   $env:SqlServerConfiguration__ConnectionString = "Server=localhost,1433;Database=ntix;User Id=sa;Password=<senha>;Encrypt=True;TrustServerCertificate=True"
   ```

4. Suba a API:

   ```bash
   dotnet run --project src/WebApi --launch-profile https
   ```

## Testes

```bash
dotnet test tsts/UnitTests
```

Os testes integrados e funcionais usam o SQL Server do passo 2 e a variável do passo 3. Cada execução cria um banco próprio, aplica os scripts de `data/` e o remove no final:

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

Configure em **Settings > Secrets and variables > Actions** o secret `MSSQL_SA_PASSWORD`. Ele é a senha de um SQL Server descartável, criado para cada job, e não pode conter `;`, porque compõe a connection string. O SQL Server exige ao menos 8 caracteres, com maiúsculas, minúsculas, números e símbolos.

## Análise estática

O CI chama o workflow `.github/workflows/static-analysis.yml` depois dos testes unitários e integrados e antes dos testes funcionais, em todo commit na `main` e em pull requests. Ele também pode ser disparado manualmente. O workflow compila a solução, roda os testes unitários e integrados com cobertura e envia o resultado ao SonarQube Cloud. Se a execução falhar, os testes funcionais e o deploy não rodam. A cobertura não inclui os testes funcionais.

Em **Settings > Secrets and variables > Actions**, além do secret `MSSQL_SA_PASSWORD`, configure:

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

Depois dos testes funcionais, em push para `main` ou disparo manual em `main`, o job **Implantar na VPS** constrói a imagem da API com `src/WebApi/Dockerfile`, envia a imagem e a configuração por SSH, gera `devops/.env` no servidor e sobe todos os serviços em `~/ntix`.

A cada deploy, `devops/vps/ensure-dependencies.sh` verifica `curl`, `tar`, `gzip`, Docker e Docker Compose no servidor e instala o que faltar. Para instalar, o usuário de SSH precisa ser root ou ter sudo sem senha. Também é preciso ter acesso à internet e um sistema com `apt`, `dnf` ou `yum`. Recomenda-se ao menos 6 GB de memória, porque o SQL Server sozinho reserva até 2 GB.

| Secret | Conteúdo |
|---|---|
| `VPS_HOST` | Endereço do servidor |
| `VPS_PORT` | Porta SSH. Opcional, o padrão é 22 |
| `VPS_USER` | Usuário de SSH |
| `VPS_SSH_PRIVATE_KEY` | Chave privada exclusiva para o deploy |
| `VPS_KNOWN_HOSTS` | Chave pública do servidor, gerada com `ssh-keyscan -p <porta> <host>` e conferida antes de salvar |
| `VPS_MSSQL_SA_PASSWORD` | Senha do SQL Server da VPS, sem `;` e sem `'` |
| `VPS_PASSWORD_PRIVATE_KEY` | Chave privada combinada ao hash das senhas dos clientes, sem `'`. Gere com `openssl rand -base64 32` |
| `VPS_JWT_SECRET_KEY` | Chave que assina os tokens JWT, com ao menos 32 bytes e sem `'`. Gere com `openssl rand -base64 32` |

A variável de repositório `VPS_MSSQL_PID` é opcional e define a edição do SQL Server. O padrão é `Express`, porque a edição `Developer` não pode ser usada em produção. Para outra edição, é preciso ter a licença.

A connection string da API, o endpoint de telemetria e as demais configurações são resolvidos pelo `docker-compose.yml` a partir desse `.env`, a cada deploy.

Só a API fica pública, na porta 8080. Os demais serviços escutam em `127.0.0.1`. Para abrir o Grafana, use um túnel e acesse <http://localhost:3000>:

```bash
ssh -L 3000:localhost:3000 <usuario>@<host>
```

A senha do SQL Server é definida quando o volume é criado. Depois disso, trocar o secret sem alterar a senha no banco faz a API perder o acesso.

Trocar `VPS_PASSWORD_PRIVATE_KEY` invalida as senhas já cadastradas, porque o hash depende dela. Trocar `VPS_JWT_SECRET_KEY` invalida os tokens já emitidos.

## Banco de dados

O esquema fica em scripts idempotentes em `data/<schema>/NNN-descricao.sql`, aplicados em ordem de caminho. Para mudar o esquema, adicione um novo script.

## Observabilidade

Logs, traces, métricas e dashboards ficam no Grafana, em <http://localhost:3000>. Veja [observabilidade.md](observabilidade.md).

## Decisões técnicas

As justificativas das dependências estão em [rfcs](rfcs).
