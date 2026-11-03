import type { ArtSpec } from "../hub/art";
import type { HubEvent } from "../hub/catalog";
import { pastEvents, buildCatalog } from "../hub/catalog";
import { DAY_MS } from "../platform/demoData";

export type PaymentMethod = "PIX" | "CARD";
export type PaymentStatus = "PAID" | "PENDING";

export type OrderPayment = {
  method: PaymentMethod;
  status: PaymentStatus;
  paidAt?: number;
  /** Só para cartão: nunca o número inteiro. */
  brand?: string;
  last4?: string;
  installments?: number;
};

export type TicketStatus = "VALID" | "USED";

export type IssuedTicket = { code: string; batchName: string; status: TicketStatus; usedAt?: number };

export type OrderItem = { batchId: string; name: string; quantity: number; unitPriceCents: number };

/** Foto do evento no momento da compra: o pedido não muda se o evento mudar depois. */
export type EventSnapshot = {
  id: string;
  slug: string;
  title: string;
  date: number;
  venue: string;
  city: string;
  state: string;
  doorsAt: string;
  accent: string;
  art: ArtSpec;
  imageUrl?: string;
  fromProducer?: boolean;
};

export type CustomerOrder = {
  code: string;
  createdAt: number;
  event: EventSnapshot;
  items: OrderItem[];
  totalCents: number;
  payment: OrderPayment;
  /** Vazio até o pagamento: ingresso só existe depois de pago. */
  tickets: IssuedTicket[];
};

let sequence = 0;

function newOrderCode(now: number): string {
  sequence += 1;
  return `NX${(now % 1_000_000).toString(36).toUpperCase()}${sequence}`;
}

export function snapshot(event: HubEvent): EventSnapshot {
  return {
    id: event.id,
    slug: event.slug,
    title: event.title,
    date: event.date,
    venue: event.venue,
    city: event.city,
    state: event.state,
    doorsAt: event.doorsAt,
    accent: event.accent,
    art: event.art,
    imageUrl: event.imageUrl,
    fromProducer: event.fromProducer,
  };
}

export function itemsFromSelection(event: HubEvent, selection: Record<string, number>): OrderItem[] {
  return event.batches
    .filter((batch) => (selection[batch.id] ?? 0) > 0)
    .map((batch) => ({ batchId: batch.id, name: batch.name, quantity: selection[batch.id], unitPriceCents: batch.priceCents }));
}

export function orderTotal(items: OrderItem[]): number {
  return items.reduce((sum, item) => sum + item.quantity * item.unitPriceCents, 0);
}

export function ticketCount(items: OrderItem[]): number {
  return items.reduce((sum, item) => sum + item.quantity, 0);
}

function issueTickets(code: string, items: OrderItem[]): IssuedTicket[] {
  let index = 0;
  return items.flatMap((item) =>
    Array.from({ length: item.quantity }, () => {
      index += 1;
      return { code: `${code}-${String(index).padStart(2, "0")}`, batchName: item.name, status: "VALID" as const };
    }),
  );
}

export function createOrder(event: HubEvent, items: OrderItem[], payment: OrderPayment, now: number): CustomerOrder {
  const code = newOrderCode(now);
  return {
    code,
    createdAt: now,
    event: snapshot(event),
    items,
    totalCents: orderTotal(items),
    payment,
    tickets: payment.status === "PAID" ? issueTickets(code, items) : [],
  };
}

/** Confirma um Pix pendente: é aqui que os ingressos passam a existir. */
export function confirmPayment(order: CustomerOrder, now: number): CustomerOrder {
  if (order.payment.status === "PAID") return order;
  return { ...order, payment: { ...order.payment, status: "PAID", paidAt: now }, tickets: issueTickets(order.code, order.items) };
}

export function markTicketUsed(order: CustomerOrder, code: string, now: number): CustomerOrder {
  return { ...order, tickets: order.tickets.map((ticket) => (ticket.code === code ? { ...ticket, status: "USED", usedAt: now } : ticket)) };
}

/** Por lote, quanto este cliente já comprou: desconta da disponibilidade do catálogo. */
export function soldByBatch(orders: CustomerOrder[]): Record<string, number> {
  const sold: Record<string, number> = {};
  for (const order of orders) {
    for (const item of order.items) sold[item.batchId] = (sold[item.batchId] ?? 0) + item.quantity;
  }
  return sold;
}

/** Histórico para a conta de demonstração: mostra ingresso usado, a usar e Pix pendente. */
export function demoOrders(now: number): CustomerOrder[] {
  const past = pastEvents(now);
  const catalog = buildCatalog(now);
  const find = (slug: string) => catalog.find((event) => event.slug === slug)!;
  const item = (event: HubEvent, index: number, quantity: number): OrderItem => ({
    batchId: event.batches[index].id,
    name: event.batches[index].name,
    quantity,
    unitPriceCents: event.batches[index].priceCents,
  });

  const usedOrder = createOrder(past[0], [item(past[0], 0, 2)], { method: "CARD", status: "PAID", paidAt: now - 90 * DAY_MS, brand: "Visa", last4: "1111", installments: 3 }, now - 90 * DAY_MS);
  const usedAt = past[0].date + 3_600_000;
  const usedComedy = createOrder(past[1], [item(past[1], 0, 1)], { method: "PIX", status: "PAID", paidAt: now - 40 * DAY_MS }, now - 40 * DAY_MS);

  return [
    createOrder(find("noites-de-vinil"), [item(find("noites-de-vinil"), 1, 1)], { method: "PIX", status: "PENDING" }, now - 2 * 3_600_000),
    createOrder(find("o-ultimo-ato"), [item(find("o-ultimo-ato"), 0, 2)], { method: "PIX", status: "PAID", paidAt: now - 6 * DAY_MS }, now - 6 * DAY_MS),
    { ...usedComedy, tickets: usedComedy.tickets.map((ticket) => ({ ...ticket, status: "USED" as const, usedAt: past[1].date + 3_600_000 })) },
    { ...usedOrder, tickets: usedOrder.tickets.map((ticket, index) => (index === 0 ? { ...ticket, status: "USED" as const, usedAt } : ticket)) },
  ];
}
