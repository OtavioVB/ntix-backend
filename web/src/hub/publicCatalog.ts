import { getPublicEvent, searchPublicEvents, type AgeRating, type EventCategory, type PublicBatch, type PublicEvent } from "../api/events";
import { generatedArt, type Category, type HubBatch, type HubEvent } from "./catalog";

/** Categoria da API para o nome do hub (as mesmas de `CATEGORIES`). */
const CATEGORY_NAMES: Record<EventCategory, Category> = {
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

const AGE_RATING_NAMES: Record<AgeRating, string> = {
  FREE: "Livre",
  AGE_10: "10 anos",
  AGE_12: "12 anos",
  AGE_14: "14 anos",
  AGE_16: "16 anos",
  AGE_18: "18 anos",
};

/** O tamanho da página da vitrine que o hub carrega de uma vez (o máximo da API). */
export const HUB_PAGE_SIZE = 50;

function hourOf(iso: string | undefined): string {
  if (!iso) return "A confirmar";
  const date = new Date(iso);
  return date.getMinutes() === 0 ? `${date.getHours()}h` : `${date.getHours()}h${String(date.getMinutes()).padStart(2, "0")}`;
}

/**
 * A API não expõe o estoque (REQ11, RN5): o lote em venda vira um lote com `maxSelectable` ingressos e nenhum vendido, e o
 * esgotado, um lote com tudo vendido. Assim as regras do hub (`batchState`, `maxSelectable`) dão o mesmo que a API.
 */
export function toHubBatch(batch: PublicBatch, maxTicketsPerOrder: number): HubBatch {
  const quantity = batch.saleState === "ON_SALE" ? batch.maxSelectable : batch.saleState === "SOLD_OUT" ? 1 : maxTicketsPerOrder;
  return {
    id: batch.id,
    name: batch.name,
    note: batch.note,
    priceCents: batch.priceCents,
    quantity,
    sold: batch.saleState === "SOLD_OUT" ? 1 : 0,
    startsAt: new Date(batch.salesStartsAt).getTime(),
    endsAt: new Date(batch.salesEndsAt).getTime(),
  };
}

/** Evento da vitrine no formato que as telas do hub já usam. Sem foto, a arte é gerada do slug. */
export function toHubEvent(event: PublicEvent): HubEvent {
  return {
    id: event.slug,
    slug: event.slug,
    title: event.title,
    tagline: `Por ${event.producer.name}`,
    category: CATEGORY_NAMES[event.category] ?? "Outros",
    producer: event.producer.name,
    venue: event.venueName,
    address: event.address,
    city: event.city,
    state: event.state,
    date: new Date(event.startsAt).getTime(),
    doorsAt: hourOf(event.doorsOpenAt),
    startsAt: hourOf(event.startsAt),
    ageRating: AGE_RATING_NAMES[event.ageRating] ?? event.ageRating,
    description: (event.description ?? "").split(/\n\s*\n/).map((paragraph) => paragraph.trim()).filter(Boolean),
    schedule: [],
    included: [],
    imageUrl: event.imageUrl,
    art: generatedArt(event.slug),
    accent: "#c99b14",
    popularity: 50,
    fromProducer: true,
    batches: event.batches.map((batch) => toHubBatch(batch, event.maxTicketsPerOrder)),
  };
}

/**
 * Carrega a vitrine real: a primeira página da lista e, em paralelo, a página de cada evento (com os lotes que a tela do hub
 * precisa). Evento cuja página falhar fica de fora, sem derrubar o hub.
 */
export async function loadPublicCatalog(producer: string | undefined, signal?: AbortSignal): Promise<HubEvent[] | null> {
  const list = await searchPublicEvents({ producer, pageSize: HUB_PAGE_SIZE }, signal);
  if (!list.ok) return null;

  const details = await Promise.all(list.data.items.map((item) => getPublicEvent(item.slug, signal)));
  return details.flatMap((detail) => (detail.ok ? [toHubEvent(detail.data)] : []));
}
