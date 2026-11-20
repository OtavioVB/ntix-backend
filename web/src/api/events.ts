import { authorizedRequest } from "./auth";
import type { ApiDataResult } from "./client";

/** Situação do evento (REQ9). Só a publicação (REQ10) leva a PUBLISHED. */
export type EventStatus = "DRAFT" | "PUBLISHED";

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

/** Evento como o painel o vê. Campo opcional ausente não vem no JSON. */
export type ProducerEvent = Omit<EventRequest, "category" | "ageRating"> & {
  id: string;
  slug: string;
  category: EventCategory;
  ageRating: AgeRating;
  status: EventStatus;
  createdAt: string;
  updatedAt: string;
};

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

/** POST .../events/{id}/publication: sem corpo; devolve o evento publicado. */
export function publishProducerEvent(slug: string, eventId: string): Promise<ApiDataResult<ProducerEvent>> {
  return authorizedRequest("POST", `${eventsRoute(slug)}/${encodeURIComponent(eventId)}/publication`);
}
