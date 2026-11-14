---
name: local-tests
description: Roda os testes do ntix-backend na máquina local (Windows + Git Bash) como a esteira roda - unitários, integrados e funcionais (.NET, com PostgreSQL 18 no WSL) e os do front (Vitest). Etapa obrigatória antes de abrir ou atualizar qualquer PR, e o caminho para reproduzir e corrigir falhas da esteira. Use também quando o usuário pedir para rodar, validar ou corrigir testes.
---

# Testes locais

Etapa obrigatória do `full-cycle` (seções 4.2 e 4.3): todo trabalho roda `all` aqui antes de abrir ou atualizar o PR, e é por aqui que se reproduz uma falha da esteira antes de corrigi-la. O PR só é liberado com as suítes locais e a esteira verdes.

## Como rodar

```bash
bash .claude/skills/local-tests/scripts/run-tests.sh all
```

| Suíte | Comando | Precisa de banco |
|---|---|---|
| Unitários | `run-tests.sh unit` | Não |
| Integrados | `run-tests.sh integrated` | Sim |
| Funcionais (Reqnroll) | `run-tests.sh functional` | Sim |
| Front (tsc + Vitest) | `run-tests.sh web` | Não |
| Tudo | `run-tests.sh all` | Sim |

- O segundo argumento é o `--filter` do `dotnet test`, por exemplo `run-tests.sh integrated "FullyQualifiedName~Payment"`.
- A suíte de integrados ou funcionais leva alguns minutos, porque o hash de senha é lento de propósito. Rode-a em segundo plano (`run_in_background`), com a saída em arquivo no scratchpad, e leia o resumo no fim. Não use um timeout curto de terminal.
- Para achar as falhas no log: `grep -E "Com falha |Failed |Mensagem de erro|Error Message" <log>`. O .NET da máquina está em pt-BR.

## Banco de teste: PostgreSQL 18 no WSL

A máquina não tem Docker. O `run-tests.sh` chama `scripts/wsl-postgresql.sh` como root no WSL (Ubuntu), e esse script:
- instala o PostgreSQL 18 do repositório oficial (PGDG) se ele faltar, a mesma versão da esteira (`postgres:18.1`);
- sobe o cluster;
- garante o usuário `ntix` com `CREATEDB`, porque o `PostgreSqlTestDatabase` cria e apaga um banco por execução.

A senha é a `POSTGRES_PASSWORD` do `devops/.env`, que é ignorado pelo Git. Se ela faltar, o script gera uma e grava lá. A senha passa ao WSL pelo `WSLENV`, nunca pela linha de comando. O Windows chega ao banco por `localhost:5432` (encaminhamento do WSL2), e o script exporta `PostgreSqlConfiguration__ConnectionString` para os testes.

O resto da configuração vem dos próprios testes:
- integrados: `PostgreSqlFixture`, com configuração em memória;
- funcionais: `NtixApiHooks`, com variáveis de ambiente de teste, inclusive as chaves de cifragem e do JWT.

## Cuidados

- Ao chamar `wsl.exe` pelo Git Bash, mande o SQL ou o script **por arquivo**, nunca por `-c "..."`. O `$$` e as aspas são reinterpretados por dois shells, e o `psql` esperando senha no stdin trava o terminal. Redirecione `< /dev/null`.
- O aviso `your 131072x1 screen size is bogus` vem do `wsl.exe` e não é erro.
- Falha encontrada aqui é corrigida no código ou no teste, conforme a regra estipulada, e nunca afrouxando a asserção para passar.
