#!/usr/bin/env bash
# Sobrescreve a configuração do nginx com a pasta gateway/ do repositório e recarrega.
# Emite o certificado HTTPS dos domínios quando ele não existe ou não cobre todos. Uso: apply-gateway.sh <pasta>
set -euo pipefail

GATEWAY_DIR="${1:?Informe a pasta com os arquivos do gateway}"
# Precisam apontar para este servidor no DNS antes da emissão: o Let's Encrypt valida cada um pela porta 80.
CERT_DOMAINS=(ntix.com.br www.ntix.com.br api.ntix.com.br produtores.ntix.com.br)
CERT_NAME="ntix.com.br"
CERT_FILE="/etc/letsencrypt/live/$CERT_NAME/fullchain.pem"
# 80: validação do Let's Encrypt e redirecionamento. 443: HTTPS.
GATEWAY_PORTS=(80 443)
NGINX_CONF_DIR="/etc/nginx/conf.d"
ACME_WEBROOT="/var/www/acme"
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
# O nginx.conf do pacote ainda aceita TLS 1.0 e 1.1. A diretiva fica lá (redefinir em conf.d é erro), restrita aqui.
"${SUDO[@]}" sed -i -E 's/^(\s*)ssl_protocols .*;/\1ssl_protocols TLSv1.2 TLSv1.3;/' /etc/nginx/nginx.conf
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

certificate_covers_all_domains() {
    local names
    names="$("${SUDO[@]}" openssl x509 -in "$CERT_FILE" -noout -ext subjectAltName 2>/dev/null)" || return 1
    for domain in "${CERT_DOMAINS[@]}"; do
        grep -q "DNS:$domain\(,\|$\)" <<<"$names" || return 1
    done
}

# Emissão ou domínio novo. Sem certificado nenhum, o nginx não sobe com 'listen ssl', então serve só a validação.
# Com um certificado que só não cobre um nome novo, a configuração atual já atende a validação e o HTTPS segue no ar.
if ! certificate_covers_all_domains; then
    echo "[ntix] Emitindo o certificado HTTPS para ${CERT_DOMAINS[*]}..."
    if ! "${SUDO[@]}" test -f "$CERT_FILE"; then
        replace_config "$GATEWAY_DIR/acme.conf"
    fi
    domain_args=()
    for domain in "${CERT_DOMAINS[@]}"; do domain_args+=(-d "$domain"); done
    if ! "${SUDO[@]}" certbot certonly --webroot --webroot-path "$ACME_WEBROOT" \
        --cert-name "$CERT_NAME" "${domain_args[@]}" --expand \
        --non-interactive --agree-tos --register-unsafely-without-email \
        --deploy-hook "systemctl reload nginx"; then
        echo "[ntix] Falha ao emitir o certificado. Os domínios apontam para este servidor e a porta 80 está liberada?" >&2
        restore_backup
        exit 1
    fi
fi

replace_config "$GATEWAY_DIR"/*.conf

echo "[ntix] Gateway aplicado."
