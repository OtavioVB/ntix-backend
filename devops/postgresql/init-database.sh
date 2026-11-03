#!/usr/bin/env bash
# Cria o banco, se não existir, e aplica os scripts idempotentes de data/ em ordem de caminho.
set -euo pipefail

DATABASE_HOST="${NTIX_DATABASE_HOST:-ntix-postgresql-local}"
DATABASE="${NTIX_DATABASE:-ntix}"
DATABASE_USER="${POSTGRES_USER:-ntix}"
DATA_DIR="${NTIX_DATA_DIR:-/ntix/data}"

export PGPASSWORD="$POSTGRES_PASSWORD"

# ON_ERROR_STOP faz o psql falhar no primeiro erro.
psql_command() {
    psql --host "$DATABASE_HOST" --username "$DATABASE_USER" --set ON_ERROR_STOP=1 --quiet "$@"
}

if [ -z "$(psql_command --dbname postgres --tuples-only --no-align --command "SELECT 1 FROM pg_database WHERE datname = '$DATABASE'")" ]; then
    psql_command --dbname postgres --command "CREATE DATABASE \"$DATABASE\""
fi

find "$DATA_DIR" -type f -name '*.sql' | LC_ALL=C sort | while read -r script; do
    echo "[ntix] Aplicando ${script#"$DATA_DIR"/}"
    psql_command --dbname "$DATABASE" --file "$script"
done

echo "[ntix] Banco '$DATABASE' pronto."
