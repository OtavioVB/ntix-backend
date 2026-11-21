import type { ApiNotification } from "../../api/client";
import type { AgeRating, EventCategory, EventRequest, EventStatus, ProducerEvent } from "../../api/events";

/** Rótulos das categorias do hub (REQ9, RN3), na ordem do formulário. */
export const CATEGORY_LABELS: Record<EventCategory, string> = {
  SHOWS: "Shows",
  FESTIVALS: "Festivais",
  THEATER: "Teatro",
  STAND_UP: "Stand-up",
  ELECTRONIC: "Eletrônica",
  SPORTS: "Esportes",
  GASTRONOMY: "Gastronomia",
  KIDS: "Infantil",
  OTHER: "Outros",
};

export const AGE_RATING_LABELS: Record<AgeRating, string> = {
  FREE: "Livre",
  AGE_10: "10 anos",
  AGE_12: "12 anos",
  AGE_14: "14 anos",
  AGE_16: "16 anos",
  AGE_18: "18 anos",
};

export const STATUS_LABELS: Record<EventStatus, string> = { DRAFT: "Rascunho", PUBLISHED: "Publicado", ENDED: "Encerrado" };

export const STATES = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

/** Limites dos value objects da API (REQ9, RN2 a RN9). */
export const LIMITS = { titleMin: 3, title: 120, description: 5000, venueName: 120, address: 256, city: 80, imageUrl: 2048 };

const MAX_DURATION_MS = 30 * 24 * 60 * 60 * 1000;
const MAX_DOORS_ADVANCE_MS = 12 * 60 * 60 * 1000;

/** Formulário com as datas como os campos `datetime-local` (hora local do navegador). */
export type EventForm = {
  title: string;
  description: string;
  category: EventCategory | "";
  startsAt: string;
  endsAt: string;
  doorsOpenAt: string;
  venueName: string;
  address: string;
  city: string;
  state: string;
  ageRating: AgeRating | "";
  imageUrl: string;
};

export type EventFormField = keyof EventForm;

export type EventFormErrors = Partial<Record<EventFormField | "general", string>>;

export const EMPTY_EVENT_FORM: EventForm = {
  title: "",
  description: "",
  category: "",
  startsAt: "",
  endsAt: "",
  doorsOpenAt: "",
  venueName: "",
  address: "",
  city: "",
  state: "",
  ageRating: "",
  imageUrl: "",
};

function normalize(value: string): string {
  return value.trim().split(/\s+/).filter(Boolean).join(" ");
}

function toTime(local: string): number | null {
  if (!local) return null;
  const time = new Date(local).getTime();
  return Number.isNaN(time) ? null : time;
}

/** ISO 8601 em UTC a partir do valor de um `datetime-local`. */
export function localToIso(local: string): string | undefined {
  const time = toTime(local);
  return time === null ? undefined : new Date(time).toISOString();
}

