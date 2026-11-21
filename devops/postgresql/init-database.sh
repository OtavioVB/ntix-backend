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

# Usuário do PgAdmin (database.ntix.com.br), separado do usuário ntix da API.
# pg_read_all_data dá SELECT e USAGE em todos os schemas, inclusive tabelas futuras, sem GRANT por tabela.
# A única escrita é UPDATE em producers.producer, para aprovar o produtor (status ACTIVE) enquanto não há backoffice.
# INSERT, DELETE e DDL seguem negados em tudo. A sessão não nasce read-only, para a edição pela grade do PgAdmin.
# A senha é reaplicada a cada execução, então trocar o secret troca a senha. Sem senha (ambiente local), não é criado.
if [ -n "${PGADMIN_DATABASE_PASSWORD:-}" ]; then
    psql_command --dbname "$DATABASE" --set pgadmin_password="$PGADMIN_DATABASE_PASSWORD" <<'SQL'
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ntix_pgadmin') THEN
        CREATE ROLE ntix_pgadmin;
    END IF;
END
$$;
ALTER ROLE ntix_pgadmin WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'pgadmin_password';
ALTER ROLE ntix_pgadmin RESET default_transaction_read_only;
GRANT pg_read_all_data TO ntix_pgadmin;
GRANT UPDATE ON producers.producer TO ntix_pgadmin;
SQL
    echo "[ntix] Usuário 'ntix_pgadmin' pronto: leitura em tudo e UPDATE em producers.producer."
fi

echo "[ntix] Banco '$DATABASE' pronto."
