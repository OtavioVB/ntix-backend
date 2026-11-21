import { authorizedRequest } from "./auth";
import { requestJson, type ApiDataResult } from "./client";

/** Situação exposta pela API (REQ9). Só a publicação (REQ10) leva a PUBLISHED; ENDED é todo evento cujo término passou (REQ14). */
export type EventStatus = "DRAFT" | "PUBLISHED" | "ENDED";

/** Categorias do hub (REQ9, RN3). */
export type EventCategory = "SHOWS" | "FESTIVALS" | "THEATER" | "STAND_UP" | "ELECTRONIC" | "SPORTS" | "GASTRONOMY" | "KIDS" | "OTHER";

/** Classificação etária (REQ9, RN8). */
export type AgeRating = "FREE" | "AGE_10" | "AGE_12" | "AGE_14" | "AGE_16" | "AGE_18";

/** `EventV1Request` do OpenAPI: o mesmo corpo na criação e na edição. Datas em ISO 8601 (UTC). */
export type EventRequest = {
  title: string;
  description?: string;
  category: EventCategory;
  startsAt: string;
  endsAt: string;
  doorsOpenAt?: string;
  venueName: string;
  address: string;
  city: string;
  state: string;
  ageRating: AgeRating;
  imageUrl?: string;
};

/** Resumo de vendas do evento (REQ14): capacidade dos lotes e vendas de pedidos pagos, calculados pela API. */
export type EventSalesSummary = {
  capacity: number;
  soldQuantity: number;
  grossCents: number;
  paidOrders: number;
};

/** Evento como o painel o vê. Campo opcional ausente não vem no JSON. A descrição é Markdown (REQ14). */
export type ProducerEvent = Omit<EventRequest, "category" | "ageRating"> & {
  id: string;
  slug: string;
  category: EventCategory;
  ageRating: AgeRating;
  status: EventStatus;
  createdAt: string;
  updatedAt: string;
  salesSummary: EventSalesSummary;
};

/** Um dia de `GET .../events/{id}/sales/daily` (REQ14), no fuso de Brasília. */
export type DailySales = { date: string; soldQuantity: number; grossCents: number; paidOrders: number };

export type DailySalesReport = { from: string; to: string; days: DailySales[] };

const eventsRoute = (slug: string) => `/api/v1/producers/${encodeURIComponent(slug)}/events`;

// Endpoints do painel: com o access token da sessão, renovado uma vez no 401 (REQ5).

export function getProducerEvents(slug: string, status?: EventStatus, signal?: AbortSignal): Promise<ApiDataResult<ProducerEvent[]>> {
  return authorizedRequest("GET", status ? `${eventsRoute(slug)}?status=${status}` : eventsRoute(slug), { signal });
}

export function getProducerEvent(slug: string, id: string, signal?: AbortSignal): Promise<ApiDataResult<ProducerEvent>> {
  return authorizedRequest("GET", `${eventsRoute(slug)}/${encodeURIComponent(id)}`, { signal });
}

export function createProducerEvent(slug: string, request: EventRequest): Promise<ApiDataResult<ProducerEvent>> {
  return authorizedRequest("POST", eventsRoute(slug), { body: request });
}

export function updateProducerEvent(slug: string, id: string, request: EventRequest): Promise<ApiDataResult<ProducerEvent>> {
  return authorizedRequest("PUT", `${eventsRoute(slug)}/${encodeURIComponent(id)}`, { body: request });
}

/** Situação do lote (REQ10): pausar é INACTIVE. */
export type BatchStatus = "ACTIVE" | "INACTIVE";

/** Estado de venda derivado pela API (REQ10, RN7). */
export type BatchSaleState = "PAUSED" | "UPCOMING" | "CLOSED" | "SOLD_OUT" | "ON_SALE";

/** `TicketBatchV1Request` do OpenAPI: o mesmo corpo na criação e na edição. Preço em centavos. */
export type TicketBatchRequest = {
  name: string;
  note?: string;
  priceCents: number;
  quantity: number;
  salesStartsAt: string;
  salesEndsAt: string;
  status: BatchStatus;
};

export type TicketBatch = Omit<TicketBatchRequest, "note"> & {
  id: string;
  note?: string;
  soldQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  saleState: BatchSaleState;
  /** Detalhe de vendas do lote (REQ14), só de pedidos pagos. */
  grossCents: number;
  paidOrders: number;
};

