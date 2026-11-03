import { postJson, type ApiResult } from "./client";

/** `CreateProducerV1Request` do OpenAPI. O e-mail é da produtora e, também, o de acesso do administrador. */
export type CreateProducerRequest = {
  name: string;
  email: string;
  phone: string;
  administratorName: string;
  administratorPassword: string;
};

export const PRODUCERS_ROUTE = "/api/v1/producers";

/** POST /api/v1/producers: cadastro público da produtora (PENDING) e do administrador (PENDING até verificar o e-mail). */
export function createProducer(request: CreateProducerRequest, signal?: AbortSignal): Promise<ApiResult> {
  return postJson(PRODUCERS_ROUTE, request, { signal });
}
