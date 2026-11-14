import { requestJson, type ApiDataResult } from "./client";

/** `CreateProducerSessionV1Request` do OpenAPI. */
export type CreateProducerSessionRequest = { email: string; password: string };

/** `RefreshProducerSessionV1Request` e `RevokeProducerSessionV1Request` do OpenAPI. */
export type ProducerSessionTokenRequest = { refreshToken: string };

/** Resposta do login e da renovação (REQ5). */
export type ProducerSessionResponse = {
  accessToken: string;
  /** Segundos: 900 (15 minutos). */
  accessTokenExpiresIn: number;
  refreshToken: string;
  /** Segundos: 604800 (7 dias). */
  refreshTokenExpiresIn: number;
  user: { id: string; name: string; userType: "ADMINISTRATOR"; status: "PENDING" | "ACTIVE" | "BLOCKED" };
  producer: { id: string; slug: string; name: string; status: "PENDING" | "PENDING_APPROVAL" | "ACTIVE" };
};

export const PRODUCER_SESSIONS_ROUTE = "/api/v1/producers/sessions";

export const INVALID_CREDENTIALS_CODE = "INVALID_CREDENTIALS";
export const PRODUCER_USER_BLOCKED_CODE = "PRODUCER_USER_BLOCKED";
export const TOO_MANY_LOGIN_ATTEMPTS_CODE = "TOO_MANY_LOGIN_ATTEMPTS";
export const INVALID_REFRESH_TOKEN_CODE = "INVALID_REFRESH_TOKEN";

/** POST /api/v1/producers/sessions: login com e-mail e senha. A senha vai só no corpo, nunca fica no navegador. */
export function createProducerSession(request: CreateProducerSessionRequest, signal?: AbortSignal): Promise<ApiDataResult<ProducerSessionResponse>> {
  return requestJson("POST", PRODUCER_SESSIONS_ROUTE, { body: request, signal });
}

/** POST /api/v1/producers/sessions/refresh: troca o refresh token por um par novo; o usado deixa de valer. */
export function refreshProducerSession(request: ProducerSessionTokenRequest): Promise<ApiDataResult<ProducerSessionResponse>> {
  return requestJson("POST", `${PRODUCER_SESSIONS_ROUTE}/refresh`, { body: request });
}

/** POST /api/v1/producers/sessions/revoke: logout; 204 também para token já revogado. */
export function revokeProducerSession(request: ProducerSessionTokenRequest): Promise<ApiDataResult<null>> {
  return requestJson("POST", `${PRODUCER_SESSIONS_ROUTE}/revoke`, { body: request });
}
