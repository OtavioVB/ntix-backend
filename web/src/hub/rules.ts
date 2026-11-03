import { DAY_MS } from "../platform/demoData";
import type { Category, HubBatch, HubEvent } from "./catalog";

/** Limite por pedido: decisão de tela enquanto a regra de negócio não existe. */
export const MAX_TICKETS_PER_ORDER = 6;

export type BatchState = "ON_SALE" | "SOLD_OUT" | "UPCOMING" | "CLOSED";

/** Vendas feitas nesta sessão, por lote: somam às que já vieram no catálogo. */
export type ExtraSold = Record<string, number>;

export function remaining(batch: HubBatch, extra: ExtraSold = {}): number {
  return Math.max(0, batch.quantity - batch.sold - (extra[batch.id] ?? 0));
}

export function batchState(batch: HubBatch, now: number, extra: ExtraSold = {}): BatchState {
  if (remaining(batch, extra) === 0) return "SOLD_OUT";
  if (now < batch.startsAt) return "UPCOMING";
  if (now >= batch.endsAt) return "CLOSED";
  return "ON_SALE";
}

export function maxSelectable(batch: HubBatch, selectedElsewhere: number, extra: ExtraSold = {}): number {
  return Math.max(0, Math.min(remaining(batch, extra), MAX_TICKETS_PER_ORDER - selectedElsewhere));
}

export function onSaleBatches(event: HubEvent, now: number, extra: ExtraSold = {}): HubBatch[] {
  return event.batches.filter((batch) => batchState(batch, now, extra) === "ON_SALE");
}

/** Menor preço entre os lotes à venda agora. Nulo se nada estiver à venda. */
export function priceFrom(event: HubEvent, now: number, extra: ExtraSold = {}): number | null {
  const prices = onSaleBatches(event, now, extra).map((batch) => batch.priceCents);
  return prices.length ? Math.min(...prices) : null;
}

/** Aviso de escassez só quando é verdade: restam menos de 15% do que está à venda. */
export function scarcity(event: HubEvent, now: number, extra: ExtraSold = {}): "SOLD_OUT" | "LAST" | null {
  const selling = onSaleBatches(event, now, extra);
  if (selling.length === 0) return "SOLD_OUT";
  const left = selling.reduce((sum, batch) => sum + remaining(batch, extra), 0);
  const total = selling.reduce((sum, batch) => sum + batch.quantity, 0);
  return left / total < 0.15 ? "LAST" : null;
}

export type WhenFilter = "" | "7" | "30" | "60";

export type HubFilters = { query: string; category: Category | ""; city: string; when: WhenFilter };

function normalize(text: string): string {
  return text.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
}

export function filterEvents(events: HubEvent[], filters: HubFilters, now: number): HubEvent[] {
  const query = normalize(filters.query.trim());
  return events
    .filter((event) => event.date >= now)
    .filter((event) => !filters.category || event.category === filters.category)
    .filter((event) => !filters.city || event.city === filters.city)
    .filter((event) => !filters.when || event.date <= now + Number(filters.when) * DAY_MS)
    .filter((event) => !query || normalize(`${event.title} ${event.tagline} ${event.producer} ${event.venue} ${event.city} ${event.category}`).includes(query))
    .sort((a, b) => a.date - b.date);
}

export function eventDay(time: number) {
  const date = new Date(time);
  return {
    day: date.toLocaleDateString("pt-BR", { day: "2-digit" }),
    month: date.toLocaleDateString("pt-BR", { month: "short" }).replace(".", "").toUpperCase(),
    weekday: date.toLocaleDateString("pt-BR", { weekday: "long" }),
    full: date.toLocaleDateString("pt-BR", { weekday: "long", day: "numeric", month: "long" }),
  };
}

export function firstNameOf(fullName: string): string {
  return fullName.trim().split(/\s+/)[0] ?? "";
}
