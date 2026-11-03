import { DAY_MS } from "./demoData";
import type { Channel, Order, PlatformData, ProducerEvent } from "./types";

export const CHANNEL_LABELS: Record<Channel, string> = {
  HUB: "Hub Ntix",
  STORE: "Ticketeria própria",
  AFFILIATE: "Links de afiliados",
};

export type Summary = { grossCents: number; feeCents: number; commissionCents: number; netCents: number; tickets: number; orders: number };

export function summarize(orders: Order[]): Summary {
  const summary: Summary = { grossCents: 0, feeCents: 0, commissionCents: 0, netCents: 0, tickets: 0, orders: orders.length };
  for (const order of orders) {
    summary.grossCents += order.totalCents;
    summary.feeCents += order.feeCents;
    summary.commissionCents += order.commissionCents;
    summary.netCents += order.totalCents - order.feeCents - order.commissionCents;
    summary.tickets += order.quantity;
  }
  return summary;
}

function ordersBetween(orders: Order[], from: number, to: number): Order[] {
  return orders.filter((order) => order.createdAt > from && order.createdAt <= to);
}

/** O período atual e o imediatamente anterior, para a comparação. */
export function periodSummary(orders: Order[], now: number, days: number) {
  const span = days * DAY_MS;
  return {
    orders: ordersBetween(orders, now - span, now),
    current: summarize(ordersBetween(orders, now - span, now)),
    previous: summarize(ordersBetween(orders, now - 2 * span, now - span)),
  };
}

/** Variação percentual. Nula quando não há base de comparação. */
export function percentChange(current: number, previous: number): number | null {
  if (previous === 0) return null;
  return ((current - previous) / previous) * 100;
}

export function shortDate(time: number): string {
  return new Date(time).toLocaleDateString("pt-BR", { day: "2-digit", month: "short" }).replace(" de ", "/").replace(".", "");
}

export function fullDate(time: number): string {
  return new Date(time).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric" });
}

/** Série do gráfico: diária até 30 dias, semanal acima disso. Receita em reais. */
export function salesSeries(orders: Order[], now: number, days: number) {
  const step = days > 30 ? 7 : 1;
  const buckets = Math.ceil(days / step);
  const series = Array.from({ length: buckets }, (_, index) => {
    const end = now - (buckets - 1 - index) * step * DAY_MS;
    return { end, label: shortDate(end), tickets: 0, revenue: 0 };
  });
  for (const order of orders) {
    const age = Math.floor((now - order.createdAt) / (step * DAY_MS));
    const bucket = series[buckets - 1 - age];
    if (bucket) {
      bucket.tickets += order.quantity;
      bucket.revenue += order.totalCents / 100;
    }
  }
  return series;
}

export function channelTotals(orders: Order[]) {
  const channels: Channel[] = ["HUB", "AFFILIATE", "STORE"];
  return channels
    .map((channel) => {
      const matching = orders.filter((order) => order.channel === channel);
      return { channel, label: CHANNEL_LABELS[channel], ...summarize(matching) };
    })
    .sort((a, b) => b.tickets - a.tickets);
}

export function batchSold(orders: Order[], batchId: string): number {
  return orders.reduce((sum, order) => (order.batchId === batchId ? sum + order.quantity : sum), 0);
}

export function eventStats(data: PlatformData, event: ProducerEvent) {
  const orders = data.orders.filter((order) => order.eventId === event.id);
  const capacity = event.batches.reduce((sum, batch) => sum + batch.quantity, 0);
  return { ...summarize(orders), capacity };
}

export function currentBalance(data: PlatformData): number {
  return data.ledger.length ? data.ledger[data.ledger.length - 1].balanceCents : 0;
}

export function customerStats(data: PlatformData) {
  return data.customers
    .map((customer) => {
      const orders = data.orders.filter((order) => order.customerId === customer.id);
      return { customer, ...summarize(orders), lastAt: Math.max(0, ...orders.map((order) => order.createdAt)) };
    })
    .filter((row) => row.orders > 0)
    .sort((a, b) => b.lastAt - a.lastAt);
}

export function affiliateStats(data: PlatformData) {
  return data.affiliates
    .map((affiliate) => ({ affiliate, ...summarize(data.orders.filter((order) => order.affiliateId === affiliate.id)) }))
    .sort((a, b) => b.tickets - a.tickets);
}

export function currentFeePlan(data: PlatformData, now: number) {
  return data.feePlans.find((plan) => plan.startsAt <= now && now <= plan.endsAt) ?? data.feePlans[data.feePlans.length - 1];
}

export type AttentionItem = { text: string; to: string };

/** O que pede uma ação do produtor agora. */
export function attentionItems(data: PlatformData, now: number): AttentionItem[] {
  const items: AttentionItem[] = [];

  for (const charge of data.charges) {
    if (!charge.paidAt) items.push({ text: `${charge.description} vence em ${fullDate(charge.dueDate)}`, to: "/painel/cobrancas" });
  }

  const plan = currentFeePlan(data, now);
  const planDays = plan ? Math.ceil((plan.endsAt - now) / DAY_MS) : 0;
  if (plan && planDays <= 30) items.push({ text: `Seu plano de intermediação termina em ${planDays} dias`, to: "/painel/plano" });

  for (const event of data.events) {
    if (event.status === "DRAFT") {
      items.push({
        text: event.batches.length ? `${event.name} está pronto para publicar` : `${event.name} ainda não tem lotes`,
        to: `/painel/eventos/${event.id}`,
      });
    }
    if (event.status !== "ON_SALE") continue;
    for (const batch of event.batches) {
      const left = batch.quantity - batchSold(data.orders, batch.id);
      const selling = batch.startsAt <= now && now < batch.endsAt;
      if (selling && left > 0 && left <= batch.quantity * 0.1) {
        items.push({ text: `${event.name}: restam ${left} ingressos no ${batch.name}`, to: `/painel/eventos/${event.id}` });
      }
    }
  }
  return items;
}
