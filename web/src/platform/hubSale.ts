import { applyTiers } from "./money";
import { currentBalance, currentFeePlan } from "./selectors";
import type { LedgerEntry, Order, PlatformData } from "./types";

export type HubSale = {
  orderCode: string;
  eventId: string;
  eventName: string;
  customer: { email: string; name: string };
  items: { batchId: string; name: string; quantity: number; unitPriceCents: number }[];
  at: number;
};

/** Venda feita no hub de um evento publicado pelo produtor da sessão: chega nas vendas e na carteira dele. */
export function recordHubSale(data: PlatformData, sale: HubSale): PlatformData {
  const plan = currentFeePlan(data, sale.at);
  const customerId = `customer-${sale.customer.email}`;
  const customers = data.customers.some((customer) => customer.id === customerId)
    ? data.customers
    : [...data.customers, { id: customerId, name: sale.customer.name, email: sale.customer.email }];

  const orders: Order[] = [];
  const ledger: LedgerEntry[] = [];
  let balance = currentBalance(data);
  let ticketNumber = 0;

  sale.items.forEach((item, index) => {
    const fee = applyTiers(plan.tiers, item.unitPriceCents);
    const totalCents = item.quantity * item.unitPriceCents;
    const feeCents = item.quantity * fee.amountCents;
    const code = sale.items.length > 1 ? `${sale.orderCode}/${index + 1}` : sale.orderCode;
    // Mesma numeração que o cliente recebe: o QR Code dele precisa bater com o que a portaria procura.
    const ticketCodes = Array.from({ length: item.quantity }, () => `${sale.orderCode}-${String((ticketNumber += 1)).padStart(2, "0")}`);
    orders.push({
      id: `order-${code}`, code, eventId: sale.eventId, batchId: item.batchId, customerId, channel: "HUB",
      quantity: item.quantity, unitPriceCents: item.unitPriceCents, totalCents, feeCents, feeRule: fee.rule, commissionCents: 0, createdAt: sale.at, ticketCodes,
    });
    balance += totalCents;
    ledger.push({ id: `entry-${code}-sale`, at: sale.at, type: "SALE", description: "Venda de ingresso", detail: `${code} · ${sale.eventName} · ${item.name}`, amountCents: totalCents, balanceCents: balance });
    balance -= feeCents;
    ledger.push({ id: `entry-${code}-fee`, at: sale.at, type: "FEE", description: "Taxa de intermediação", detail: `${code} · ${fee.rule}`, amountCents: -feeCents, balanceCents: balance });
  });

  return { ...data, customers, orders: [...data.orders, ...orders], ledger: [...data.ledger, ...ledger] };
}
