import { requestJson, type ApiDataResult } from "./client";
import { refreshProducerSession, revokeProducerSession, type ProducerSessionResponse } from "./sessions";

/**
 * Tokens da sessão do produtor na API real (REQ5).
 * - Access token (15 min): só em memória. Some ao recarregar a página e volta pela renovação.
 * - Refresh token (7 dias, rotativo): no localStorage, para a sessão sobreviver ao recarregamento (Decisões, item 2).
 *   A API troca o token a cada renovação e encerra a sessão se um token já trocado for reapresentado.
 * A senha nunca passa por aqui.
 */
export const REFRESH_TOKEN_STORAGE_KEY = "ntix.producer.refresh-token.v1";

export type AuthEvent = { type: "refreshed"; session: ProducerSessionResponse } | { type: "expired" };

let accessToken: string | undefined;
let refreshing: Promise<boolean> | null = null;
const listeners = new Set<(event: AuthEvent) => void>();

export function getAccessToken(): string | undefined {
  return accessToken;
}

export function readRefreshToken(): string | null {
  try {
    return window.localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

/** Guarda o par devolvido pelo login ou pela renovação. */
export function storeSessionTokens(session: Pick<ProducerSessionResponse, "accessToken" | "refreshToken">) {
  accessToken = session.accessToken;
  try {
    window.localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, session.refreshToken);
  } catch {
    // Sem armazenamento, a sessão vale só enquanto a página estiver aberta.
  }
}

export function clearSessionTokens() {
  accessToken = undefined;
  try {
    window.localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    // Nada a apagar.
  }
}

/** A sessão do produtor ouve a renovação (slug e status atuais) e a expiração (volta para /entrar). */
export function subscribeAuth(listener: (event: AuthEvent) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function emit(event: AuthEvent) {
  for (const listener of listeners) listener(event);
}

function expire() {
  clearSessionTokens();
  emit({ type: "expired" });
}

/**
 * Renova a sessão uma vez, mesmo com várias chamadas recusadas ao mesmo tempo: duas renovações com o mesmo token seriam
 * reuso, e a API encerraria a sessão. Falha de rede não encerra a sessão; 401 e 403 encerram.
 */
export function refreshAccessToken(): Promise<boolean> {
  refreshing ??= (async () => {
    const refreshToken = readRefreshToken();
    if (!refreshToken) {
      expire();
      return false;
    }

    const result = await refreshProducerSession({ refreshToken });
    if (result.ok) {
      storeSessionTokens(result.data);
      emit({ type: "refreshed", session: result.data });
      return true;
    }

    if (result.status === 401 || result.status === 403) expire();
    return false;
  })().finally(() => {
    refreshing = null;
  });

  return refreshing;
}

/**
 * Chamada do painel com o access token. No 401, renova uma vez e repete; se a renovação falhar, a sessão expira e o
 * painel volta para /entrar.
 */
export async function authorizedRequest<T>(
  method: "GET" | "POST" | "PUT",
  path: string,
  options: { body?: unknown; signal?: AbortSignal } = {},
): Promise<ApiDataResult<T>> {
  const first = await requestJson<T>(method, path, { ...options, accessToken });
  if (first.ok || first.status !== 401) return first;

  if (!(await refreshAccessToken())) return first;

  const retried = await requestJson<T>(method, path, { ...options, accessToken });
  if (!retried.ok && retried.status === 401) expire();
  return retried;
}

/** Sair: revoga a sessão na API e apaga os tokens deste navegador, mesmo se a API não responder. */
export async function signOut(): Promise<void> {
  const refreshToken = readRefreshToken();
  clearSessionTokens();
  if (refreshToken) await revokeProducerSession({ refreshToken });
}
