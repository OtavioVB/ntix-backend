/**
 * Cliente HTTP da API do ntix-backend. O contrato vem do OpenAPI da API (`/openapi/v1.json`);
 * os tipos de cada recurso ficam no módulo dele (ex.: `api/producers.ts`).
 */

/** Notificação devolvida pela API em todas as respostas de caso de uso (200 e 400). */
export type ApiNotification = { code: string; message: string; type?: unknown };

export type ApiResult =
  | { ok: true; status: number; notifications: ApiNotification[] }
  | { ok: false; status: number; notifications: ApiNotification[] };

const ORIGIN_SERVICE = "ntix-web";
const PRODUCTION_API_URL = "https://api.ntix.com.br";
const LOCAL_API_URL = "https://localhost:5000";
const LOCAL_HOSTS = ["localhost", "127.0.0.1", "[::1]"];

export const NETWORK_ERROR_CODE = "NETWORK_ERROR";
const NETWORK_ERROR_MESSAGE = "Não foi possível falar com a Ntix agora. Verifique sua conexão e tente de novo.";

export function apiBaseUrl(hostname = window.location.hostname): string {
  const configured = import.meta.env.VITE_API_BASE_URL as string | undefined;
  if (configured) return configured.replace(/\/$/, "");
  return LOCAL_HOSTS.includes(hostname) ? LOCAL_API_URL : PRODUCTION_API_URL;
}

/**
 * Headers auditáveis exigidos pela API (AuditableInfoValueObject): X-Correlation-Id (UUID), X-Origin-Service e
 * X-Requested-At (UTC com milissegundos). X-Execution-User só quando houver usuário autenticado.
 */
export function auditableHeaders(executionUser?: string): Record<string, string> {
  const headers: Record<string, string> = {
    "X-Correlation-Id": crypto.randomUUID(),
    "X-Origin-Service": ORIGIN_SERVICE,
    "X-Requested-At": new Date().toISOString(),
  };
  if (executionUser) headers["X-Execution-User"] = executionUser;
  return headers;
}

function toNotifications(body: unknown): ApiNotification[] {
  if (!Array.isArray(body)) return [];
  return body.filter((item): item is ApiNotification => typeof item?.code === "string" && typeof item?.message === "string");
}

export async function postJson(path: string, body: unknown, options: { executionUser?: string; signal?: AbortSignal } = {}): Promise<ApiResult> {
  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl()}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...auditableHeaders(options.executionUser) },
      body: JSON.stringify(body),
      signal: options.signal,
    });
  } catch {
    return { ok: false, status: 0, notifications: [{ code: NETWORK_ERROR_CODE, message: NETWORK_ERROR_MESSAGE }] };
  }

  const notifications = toNotifications(await response.json().catch(() => null));
  if (response.ok) return { ok: true, status: response.status, notifications };

  // 500 e afins não trazem notificação: a pessoa recebe a mesma mensagem de falha de conexão.
  return {
    ok: false,
    status: response.status,
    notifications: notifications.length ? notifications : [{ code: NETWORK_ERROR_CODE, message: NETWORK_ERROR_MESSAGE }],
  };
}

/** Resultado de endpoint que devolve dados no sucesso (ex.: integrações) e `Notification[]` na recusa. */
export type ApiDataResult<T> = { ok: true; status: number; data: T } | { ok: false; status: number; notifications: ApiNotification[] };

export const UNAUTHORIZED_CODE = "UNAUTHORIZED";
const UNAUTHORIZED_MESSAGE = "Sua sessão expirou. Entre de novo para continuar.";

export const FORBIDDEN_CODE = "FORBIDDEN";
const FORBIDDEN_MESSAGE = "Você não tem acesso a esta produtora.";

/**
 * Chamada autenticada do painel do produtor (REQ5): `Authorization: Bearer` com o access token, sem X-Execution-User,
 * porque a API tira o usuário do token. 401 e 403 viram notificações próprias, já que a API responde sem corpo.
 */
export async function requestJson<T>(
  method: "GET" | "POST" | "PUT",
  path: string,
  options: { body?: unknown; accessToken?: string; signal?: AbortSignal } = {},
): Promise<ApiDataResult<T>> {
  const headers: Record<string, string> = { ...auditableHeaders() };
  if (options.body !== undefined) headers["Content-Type"] = "application/json";
  if (options.accessToken) headers.Authorization = `Bearer ${options.accessToken}`;

  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl()}${path}`, {
      method,
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch {
    return { ok: false, status: 0, notifications: [{ code: NETWORK_ERROR_CODE, message: NETWORK_ERROR_MESSAGE }] };
  }

  const body = await response.json().catch(() => null);
  if (response.ok) return { ok: true, status: response.status, data: body as T };

  if (response.status === 401) return { ok: false, status: 401, notifications: [{ code: UNAUTHORIZED_CODE, message: UNAUTHORIZED_MESSAGE }] };
  if (response.status === 403) return { ok: false, status: 403, notifications: [{ code: FORBIDDEN_CODE, message: FORBIDDEN_MESSAGE }] };

  const notifications = toNotifications(body);
  return {
    ok: false,
    status: response.status,
    notifications: notifications.length ? notifications : [{ code: NETWORK_ERROR_CODE, message: NETWORK_ERROR_MESSAGE }],
  };
}