/** Valor de `datetime-local` (hora local, sem segundos) a partir da data da API. */
export function isoToLocal(iso: string | undefined): string {
  if (!iso) return "";
  const date = new Date(iso);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function formFromEvent(event: ProducerEvent): EventForm {
  return {
    title: event.title,
    description: event.description ?? "",
    category: event.category,
    startsAt: isoToLocal(event.startsAt),
    endsAt: isoToLocal(event.endsAt),
    doorsOpenAt: isoToLocal(event.doorsOpenAt),
    venueName: event.venueName,
    address: event.address,
    city: event.city,
    state: event.state,
    ageRating: event.ageRating,
    imageUrl: event.imageUrl ?? "",
  };
}

/** Validação local espelhando a API, para a pessoa não descobrir o erro só no servidor. */
export function validateEvent(form: EventForm, now = Date.now()): EventFormErrors {
  const errors: EventFormErrors = {};
  const title = normalize(form.title);

  if (title.length < LIMITS.titleMin || title.length > LIMITS.title) errors.title = "O título do evento deve ter de 3 a 120 caracteres.";
  if (form.description.trim().length > LIMITS.description) errors.description = "A descrição do evento deve ter até 5000 caracteres.";
  if (!form.category) errors.category = "Escolha a categoria do evento.";
  if (!form.ageRating) errors.ageRating = "Escolha a classificação etária.";

  const imageUrl = form.imageUrl.trim();
  if (imageUrl && (!imageUrl.startsWith("https://") || imageUrl.length > LIMITS.imageUrl || /\s/.test(imageUrl))) errors.imageUrl = "A imagem deve ser um endereço https://.";

  return { ...errors, ...validatePeriod(form, now), ...validateVenue(form) };
}

/** RN5 e RN6: início no futuro, fim depois do início (até 30 dias) e portões até 12 horas antes. */
function validatePeriod(form: EventForm, now: number): EventFormErrors {
  const errors: EventFormErrors = {};
  const startsAt = toTime(form.startsAt);
  const endsAt = toTime(form.endsAt);
  const doorsOpenAt = toTime(form.doorsOpenAt);

  if (startsAt === null) errors.startsAt = "Informe quando o evento começa.";
  else if (startsAt <= now) errors.startsAt = "O evento deve começar no futuro.";

  if (endsAt === null) errors.endsAt = "Informe quando o evento termina.";
  else if (startsAt !== null && (endsAt <= startsAt || endsAt - startsAt > MAX_DURATION_MS)) errors.endsAt = "O fim deve ser depois do início, com duração de até 30 dias.";

  if (doorsOpenAt !== null && startsAt !== null && (doorsOpenAt > startsAt || startsAt - doorsOpenAt > MAX_DOORS_ADVANCE_MS))
    errors.doorsOpenAt = "A abertura dos portões deve ser até o início e no máximo 12 horas antes.";

  return errors;
}

/** RN7: nome, endereço e cidade obrigatórios e a UF. */
function validateVenue(form: EventForm): EventFormErrors {
  const errors: EventFormErrors = {};
  const venueName = normalize(form.venueName);
  const address = normalize(form.address);
  const city = normalize(form.city);

  if (venueName.length === 0 || venueName.length > LIMITS.venueName) errors.venueName = "O nome do local deve ter de 1 a 120 caracteres.";
  if (address.length === 0 || address.length > LIMITS.address) errors.address = "O endereço do local deve ter de 1 a 256 caracteres.";
  if (city.length === 0 || city.length > LIMITS.city) errors.city = "A cidade deve ter de 1 a 80 caracteres.";
  if (!STATES.includes(form.state)) errors.state = "Escolha a UF.";

  return errors;
}

export function hasErrors(errors: EventFormErrors): boolean {
  return Object.values(errors).some(Boolean);
}

/** Corpo do `EventV1Request`: opcional vazio não vai, e as datas saem em UTC. */
export function toEventRequest(form: EventForm): EventRequest {
  const request: EventRequest = {
    title: normalize(form.title),
    category: form.category as EventCategory,
    startsAt: localToIso(form.startsAt)!,
    endsAt: localToIso(form.endsAt)!,
    venueName: normalize(form.venueName),
    address: normalize(form.address),
    city: normalize(form.city),
    state: form.state,
    ageRating: form.ageRating as AgeRating,
  };

  const description = form.description.trim();
  const doorsOpenAt = localToIso(form.doorsOpenAt);
  const imageUrl = form.imageUrl.trim();

  if (description) request.description = description;
  if (doorsOpenAt) request.doorsOpenAt = doorsOpenAt;
  if (imageUrl) request.imageUrl = imageUrl;

  return request;
}

/** Código da API para o campo do formulário; o que não tem campo vira mensagem geral. */
const FIELD_BY_CODE: Record<string, EventFormField> = {
  EVENT_TITLE_INVALID: "title",
  EVENT_DESCRIPTION_INVALID: "description",
  EVENT_CATEGORY_INVALID: "category",
  EVENT_PERIOD_INVALID: "startsAt",
  EVENT_DOORS_OPEN_INVALID: "doorsOpenAt",
  EVENT_VENUE_NAME_INVALID: "venueName",
  EVENT_VENUE_ADDRESS_INVALID: "address",
  EVENT_VENUE_CITY_INVALID: "city",
  EVENT_STATE_INVALID: "state",
  EVENT_AGE_RATING_INVALID: "ageRating",
  EVENT_IMAGE_URL_INVALID: "imageUrl",
};

export function mapEventNotifications(notifications: ApiNotification[]): EventFormErrors {
  const errors: EventFormErrors = {};

  for (const notification of notifications) {
    const field = FIELD_BY_CODE[notification.code] ?? "general";
    errors[field] ??= notification.message;
  }

  return errors;
}
