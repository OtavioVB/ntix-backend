import type { ApiDataResult } from "./client";
import { customerAuthorizedRequest } from "./customerAuth";

/**
 * Área do comprador (REQ13): pedidos e ingressos do cliente logado. Todas as chamadas usam o access token do comprador;
 * a API só devolve o que é dele (RN1), então o front não filtra nada por conta própria.
 */

export type CustomerOrderStatus = "PENDING_PAYMENT" | "PAID" | "EXPIRED";

export type CustomerOrderItem = {
  code: string;
  status: CustomerOrderStatus;
  createdAt: string;
  paidAt?: string;
  totalCents: number;
  ticketCount: number;
  event: { slug: string; title: string; startsAt: string; city: string; state: string; imageUrl?: string };
  producer: { slug: string; name: string };
};

export type CustomerOrdersPage = { items: CustomerOrderItem[]; page: number; pageSize: number; totalItems: number };

export type CustomerTicketEvent = {
  slug: string;
  title: string;
  startsAt: string;
  doorsOpenAt?: string;
  venueName: string;
  address: string;
  city: string;
  state: string;
  imageUrl?: string;
};

export type CustomerTicketItem = {
  code: string;
  status: "VALID" | "USED";
  batchName: string;
  holderName: string;
  orderCode: string;
  event: CustomerTicketEvent;
};

export type CustomerTicketsPage = { items: CustomerTicketItem[]; page: number; pageSize: number; totalItems: number };

/** Detalhe do ingresso para a portaria: a lista mais a classificação indicativa e a emissão (RN6). */
export type CustomerTicketDetail = CustomerTicketItem & { ageRating: string; issuedAt: string };

export type TicketWhen = "UPCOMING" | "PAST";

/** GET /api/v1/customers/me/orders: do mais recente ao mais antigo, com o status efetivo (RN3). */
export function listMyOrders(options: { status?: CustomerOrderStatus; page?: number; pageSize?: number } = {}, signal?: AbortSignal): Promise<ApiDataResult<CustomerOrdersPage>> {
  const query = new URLSearchParams();
  if (options.status) query.set("status", options.status);
  if (options.page) query.set("page", String(options.page));
  if (options.pageSize) query.set("pageSize", String(options.pageSize));
  const suffix = query.toString() ? `?${query.toString()}` : "";

  return customerAuthorizedRequest("GET", `/api/v1/customers/me/orders${suffix}`, { signal });
}

/** GET /api/v1/customers/me/tickets: próximos (padrão) ou passados, pela data do evento (RN5). */
export function listMyTickets(when: TicketWhen = "UPCOMING", signal?: AbortSignal): Promise<ApiDataResult<CustomerTicketsPage>> {
  return customerAuthorizedRequest("GET", `/api/v1/customers/me/tickets?when=${when}`, { signal });
}

/** GET /api/v1/customers/me/tickets/{code}: o código é o conteúdo do QR Code (RN6). */
export function getMyTicket(code: string, signal?: AbortSignal): Promise<ApiDataResult<CustomerTicketDetail>> {
  return customerAuthorizedRequest("GET", `/api/v1/customers/me/tickets/${encodeURIComponent(code)}`, { signal });
}
