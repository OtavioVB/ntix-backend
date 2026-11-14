import { requestJson, type ApiDataResult } from "./client";

/** Métodos de pagamento da Ntix (REQ4, RN2). */
export type PaymentMethod = "PIX" | "BOLETO" | "CREDIT_CARD";

export const PAYMENT_METHODS: PaymentMethod[] = ["PIX", "BOLETO", "CREDIT_CARD"];

export type IntegrationStatus = "ACTIVE" | "INACTIVE";

/** Item de `GET /api/v1/payment-providers`: o catálogo monta o formulário de cada fornecedor. */
export type PaymentProvider = {
  code: string;
  name: string;
  paymentMethods: PaymentMethod[];
  credentialKeys: { key: string; isSecret: boolean; isRequired: boolean }[];
};

/** Chave secreta vem só com `valueHint` (últimos caracteres); a não secreta, com `value`. */
export type IntegrationCredential = { key: string; isSecret: boolean; valueHint?: string; value?: string };

export type ProducerIntegration = {
  id: string;
  providerCode: string;
  name: string;
  status: IntegrationStatus;
  credentials: IntegrationCredential[];
};

export type PaymentRouteItem = { priority: number; integrationId: string; name: string; status: IntegrationStatus };

export type PaymentRoutes = Record<PaymentMethod, PaymentRouteItem[]>;

/** `CreateProducerIntegrationV1Request` do OpenAPI. */
export type CreateProducerIntegrationRequest = { providerCode: string; name: string; credentials: Record<string, string> };

/** `UpdateProducerIntegrationV1Request`: tudo opcional; credencial com `null` é removida, se não for obrigatória. */
export type UpdateProducerIntegrationRequest = { name?: string; status?: IntegrationStatus; credentials?: Record<string, string | null> };

/** `ReplacePaymentRouteV1Request`: a ordem do array é a prioridade. Lista vazia remove o método. */
export type ReplacePaymentRouteRequest = { integrationIds: string[] };

export const PAYMENT_PROVIDERS_ROUTE = "/api/v1/payment-providers";

const producerRoute = (slug: string) => `/api/v1/producers/${encodeURIComponent(slug)}`;

/** GET /api/v1/payment-providers: catálogo público dos fornecedores habilitados. */
export function getPaymentProviders(signal?: AbortSignal): Promise<ApiDataResult<PaymentProvider[]>> {
  return requestJson("GET", PAYMENT_PROVIDERS_ROUTE, { signal });
}

export function getPaymentIntegrations(slug: string, accessToken?: string, signal?: AbortSignal): Promise<ApiDataResult<ProducerIntegration[]>> {
  return requestJson("GET", `${producerRoute(slug)}/payment-integrations`, { accessToken, signal });
}

export function createPaymentIntegration(slug: string, request: CreateProducerIntegrationRequest, accessToken?: string): Promise<ApiDataResult<ProducerIntegration>> {
  return requestJson("POST", `${producerRoute(slug)}/payment-integrations`, { body: request, accessToken });
}

export function updatePaymentIntegration(slug: string, id: string, request: UpdateProducerIntegrationRequest, accessToken?: string): Promise<ApiDataResult<ProducerIntegration>> {
  return requestJson("PUT", `${producerRoute(slug)}/payment-integrations/${encodeURIComponent(id)}`, { body: request, accessToken });
}

export function getPaymentRoutes(slug: string, accessToken?: string, signal?: AbortSignal): Promise<ApiDataResult<PaymentRoutes>> {
  return requestJson("GET", `${producerRoute(slug)}/payment-routes`, { accessToken, signal });
}

export function replacePaymentRoute(slug: string, method: PaymentMethod, request: ReplacePaymentRouteRequest, accessToken?: string): Promise<ApiDataResult<PaymentRouteItem[]>> {
  return requestJson("PUT", `${producerRoute(slug)}/payment-routes/${method}`, { body: request, accessToken });
}
