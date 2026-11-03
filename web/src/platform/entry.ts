import type { CheckIn, Order, PlatformData } from "./types";

/* Portaria (P-51): o QR Code do ingresso carrega só o código. Cada código entra uma vez, no evento dele. */

export type EntryResult =
  | { kind: "ADMITTED"; code: string; batchName: string; holder: string; at: number }
  | { kind: "ALREADY_USED"; code: string; at: number; by: string }
  | { kind: "OTHER_EVENT"; code: string; eventName: string }
  | { kind: "NOT_FOUND"; code: string };

const CODE_PATTERN = /^[A-Z0-9][A-Z0-9/-]{3,39}$/;

/** Códigos de ingresso de um pedido. Venda do hub guarda os dela, numerados como o cliente recebeu. */
export function ticketCodesOf(order: Order): string[] {
  return order.ticketCodes ?? Array.from({ length: order.quantity }, (_, index) => `${order.code}-${String(index + 1).padStart(2, "0")}`);
}

/** Leitores e teclados trazem espaços e minúsculas: o código é comparado sempre do mesmo jeito. */
export function normalizeCode(raw: string): string {
  return raw.trim().toUpperCase();
}

function findTicket(data: PlatformData, code: string): Order | undefined {
  return data.orders.find((order) => ticketCodesOf(order).includes(code));
}

export function checkInsOf(data: PlatformData): CheckIn[] {
  return data.checkIns ?? [];
}

/** Integração pendente: em produção, a validação é da API (com os headers de T-06), que garante a
    entrada única mesmo com várias pessoas lendo ingressos ao mesmo tempo. */
export function admitTicket(data: PlatformData, eventId: string, raw: string, now: number, by: string): { data: PlatformData; result: EntryResult } {
  const code = normalizeCode(raw);
  const order = CODE_PATTERN.test(code) ? findTicket(data, code) : undefined;
  if (!order) return { data, result: { kind: "NOT_FOUND", code } };

  if (order.eventId !== eventId) {
    const eventName = data.events.find((event) => event.id === order.eventId)?.name ?? "outro evento";
    return { data, result: { kind: "OTHER_EVENT", code, eventName } };
  }

  const previous = checkInsOf(data).find((checkIn) => checkIn.ticketCode === code);
  if (previous) return { data, result: { kind: "ALREADY_USED", code, at: previous.at, by: previous.by } };

  const event = data.events.find((candidate) => candidate.id === eventId);
  const batchName = event?.batches.find((batch) => batch.id === order.batchId)?.name ?? "Ingresso";
  const holder = data.customers.find((customer) => customer.id === order.customerId)?.name ?? "";
  const checkIn: CheckIn = { ticketCode: code, eventId, at: now, by };
  return {
    data: { ...data, checkIns: [...checkInsOf(data), checkIn] },
    result: { kind: "ADMITTED", code, batchName, holder, at: now },
  };
}

export function entryStats(data: PlatformData, eventId: string) {
  const issued = data.orders.filter((order) => order.eventId === eventId).reduce((sum, order) => sum + order.quantity, 0);
  const admitted = checkInsOf(data).filter((checkIn) => checkIn.eventId === eventId).length;
  return { issued, admitted };
}

/** Ingressos ainda sem entrada, para testar a portaria na demonstração. */
export function sampleTickets(data: PlatformData, eventId: string, count: number): string[] {
  const used = new Set(checkInsOf(data).map((checkIn) => checkIn.ticketCode));
  return data.orders
    .filter((order) => order.eventId === eventId)
    .flatMap(ticketCodesOf)
    .filter((code) => !used.has(code))
    .slice(0, count);
}
