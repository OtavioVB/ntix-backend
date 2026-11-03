/**
 * Modo de integração com a API. Por padrão o front roda em demonstração, sem chamar o backend.
 * `?api=real` em qualquer endereço liga as requisições reais; `?api=demo` volta para a demonstração.
 * A escolha fica na aba (sessionStorage), para sobreviver à navegação entre rotas, que descarta a query.
 */
export type ApiMode = "demo" | "real";

export const API_MODE_PARAM = "api";
const STORAGE_KEY = "ntix:api-mode";

function isApiMode(value: string | null): value is ApiMode {
  return value === "demo" || value === "real";
}

function readStored(): string | null {
  try {
    return window.sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function store(mode: ApiMode) {
  try {
    window.sessionStorage.setItem(STORAGE_KEY, mode);
  } catch {
    // Sem storage (aba privada, bloqueio): o modo vale só enquanto a query estiver na URL.
  }
}

export function getApiMode(search = window.location.search): ApiMode {
  const requested = new URLSearchParams(search).get(API_MODE_PARAM);

  if (isApiMode(requested)) {
    store(requested);
    return requested;
  }

  const stored = readStored();
  return isApiMode(stored) ? stored : "demo";
}

export function isRealApi(search?: string): boolean {
  return getApiMode(search) === "real";
}
