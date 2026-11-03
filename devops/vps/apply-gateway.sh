#!/usr/bin/env bash
# Sobrescreve a configuração do nginx com a pasta gateway/ do repositório e recarrega. Uso: apply-gateway.sh <pasta>
set -euo pipefail

GATEWAY_DIR="${1:?Informe a pasta com os arquivos do gateway}"
# 8080: API. 8081: frontend.
GATEWAY_PORTS=(8080 8081)
NGINX_CONF_DIR="/etc/nginx/conf.d"
if [ "$(id -u)" -eq 0 ]; then SUDO=(); else SUDO=(sudo -n); fi

# Os deploys da API e do frontend rodam em paralelo: um aplica o gateway por vez.
exec 9>/tmp/ntix-gateway.lock
flock 9

BACKUP_DIR="$(mktemp -d)"
trap '"${SUDO[@]}" rm -rf "$BACKUP_DIR"' EXIT

# Substitui a pasta por inteiro: arquivos removidos do repositório não podem continuar no servidor.
"${SUDO[@]}" cp -a "$NGINX_CONF_DIR/." "$BACKUP_DIR/"
"${SUDO[@]}" find "$NGINX_CONF_DIR" -mindepth 1 -delete
"${SUDO[@]}" install -m 644 "$GATEWAY_DIR"/*.conf "$NGINX_CONF_DIR/"

if ! "${SUDO[@]}" nginx -t; then
    echo "[ntix] Configuração do gateway inválida, restaurando a anterior." >&2
    "${SUDO[@]}" find "$NGINX_CONF_DIR" -mindepth 1 -delete
    "${SUDO[@]}" cp -a "$BACKUP_DIR/." "$NGINX_CONF_DIR/"
    exit 1
fi

"${SUDO[@]}" systemctl enable --now nginx
"${SUDO[@]}" systemctl reload nginx

# Libera as portas do gateway no firewall do host e persiste as regras entre reinicializações.
for port in "${GATEWAY_PORTS[@]}"; do
    if ! "${SUDO[@]}" iptables -C INPUT -p tcp --dport "$port" -m state --state NEW -j ACCEPT 2>/dev/null; then
        "${SUDO[@]}" iptables -I INPUT 1 -p tcp --dport "$port" -m state --state NEW -j ACCEPT
        if command -v netfilter-persistent >/dev/null; then
            "${SUDO[@]}" netfilter-persistent save
        fi
    fi
done

echo "[ntix] Gateway aplicado."
