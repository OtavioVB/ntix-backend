#!/usr/bin/env bash
# Baixa o OpenAPI da WebApi em execução para docs/openapi/v1.json e lista as operações.
# Uso: bash .claude/skills/frontend-integration/scripts/fetch-openapi.sh [base-url]
# Padrão: https://localhost:5000 (perfil https do launchSettings). -k aceita o certificado de desenvolvimento.
set -euo pipefail

BASE_URL="${1:-https://localhost:5000}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
OUTPUT="$REPO_ROOT/docs/openapi/v1.json"

mkdir -p "$(dirname "$OUTPUT")"
curl --silent --show-error --fail -k "$BASE_URL/openapi/v1.json" -o "$OUTPUT"

echo "OpenAPI salvo em ${OUTPUT#"$REPO_ROOT"/}"
echo "Operações:"
node -e '
const spec = require(process.argv[1]);
for (const [path, item] of Object.entries(spec.paths ?? {}))
  for (const method of Object.keys(item))
    console.log(`  ${method.toUpperCase().padEnd(6)} ${path}`);
' "$OUTPUT"
