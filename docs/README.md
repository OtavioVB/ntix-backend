# ntix

## Rodando localmente

1. Copie `devops/.env.example` para `devops/.env` e defina a senha local do SQL Server.
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

O workflow `.github/workflows/github-workflows.yml` roda em push e pull request para `main` e pode ser disparado manualmente em **Actions > CI > Run workflow**: build, testes unitários e integrados em paralelo e, depois, testes funcionais. Os testes de carga não fazem parte da esteira.

Configure em **Settings > Secrets and variables > Actions** o secret `MSSQL_SA_PASSWORD`. Ele é a senha de um SQL Server descartável, criado para cada job, e não pode conter `;`, porque compõe a connection string. O SQL Server exige ao menos 8 caracteres, com maiúsculas, minúsculas, números e símbolos.

## Implantação na VPS

Depois dos testes funcionais, em push para `main` ou disparo manual em `main`, o job **Implantar na VPS** constrói a imagem da API com `src/WebApi/Dockerfile`, envia a imagem e a configuração por SSH, gera `devops/.env` no servidor e sobe todos os serviços em `~/ntix`.

O servidor precisa ter Docker com Compose e `curl`, e o usuário de SSH precisa poder usar o Docker. Recomenda-se ao menos 6 GB de memória, porque o SQL Server sozinho reserva até 2 GB.

| Secret | Conteúdo |
|---|---|
| `VPS_HOST` | Endereço do servidor |
| `VPS_PORT` | Porta SSH. Opcional, o padrão é 22 |
| `VPS_USER` | Usuário de SSH |
| `VPS_SSH_PRIVATE_KEY` | Chave privada exclusiva para o deploy |
| `VPS_KNOWN_HOSTS` | Chave pública do servidor, gerada com `ssh-keyscan -p <porta> <host>` e conferida antes de salvar |
| `VPS_MSSQL_SA_PASSWORD` | Senha do SQL Server da VPS, sem `;` e sem `'` |

A variável de repositório `VPS_MSSQL_PID` é opcional e define a edição do SQL Server. O padrão é `Express`, porque a edição `Developer` não pode ser usada em produção. Para outra edição, é preciso ter a licença.

A connection string da API, o endpoint de telemetria e as demais configurações são resolvidos pelo `docker-compose.yml` a partir desse `.env`, a cada deploy.

Só a API fica pública, na porta 8080. Os demais serviços escutam em `127.0.0.1`. Para abrir o Grafana, use um túnel e acesse <http://localhost:3000>:

```bash
ssh -L 3000:localhost:3000 <usuario>@<host>
```

A senha do SQL Server é definida quando o volume é criado. Depois disso, trocar o secret sem alterar a senha no banco faz a API perder o acesso.

## Banco de dados

O esquema fica em scripts idempotentes em `data/<schema>/NNN-descricao.sql`, aplicados em ordem de caminho. Para mudar o esquema, adicione um novo script.

## Observabilidade

Logs, traces, métricas e dashboards ficam no Grafana, em <http://localhost:3000>. Veja [observabilidade.md](observabilidade.md).

## Decisões técnicas

As justificativas das dependências estão em [rfcs](rfcs).
