import { TICKETERIA_MONTHLY_CENTS } from "./demoData";

/** Comissão usada de referência no simulador da ticketeria. Exemplo, como a mensalidade. */
export const REFERENCE_COMMISSION_RATE = 0.05;

export type TicketeriaInsights = {
  yearlyCents: number;
  costPerTicketCents: number;
  shareOfRevenue: number;
  /** Ingressos vendidos direto, em vez de por afiliado, que pagam a mensalidade. */
  ticketsToBreakEven: number;
};

/** Contas do simulador: só aritmética sobre os números que a pessoa informa. */
export function ticketeriaInsights(ticketsPerMonth: number, ticketPriceCents: number): TicketeriaInsights {
  const revenue = ticketsPerMonth * ticketPriceCents;
  const commissionPerTicket = ticketPriceCents * REFERENCE_COMMISSION_RATE;
  return {
    yearlyCents: TICKETERIA_MONTHLY_CENTS * 12,
    costPerTicketCents: ticketsPerMonth > 0 ? Math.round(TICKETERIA_MONTHLY_CENTS / ticketsPerMonth) : 0,
    shareOfRevenue: revenue > 0 ? (TICKETERIA_MONTHLY_CENTS / revenue) * 100 : 0,
    ticketsToBreakEven: commissionPerTicket > 0 ? Math.ceil(TICKETERIA_MONTHLY_CENTS / commissionPerTicket) : 0,
  };
}
