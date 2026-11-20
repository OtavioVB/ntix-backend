import { postOrder } from "../api/orders";
import type { OrderItem } from "./orders";

/**
 * Resultado de pedir os ingressos à API (REQ6).
 * - `created`: a reserva vale até `expiresAt`, e o total é o do servidor.
 * - `unavailable`: a seleção deixou de valer (estoque, lote fora de venda, limite por pedido); volta ao evento com a mensagem.
 * - `refused`: falha que a seleção não explica (sessão expirada, rede); fica no checkout com a mensagem.
 */
export type PlaceOrderOutcome =
  | { kind: "created"; code: string; expiresAt: number; totalCents: number }
  | { kind: "unavailable"; message: string }
  | { kind: "refused"; message: string };

const SELECTION_REFUSED_STATUSES = [400, 404, 409];

export async function placeServerOrder(eventSlug: string, items: OrderItem[]): Promise<PlaceOrderOutcome> {
  const result = await postOrder({ eventSlug, salesChannel: "HUB", items: items.map(({ batchId, quantity }) => ({ batchId, quantity })) });

  if (result.ok) return { kind: "created", code: result.data.code, expiresAt: Date.parse(result.data.expiresAt), totalCents: result.data.totalCents };

  const message = result.notifications.map((notification) => notification.message).join(" ");

  return { kind: SELECTION_REFUSED_STATUSES.includes(result.status) ? "unavailable" : "refused", message };
}
