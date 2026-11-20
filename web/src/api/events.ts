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
