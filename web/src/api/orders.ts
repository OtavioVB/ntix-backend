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
};

/** POST /api/v1/orders: cria o pedido e reserva os ingressos por 15 minutos. Exige o access token do comprador. */
export function postOrder(request: CreateOrderRequest, signal?: AbortSignal): Promise<ApiDataResult<ApiOrder>> {
  return customerAuthorizedRequest("POST", "/api/v1/orders", { body: request, signal });
}
