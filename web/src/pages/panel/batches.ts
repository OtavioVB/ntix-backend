import type { ApiNotification } from "../../api/client";
import type { BatchSaleState, BatchStatus, TicketBatch, TicketBatchRequest } from "../../api/events";
import { parseCents } from "../../platform/money";
import { localToIso } from "./events";

/** Limites dos value objects da API (REQ10, RN2 a RN4). */
export const BATCH_LIMITS = { name: 64, note: 140, minCents: 100, maxCents: 10_000_000, maxQuantity: 100_000 };

export const SALE_STATE_LABELS: Record<BatchSaleState, string> = {
  ON_SALE: "Em venda",
  UPCOMING: "Em breve",
  SOLD_OUT: "Esgotado",
  CLOSED: "Encerrado",
  PAUSED: "Pausado",
};

/** Formulário do lote: preço como a pessoa digita (R$) e datas como `datetime-local`. */
export type BatchForm = { name: string; note: string; price: string; quantity: string; salesStartsAt: string; salesEndsAt: string };

export type BatchFormField = keyof BatchForm;

export type BatchFormErrors = Partial<Record<BatchFormField | "general", string>>;

export const EMPTY_BATCH_FORM: BatchForm = { name: "", note: "", price: "", quantity: "", salesStartsAt: "", salesEndsAt: "" };

function normalize(value: string): string {
  return value.trim().split(/\s+/).filter(Boolean).join(" ");
}

/** Validação local espelhando a API; o fim da venda até o fim do evento é conferido contra o evento escolhido. */
export function validateBatch(form: BatchForm, eventEndsAt: string): BatchFormErrors {
  const errors: BatchFormErrors = {};
  const name = normalize(form.name);
  const cents = parseCents(form.price);
  const quantity = Number(form.quantity);

  if (name.length === 0 || name.length > BATCH_LIMITS.name) errors.name = "O nome do lote deve ter de 1 a 64 caracteres.";
  if (form.note.trim().length > BATCH_LIMITS.note) errors.note = "A observação do lote deve ter até 140 caracteres.";
  if (cents === null || cents < BATCH_LIMITS.minCents || cents > BATCH_LIMITS.maxCents) errors.price = "O preço deve ser de R$ 1,00 a R$ 100.000,00.";
  if (!Number.isInteger(quantity) || quantity < 1 || quantity > BATCH_LIMITS.maxQuantity) errors.quantity = "A quantidade deve ser de 1 a 100.000 ingressos.";

  const startsAt = localToIso(form.salesStartsAt);
  const endsAt = localToIso(form.salesEndsAt);

  if (!startsAt || !endsAt || endsAt <= startsAt) errors.salesEndsAt = "Informe o início e o fim da venda: o fim deve ser depois do início.";
  else if (new Date(endsAt).getTime() > new Date(eventEndsAt).getTime()) errors.salesEndsAt = "A venda do lote deve terminar até o fim do evento.";

  return errors;
}

export function hasBatchErrors(errors: BatchFormErrors): boolean {
  return Object.values(errors).some(Boolean);
}

/** Corpo do `TicketBatchV1Request`: preço em centavos, datas em UTC e observação vazia fora. */
export function toBatchRequest(form: BatchForm, status: BatchStatus = "ACTIVE"): TicketBatchRequest {
  const request: TicketBatchRequest = {
    name: normalize(form.name),
    priceCents: parseCents(form.price)!,
    quantity: Number(form.quantity),
    salesStartsAt: localToIso(form.salesStartsAt)!,
    salesEndsAt: localToIso(form.salesEndsAt)!,
    status,
  };

  const note = form.note.trim();
  if (note) request.note = note;

  return request;
}

/** Mesmo lote com outra situação: a API substitui todos os campos, então o corpo repete os atuais. */
export function withStatus(batch: TicketBatch, status: BatchStatus): TicketBatchRequest {
  const request: TicketBatchRequest = {
    name: batch.name,
    priceCents: batch.priceCents,
    quantity: batch.quantity,
    salesStartsAt: batch.salesStartsAt,
    salesEndsAt: batch.salesEndsAt,
    status,
  };

  if (batch.note) request.note = batch.note;

  return request;
}

const FIELD_BY_CODE: Record<string, BatchFormField> = {
  BATCH_NAME_INVALID: "name",
  BATCH_NAME_ALREADY_EXISTS: "name",
  BATCH_NOTE_INVALID: "note",
  BATCH_PRICE_INVALID: "price",
  BATCH_QUANTITY_INVALID: "quantity",
  BATCH_QUANTITY_BELOW_SOLD: "quantity",
  BATCH_SALES_PERIOD_INVALID: "salesEndsAt",
};

export function mapBatchNotifications(notifications: ApiNotification[]): BatchFormErrors {
  const errors: BatchFormErrors = {};

  for (const notification of notifications) {
    const field = FIELD_BY_CODE[notification.code] ?? "general";
    errors[field] ??= notification.message;
  }

  return errors;
}
