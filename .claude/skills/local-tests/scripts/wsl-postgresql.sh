#!/usr/bin/env bash
# Roda DENTRO do WSL (Ubuntu), como root: instala o PostgreSQL 18 do repositório oficial (PGDG), a mesma versão da
# esteira (postgres:18.1), sobe o cluster e garante o usuário ntix com CREATEDB, porque os testes criam um banco por
# execução. A senha chega pela variável NTIX_PG_PASSWORD (repassada pelo WSLENV), nunca pela linha de comando.
set -euo pipefail

PG_VERSION=18

if [ -z "${NTIX_PG_PASSWORD:-}" ]; then
    echo "[ntix] NTIX_PG_PASSWORD não informada." >&2
    exit 1
fi

if [ ! -x "/usr/lib/postgresql/$PG_VERSION/bin/postgres" ]; then
    echo "[ntix] Instalando o PostgreSQL $PG_VERSION no WSL..."
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq postgresql-common ca-certificates >/dev/null
    /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y >/dev/null
    apt-get install -y -qq "postgresql-$PG_VERSION" >/dev/null
fi

# Sem systemd no WSL, pg_ctlcluster sobe o cluster direto; com systemd, ele delega ao serviço.
if ! pg_lsclusters --no-header | awk -v v="$PG_VERSION" '$1 == v && $2 == "main" && $4 == "online" { found = 1 } END { exit !found }'; then
    pg_ctlcluster "$PG_VERSION" main start
fi

# A senha vai como variável do psql (:'pw'), que a escapa: nada dela aparece no SQL nem nos processos.
runuser -u postgres -- psql -q -v ON_ERROR_STOP=1 -v pw="$NTIX_PG_PASSWORD" <<'SQL'
SELECT 'CREATE ROLE ntix LOGIN CREATEDB' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ntix')\gexec
ALTER ROLE ntix LOGIN CREATEDB PASSWORD :'pw';
SQL

echo "[ntix] PostgreSQL $PG_VERSION no ar em localhost:5432, usuário ntix."
