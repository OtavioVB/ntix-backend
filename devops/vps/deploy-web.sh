#!/usr/bin/env bash
# Instala as dependências do frontend, gera o build e publica em /var/www/ntix-web. Uso: deploy-web.sh <pasta do web/>
set -euo pipefail

WEB_DIR="${1:?Informe a pasta do frontend}"
PUBLISH_DIR="/var/www/ntix-web"
if [ "$(id -u)" -eq 0 ]; then SUDO=(); else SUDO=(sudo -n); fi

cd "$WEB_DIR"

echo "[ntix] Node $(node --version), npm $(npm --version)"
npm ci --no-audit --no-fund
npm run build

# Troca a pasta publicada de uma vez: o nginx nunca serve um build pela metade.
"${SUDO[@]}" rm -rf "$PUBLISH_DIR.new" "$PUBLISH_DIR.old"
"${SUDO[@]}" cp -r dist "$PUBLISH_DIR.new"
"${SUDO[@]}" chmod -R a+rX "$PUBLISH_DIR.new"
if [ -d "$PUBLISH_DIR" ]; then
    "${SUDO[@]}" mv "$PUBLISH_DIR" "$PUBLISH_DIR.old"
fi
"${SUDO[@]}" mv "$PUBLISH_DIR.new" "$PUBLISH_DIR"
"${SUDO[@]}" rm -rf "$PUBLISH_DIR.old"

echo "[ntix] Frontend publicado."
