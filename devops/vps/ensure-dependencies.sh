#!/usr/bin/env bash
# Verifica o que o deploy precisa na VPS e instala o que faltar. Pode ser executado a cada deploy.
set -euo pipefail

COMPOSE_VERSION="v5.6.1"
COMPOSE_PLUGIN_DIR="/usr/local/lib/docker/cli-plugins"

if [ "$(id -u)" -eq 0 ]; then
    SUDO=()
elif sudo -n true 2>/dev/null; then
    SUDO=(sudo -n)
else
    SUDO=()
fi

require_root_access() {
    if [ "$(id -u)" -ne 0 ] && [ "${#SUDO[@]}" -eq 0 ]; then
        echo "[ntix] Falta instalar $1, e o usuário '$(id -un)' não é root nem tem sudo sem senha." >&2
        exit 1
    fi
}

install_package() {
    require_root_access "$1"

    if command -v apt-get >/dev/null; then
        "${SUDO[@]}" env DEBIAN_FRONTEND=noninteractive apt-get update -qq
        "${SUDO[@]}" env DEBIAN_FRONTEND=noninteractive apt-get install -y -qq "$1"
    elif command -v dnf >/dev/null; then
        "${SUDO[@]}" dnf install -y -q "$1"
    elif command -v yum >/dev/null; then
        "${SUDO[@]}" yum install -y -q "$1"
    else
        echo "[ntix] Gerenciador de pacotes não suportado: instale $1 manualmente." >&2
        exit 1
    fi
}

for package in curl tar gzip; do
    if ! command -v "$package" >/dev/null; then
        echo "[ntix] Instalando $package..."
        install_package "$package"
    fi
done

if ! command -v docker >/dev/null; then
    echo "[ntix] Instalando o Docker..."
    require_root_access "o Docker"
    curl -fsSL https://get.docker.com | "${SUDO[@]}" sh
fi

if ! docker info >/dev/null 2>&1; then
    if [ "$(id -u)" -ne 0 ] && [ "${#SUDO[@]}" -gt 0 ]; then
        "${SUDO[@]}" systemctl enable --now docker
        "${SUDO[@]}" usermod -aG docker "$(id -un)"
    elif [ "$(id -u)" -eq 0 ]; then
        systemctl enable --now docker
    fi
fi

if ! docker compose version >/dev/null 2>&1 && ! "${SUDO[@]}" docker compose version >/dev/null 2>&1; then
    echo "[ntix] Instalando o Docker Compose $COMPOSE_VERSION..."
    require_root_access "o Docker Compose"

    temporary_dir="$(mktemp -d)"
    trap 'rm -rf "$temporary_dir"' EXIT

    file_name="docker-compose-linux-$(uname -m)"
    base_url="https://github.com/docker/compose/releases/download/$COMPOSE_VERSION"

    curl -fsSL "$base_url/$file_name" -o "$temporary_dir/$file_name"
    curl -fsSL "$base_url/$file_name.sha256" -o "$temporary_dir/$file_name.sha256"
    (cd "$temporary_dir" && sha256sum -c "$file_name.sha256")

    "${SUDO[@]}" install -d -m 755 "$COMPOSE_PLUGIN_DIR"
    "${SUDO[@]}" install -m 755 "$temporary_dir/$file_name" "$COMPOSE_PLUGIN_DIR/docker-compose"
fi

# Verificação final em uma nova sessão: a entrada no grupo docker só vale a partir do próximo login.
echo "[ntix] Dependências verificadas."
