import type { ApiDataResult } from "./client";
import { customerAuthorizedRequest } from "./customerAuth";

/** Canal da venda (REQ6, RN2): hub da Ntix ou loja do produtor. */
export type SalesChannel = "HUB" | "STORE";

/** Situação do pedido (REQ6). `EXPIRED` é derivado pela API quando a reserva venceu sem pagamento. */
export type OrderStatus = "PENDING_PAYMENT" | "PAID" | "EXPIRED";

/**
 * `CreateOrderV1Request` do OpenAPI. O corpo não tem preço: ele vem do lote, no servidor, e fica congelado no item (RN7).
 */
export type CreateOrderRequest = {
  eventSlug: string;
  salesChannel: SalesChannel;
  items: { batchId: string; quantity: number }[];
};

export type ApiOrderItem = { batchId: string; batchName: string; quantity: number; unitPriceCents: number; totalCents: number };

/** Pedido como a API devolve. Os ingressos só existem depois do pagamento (REQ8). */
export type ApiOrder = {
  code: string;
  status: OrderStatus;
  salesChannel: SalesChannel;
  createdAt: string;
  expiresAt: string;
  totalCents: number;
  event: { slug: string; title: string; startsAt: string; venueName: string; city: string; state: string; imageUrl?: string };
  items: ApiOrderItem[];
  tickets: { code: string; batchName: string; holderName: string; status: string }[];
  payment?: ApiPayment;
};

/** POST /api/v1/orders: cria o pedido e reserva os ingressos por 15 minutos. Exige o access token do comprador. */
export function postOrder(request: CreateOrderRequest, signal?: AbortSignal): Promise<ApiDataResult<ApiOrder>> {
  return customerAuthorizedRequest("POST", "/api/v1/orders", { body: request, signal });
}

/** Cobrança do pedido (REQ7). O bloco `pix` só vem com a cobrança PENDING e dentro da validade. */
export type ApiPayment = {
  id: string;
  method: "PIX";
  status: "PENDING" | "PAID" | "EXPIRED";
  amountCents: number;
  expiresAt: string;
  pix?: { copyPasteCode: string; qrCodeImage: string };
};

/** GET /api/v1/orders/{code}: o pedido, com a cobrança mais recente em `payment`. O checkout consulta a cada poucos segundos. */
export function getOrder(code: string, signal?: AbortSignal): Promise<ApiDataResult<ApiOrder>> {
  return customerAuthorizedRequest("GET", `/api/v1/orders/${encodeURIComponent(code)}`, { signal });
}

/** GET /api/v1/orders/{code}/payment-methods: só o que funciona para o produtor do pedido. */
export function getOrderPaymentMethods(code: string, signal?: AbortSignal): Promise<ApiDataResult<{ methods: { method: string }[] }>> {
  return customerAuthorizedRequest("GET", `/api/v1/orders/${encodeURIComponent(code)}/payment-methods`, { signal });
}

/**
 * POST /api/v1/orders/{code}/payments (`CreateOrderPaymentV1Request`): gera o Pix (201) ou devolve o pendente que já existe (200).
 * O valor não vai no corpo: é o total do pedido.
 */
export function postOrderPayment(code: string, method: "PIX", signal?: AbortSignal): Promise<ApiDataResult<ApiPayment>> {
  return customerAuthorizedRequest("POST", `/api/v1/orders/${encodeURIComponent(code)}/payments`, { body: { method }, signal });
}