const batchesRoute = (slug: string, eventId: string) => `${eventsRoute(slug)}/${encodeURIComponent(eventId)}/batches`;

export function getTicketBatches(slug: string, eventId: string, signal?: AbortSignal): Promise<ApiDataResult<TicketBatch[]>> {
  return authorizedRequest("GET", batchesRoute(slug, eventId), { signal });
}

export function createTicketBatch(slug: string, eventId: string, request: TicketBatchRequest): Promise<ApiDataResult<TicketBatch>> {
  return authorizedRequest("POST", batchesRoute(slug, eventId), { body: request });
}

export function updateTicketBatch(slug: string, eventId: string, batchId: string, request: TicketBatchRequest): Promise<ApiDataResult<TicketBatch>> {
  return authorizedRequest("PUT", `${batchesRoute(slug, eventId)}/${encodeURIComponent(batchId)}`, { body: request });
}

/** Vendas por dia (REQ14): datas `yyyy-MM-dd` de Brasília; sem elas, a API devolve os últimos 30 dias. */
export function getEventDailySales(slug: string, eventId: string, period: { from?: string; to?: string } = {}, signal?: AbortSignal): Promise<ApiDataResult<DailySalesReport>> {
  const params = new URLSearchParams();
  if (period.from) params.set("from", period.from);
  if (period.to) params.set("to", period.to);
  const search = params.toString();
  const route = `${eventsRoute(slug)}/${encodeURIComponent(eventId)}/sales/daily`;
  return authorizedRequest("GET", search ? `${route}?${search}` : route, { signal });
}

/** POST .../events/{id}/publication: sem corpo; devolve o evento publicado. */
export function publishProducerEvent(slug: string, eventId: string): Promise<ApiDataResult<ProducerEvent>> {
  return authorizedRequest("POST", `${eventsRoute(slug)}/${encodeURIComponent(eventId)}/publication`);
}

// Vitrine pública (REQ11): anônima, sem token.

export type EventSaleState = "ON_SALE" | "UPCOMING" | "SOLD_OUT" | "CLOSED";

export type PublicProducer = { slug: string; name: string };

/** Item de `GET /api/v1/events`: sem estoque nem vendidos. */
export type PublicEventSummary = {
  slug: string;
  title: string;
  category: EventCategory;
  startsAt: string;
  venueName: string;
  city: string;
  state: string;
  imageUrl?: string;
  producer: PublicProducer;
  priceFromCents?: number;
  saleState: EventSaleState;
};

export type PublicEventsPage = { items: PublicEventSummary[]; page: number; pageSize: number; totalItems: number };

/** Lote da página pública: `maxSelectable` já limitado pelo pedido (até 6) e zero fora de venda. */
export type PublicBatch = {
  id: string;
  name: string;
  note?: string;
  priceCents: number;
  salesStartsAt: string;
  salesEndsAt: string;
  saleState: BatchSaleState;
  maxSelectable: number;
};

export type PublicEvent = {
  slug: string;
  title: string;
  description?: string;
  category: EventCategory;
  startsAt: string;
  endsAt: string;
  doorsOpenAt?: string;
  venueName: string;
  address: string;
  city: string;
  state: string;
  ageRating: AgeRating;
  imageUrl?: string;
  producer: PublicProducer;
  saleState: EventSaleState;
  maxTicketsPerOrder: number;
  batches: PublicBatch[];
};

export type PublicEventsQuery = {
  producer?: string;
  category?: EventCategory;
  city?: string;
  state?: string;
  startsFrom?: string;
  startsTo?: string;
  page?: number;
  pageSize?: number;
};

export function searchPublicEvents(query: PublicEventsQuery = {}, signal?: AbortSignal): Promise<ApiDataResult<PublicEventsPage>> {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== "") params.set(key, String(value));
  const search = params.toString();
  return requestJson("GET", search ? `/api/v1/events?${search}` : "/api/v1/events", { signal });
}

export function getPublicEvent(slug: string, signal?: AbortSignal): Promise<ApiDataResult<PublicEvent>> {
  return requestJson("GET", `/api/v1/events/${encodeURIComponent(slug)}`, { signal });
}
