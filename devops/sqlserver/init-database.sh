#!/usr/bin/env bash
# Cria o banco, se não existir, e aplica os scripts idempotentes de data/ em ordem de caminho.
set -euo pipefail

SQLCMD=/opt/mssql-tools18/bin/sqlcmd
SQL_HOST="${NTIX_SQL_HOST:-ntix-sqlserver-local}"
DATABASE="${NTIX_DATABASE:-ntix}"
DATA_DIR="${NTIX_DATA_DIR:-/ntix/data}"

# -C confia no certificado autoassinado; -b falha no primeiro erro; -f 65001 lê os scripts em UTF-8.
sql() {
    "$SQLCMD" -S "$SQL_HOST" -U sa -P "$MSSQL_SA_PASSWORD" -C -b -f 65001 "$@"
}

sql -Q "IF DB_ID(N'$DATABASE') IS NULL CREATE DATABASE [$DATABASE];"

find "$DATA_DIR" -type f -name '*.sql' | LC_ALL=C sort | while read -r script; do
    echo "[ntix] Aplicando ${script#"$DATA_DIR"/}"
    sql -d "$DATABASE" -i "$script"
done

echo "[ntix] Banco '$DATABASE' pronto."
