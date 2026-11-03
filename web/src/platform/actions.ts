import { DAY_MS, TICKETERIA_MONTHLY_CENTS } from "./demoData";
import { formatCents } from "./money";
import { currentBalance } from "./selectors";
import type { Batch, PlatformData, ProducerEvent } from "./types";

let idCounter = 0;

function newId(prefix: string): string {
  idCounter += 1;
  return `${prefix}-${Date.now().toString(36)}-${idCounter}`;
}

export type NewEvent = { name: string; venue: string; date: number; allowsAffiliates: boolean };

export function createEvent(data: PlatformData, input: NewEvent): { data: PlatformData; event: ProducerEvent } {
  const event: ProducerEvent = { ...input, id: newId("event"), status: "DRAFT", batches: [] };
  return { data: { ...data, events: [...data.events, event] }, event };
}

function updateEvent(data: PlatformData, eventId: string, change: (event: ProducerEvent) => ProducerEvent): PlatformData {
  return { ...data, events: data.events.map((event) => (event.id === eventId ? change(event) : event)) };
}

export function addBatch(data: PlatformData, eventId: string, input: Omit<Batch, "id">): PlatformData {
  return updateEvent(data, eventId, (event) => ({ ...event, batches: [...event.batches, { ...input, id: newId("batch") }] }));
}

/** Evento sem lote não tem o que vender. */
export function publishEvent(data: PlatformData, eventId: string): PlatformData {
  return updateEvent(data, eventId, (event) => (event.batches.length ? { ...event, status: "ON_SALE" } : event));
}

export function setAllowsAffiliates(data: PlatformData, eventId: string, allowsAffiliates: boolean): PlatformData {
  return updateEvent(data, eventId, (event) => ({ ...event, allowsAffiliates }));
}

/** Saque: valida contra o saldo. As regras de valor mínimo e prazo seguem em aberto nas premissas. */
export function requestWithdrawal(data: PlatformData, amountCents: number, now: number): { data: PlatformData } | { error: string } {
  const balance = currentBalance(data);
  if (amountCents <= 0) return { error: "Informe um valor maior que zero." };
  if (amountCents > balance) return { error: `O saldo disponível é de ${formatCents(balance)}.` };

  const entry = {
    id: newId("entry"),
    at: now,
    type: "WITHDRAWAL" as const,
    description: "Saque",
    detail: `Conta cadastrada · ${formatCents(amountCents)}`,
    amountCents: -amountCents,
    balanceCents: balance - amountCents,
  };
  return { data: { ...data, ledger: [...data.ledger, entry] } };
}

/** Ao contratar a ticketeria, entra a primeira mensalidade. */
export function contractTicketeria(data: PlatformData, now: number): PlatformData {
  const charge = {
    id: newId("charge"),
    description: "Mensalidade da ticketeria própria",
    type: "RECURRING" as const,
    amountCents: TICKETERIA_MONTHLY_CENTS,
    dueDate: now + 10 * DAY_MS,
  };
  return { ...data, charges: [...data.charges, charge] };
}
