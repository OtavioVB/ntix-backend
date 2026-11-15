#!/usr/bin/env bash
# Roda os testes do ntix-backend na máquina local (Windows + Git Bash), como a esteira roda.
#
# Uso: bash .claude/skills/local-tests/scripts/run-tests.sh [unit|integrated|functional|web|all|coverage] [filtro do dotnet test]
#   bash .claude/skills/local-tests/scripts/run-tests.sh all
#   bash .claude/skills/local-tests/scripts/run-tests.sh integrated "FullyQualifiedName~Payment"
#   bash .claude/skills/local-tests/scripts/run-tests.sh coverage   # cobertura como o Sonar mede (unitários, integrados e funcionais)
#
# Integrados e funcionais precisam de PostgreSQL: o script sobe o PostgreSQL 18 no WSL (wsl-postgresql.sh) e aponta
# PostgreSqlConfiguration__ConnectionString para ele. A senha é a POSTGRES_PASSWORD do devops/.env (ignorado pelo
# Git); se faltar, é gerada e gravada lá.
set -euo pipefail

SUITE="${1:-all}"
FILTER="${2:-}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
ENV_FILE="$ROOT/devops/.env"
CONFIGURATION="${BUILD_CONFIGURATION:-Debug}"

read_env() {
    [ -f "$ENV_FILE" ] && grep -E "^$1=" "$ENV_FILE" | tail -1 | cut -d= -f2- | tr -d "'\"\r" || true
}

ensure_postgresql() {
    local password
    password="$(read_env POSTGRES_PASSWORD)"

    if [ -z "$password" ]; then
        password="$(openssl rand -hex 16)"
        touch "$ENV_FILE"
        sed -i '/^POSTGRES_PASSWORD=/d' "$ENV_FILE"
        echo "POSTGRES_PASSWORD=$password" >> "$ENV_FILE"
        echo "[ntix] POSTGRES_PASSWORD gerada em devops/.env."
    fi

    # Caminho do script visto de dentro do WSL (C:\... -> /mnt/c/...).
    local script
    script="$(cygpath -m "$ROOT/.claude/skills/local-tests/scripts/wsl-postgresql.sh" | sed -E 's|^([A-Za-z]):|/mnt/\L\1|')"

    # MSYS_NO_PATHCONV: sem ele, o Git Bash reescreve /mnt/c/... como C:/Program Files/Git/mnt/c/... para o wsl.exe.
    NTIX_PG_PASSWORD="$password" WSLENV="NTIX_PG_PASSWORD/u" MSYS_NO_PATHCONV=1 wsl.exe -u root -- bash "$script" < /dev/null

    # O WSL desliga a distribuição alguns segundos depois do último wsl.exe sair, levando o PostgreSQL junto.
    # Uma sessão parada mantém tudo no ar até o fim dos testes.
    if [ -z "${KEEPALIVE_PID:-}" ]; then
        wsl.exe -u root -- sleep 86400 < /dev/null > /dev/null 2>&1 &
        KEEPALIVE_PID=$!
        trap 'kill "$KEEPALIVE_PID" 2>/dev/null || true' EXIT
    fi

    export PostgreSqlConfiguration__ConnectionString="Host=localhost;Port=5432;Database=ntix;Username=ntix;Password=$password"
}

run_dotnet() {
    local project="$1"
    local args=(test "$ROOT/tsts/$project/$project.csproj" --configuration "$CONFIGURATION" --logger "console;verbosity=normal")

    [ -n "$FILTER" ] && args+=(--filter "$FILTER")

    echo "[ntix] dotnet ${args[*]}"
    dotnet "${args[@]}"
}

run_web() {
    (cd "$ROOT/web" && npm ci --no-audit --no-fund >/dev/null && npx tsc --noEmit && npx vitest run)
}

case "$SUITE" in
    unit) run_dotnet UnitTests ;;
    integrated) ensure_postgresql; run_dotnet IntegratedTests ;;
    functional) ensure_postgresql; run_dotnet FunctionalTests ;;
    web) run_web ;;
    all)
        run_dotnet UnitTests
        ensure_postgresql
        run_dotnet IntegratedTests
        run_dotnet FunctionalTests
        run_web
        ;;
    coverage)
        # Como a esteira (static-analysis.yml): unitários, integrados e funcionais entram na cobertura do Sonar.
        rm -rf "$ROOT/TestResults/coverage"
        ensure_postgresql
        for project in UnitTests IntegratedTests FunctionalTests; do
            dotnet test "$ROOT/tsts/$project/$project.csproj" --configuration "$CONFIGURATION" \
                --results-directory "$ROOT/TestResults/coverage" --collect:"XPlat Code Coverage" \
                --settings "$ROOT/.claude/skills/local-tests/scripts/coverage.runsettings"
        done
        node "$ROOT/.claude/skills/local-tests/scripts/coverage-summary.mjs" "$ROOT"
        ;;
    *) echo "Uso: $0 [unit|integrated|functional|web|all|coverage] [filtro]" >&2; exit 2 ;;
esac
