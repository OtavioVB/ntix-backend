import { postJson, requestJson, type ApiDataResult, type ApiResult } from "./client";

/**
 * Sessão do comprador na API real (REQ12), separada da do produtor (`api/auth.ts`).
 * - Access token (15 min): só em memória.
 * - Refresh token (30 dias, rotativo): no localStorage, para a sessão sobreviver ao recarregamento.
 * A senha vai só no corpo do login e do cadastro, nunca fica no navegador.
 */
export const CUSTOMER_REFRESH_TOKEN_STORAGE_KEY = "ntix.customer.refresh-token.v1";

export const CUSTOMER_SESSIONS_ROUTE = "/api/v1/customers/sessions";

export type CustomerSessionResponse = {
  accessToken: string;
  accessTokenExpiresIn: number;
  refreshToken: string;
  refreshTokenExpiresIn: number;
  customer: { id: string; firstName: string };
};

/** `GET /api/v1/customers/me`: o CPF vem ofuscado. */
export type CurrentCustomer = { id: string; fullName: string; email: string; document: string; phoneNumber?: string };

/** `CreateCustomerV1Request` do OpenAPI (cadastro, já existente). */
export type CreateCustomerRequest = { fullName: string; email: string; document: string; password: string; phoneNumber?: string };

let accessToken: string | undefined;
let refreshing: Promise<boolean> | null = null;

export function getCustomerAccessToken(): string | undefined {
  return accessToken;
}

export function readCustomerRefreshToken(): string | null {
  try {
    return window.localStorage.getItem(CUSTOMER_REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function storeCustomerTokens(session: Pick<CustomerSessionResponse, "accessToken" | "refreshToken">) {
  accessToken = session.accessToken;
  try {
    window.localStorage.setItem(CUSTOMER_REFRESH_TOKEN_STORAGE_KEY, session.refreshToken);
  } catch {
    // Sem armazenamento, a sessão vale só enquanto a página estiver aberta.
  }
}

export function clearCustomerTokens() {
  accessToken = undefined;
  try {
    window.localStorage.removeItem(CUSTOMER_REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    // Nada a apagar.
  }
}

/** POST /api/v1/customers: cadastro anônimo (já existente). Responde com as notificações. */
export function registerCustomer(request: CreateCustomerRequest): Promise<ApiResult> {
  return postJson("/api/v1/customers", request);
}

/** POST /api/v1/customers/sessions: login com e-mail ou CPF e senha. */
export function createCustomerSession(login: string, password: string): Promise<ApiDataResult<CustomerSessionResponse>> {
  return requestJson("POST", CUSTOMER_SESSIONS_ROUTE, { body: { login, password } });
}

/** Renova uma vez, mesmo com várias chamadas recusadas ao mesmo tempo: duas renovações com o mesmo token seriam reuso. */
export function refreshCustomerAccessToken(): Promise<boolean> {
  refreshing ??= (async () => {
    const refreshToken = readCustomerRefreshToken();
    if (!refreshToken) return false;

    const result = await requestJson<CustomerSessionResponse>("POST", `${CUSTOMER_SESSIONS_ROUTE}/refresh`, { body: { refreshToken } });
    if (result.ok) {
      storeCustomerTokens(result.data);
      return true;
    }
    if (result.status === 401 || result.status === 403) clearCustomerTokens();
    return false;
  })().finally(() => {
    refreshing = null;
  });

  return refreshing;
}

/** Chamada do comprador com o access token: no 401, renova uma vez e repete. */
export async function customerAuthorizedRequest<T>(method: "GET" | "POST" | "PUT", path: string, options: { body?: unknown; signal?: AbortSignal } = {}): Promise<ApiDataResult<T>> {
  const first = await requestJson<T>(method, path, { ...options, accessToken });
  if (first.ok || first.status !== 401) return first;

  if (!(await refreshCustomerAccessToken())) return first;

  return requestJson<T>(method, path, { ...options, accessToken });
}

export function getCurrentCustomer(signal?: AbortSignal): Promise<ApiDataResult<CurrentCustomer>> {
  return customerAuthorizedRequest("GET", "/api/v1/customers/me", { signal });
}

/** Sair: revoga a sessão na API e apaga os tokens deste navegador, mesmo se a API não responder. */
export async function signOutCustomer(): Promise<void> {
  const refreshToken = readCustomerRefreshToken();
  clearCustomerTokens();
  if (refreshToken) await requestJson("POST", `${CUSTOMER_SESSIONS_ROUTE}/revoke`, { body: { refreshToken } }).catch(() => undefined);
}
