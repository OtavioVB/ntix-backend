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

## Banco de dados

O esquema fica em scripts idempotentes em `data/<schema>/NNN-descricao.sql`, aplicados em ordem de caminho. Para mudar o esquema, adicione um novo script.

## Observabilidade

Logs, traces, métricas e dashboards ficam no Grafana, em <http://localhost:3000>. Veja [observabilidade.md](observabilidade.md).

## Decisões técnicas

As justificativas das dependências estão em [rfcs](rfcs).
