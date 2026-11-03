#!/usr/bin/env bash
# Sobrescreve a configuração do nginx com a pasta gateway/ do repositório e recarrega.
# Emite o certificado HTTPS do IP público na primeira vez. Uso: apply-gateway.sh <pasta> <ip público>
set -euo pipefail

GATEWAY_DIR="${1:?Informe a pasta com os arquivos do gateway}"
PUBLIC_IP="${2:?Informe o IP público do servidor}"
# 80: validação do Let's Encrypt. 8080: API. 8081: frontend.
GATEWAY_PORTS=(80 8080 8081)
NGINX_CONF_DIR="/etc/nginx/conf.d"
ACME_WEBROOT="/var/www/acme"
CERT_NAME="ntix"
CERT_DIR="/etc/letsencrypt/live/$CERT_NAME"
if [ "$(id -u)" -eq 0 ]; then SUDO=(); else SUDO=(sudo -n); fi

# Os deploys da API e do frontend rodam em paralelo: um aplica o gateway por vez.
exec 9>/tmp/ntix-gateway.lock
flock 9

BACKUP_DIR="$(mktemp -d)"
trap '"${SUDO[@]}" rm -rf "$BACKUP_DIR"' EXIT

# Libera as portas do gateway no firewall do host e persiste as regras entre reinicializações.
for port in "${GATEWAY_PORTS[@]}"; do
    if ! "${SUDO[@]}" iptables -C INPUT -p tcp --dport "$port" -m state --state NEW -j ACCEPT 2>/dev/null; then
        "${SUDO[@]}" iptables -I INPUT 1 -p tcp --dport "$port" -m state --state NEW -j ACCEPT
        if command -v netfilter-persistent >/dev/null; then
            "${SUDO[@]}" netfilter-persistent save
        fi
    fi
done

# O site padrão do pacote também escuta na 80 e disputaria a validação com acme.conf.
"${SUDO[@]}" rm -f /etc/nginx/sites-enabled/default
"${SUDO[@]}" install -d -m 755 "$ACME_WEBROOT"
"${SUDO[@]}" cp -a "$NGINX_CONF_DIR/." "$BACKUP_DIR/"

restore_backup() {
    "${SUDO[@]}" find "$NGINX_CONF_DIR" -mindepth 1 -delete
    "${SUDO[@]}" cp -a "$BACKUP_DIR/." "$NGINX_CONF_DIR/"
    "${SUDO[@]}" nginx -t && "${SUDO[@]}" systemctl reload nginx || true
}

# Substitui a pasta por inteiro: arquivos removidos do repositório não podem continuar no servidor.
replace_config() {
    "${SUDO[@]}" find "$NGINX_CONF_DIR" -mindepth 1 -delete
    "${SUDO[@]}" install -m 644 "$@" "$NGINX_CONF_DIR/"
    if ! "${SUDO[@]}" nginx -t; then
        echo "[ntix] Configuração do gateway inválida, restaurando a anterior." >&2
        restore_backup
        exit 1
    fi
    "${SUDO[@]}" systemctl enable --now nginx
    "${SUDO[@]}" systemctl reload nginx
}

# Primeira emissão: o nginx não sobe com 'listen ssl' sem certificado, então serve só a validação até emitir.
if ! "${SUDO[@]}" test -f "$CERT_DIR/fullchain.pem"; then
    echo "[ntix] Emitindo o certificado HTTPS para $PUBLIC_IP..."
    replace_config "$GATEWAY_DIR/acme.conf"
    if ! "${SUDO[@]}" certbot certonly --webroot --webroot-path "$ACME_WEBROOT" \
        --cert-name "$CERT_NAME" --ip-address "$PUBLIC_IP" --preferred-profile shortlived \
        --non-interactive --agree-tos --register-unsafely-without-email \
        --deploy-hook "systemctl reload nginx"; then
        echo "[ntix] Falha ao emitir o certificado. A porta 80 está liberada no firewall da nuvem?" >&2
        restore_backup
        exit 1
    fi
fi

replace_config "$GATEWAY_DIR"/*.conf

echo "[ntix] Gateway aplicado."
