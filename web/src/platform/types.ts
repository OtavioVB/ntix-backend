/* Modelo da plataforma do produtor. Valores em centavos: dinheiro não passa por ponto flutuante.
   Espelha o MER (docs/mer.md) no que a tela precisa, sem repetir colunas de auditoria. */

export type Channel = "HUB" | "STORE" | "AFFILIATE";

export type Batch = {
  id: string;
  name: string;
  priceCents: number;
  quantity: number;
  startsAt: number;
  endsAt: number;
};

export type EventStatus = "DRAFT" | "ON_SALE" | "ENDED";

export type ProducerEvent = {
  id: string;
  name: string;
  venue: string;
  date: number;
  status: EventStatus;
  allowsAffiliates: boolean;
  batches: Batch[];
};

export type Customer = { id: string; name: string; email: string };

export type Affiliate = { id: string; name: string; shareCode: string };

export type Order = {
  id: string;
  code: string;
  eventId: string;
  batchId: string;
  customerId: string;
  channel: Channel;
  affiliateId?: string;
  quantity: number;
  unitPriceCents: number;
  totalCents: number;
  feeCents: number;
  feeRule: string;
  commissionCents: number;
  commissionRule?: string;
  createdAt: number;
  /** Códigos dos ingressos, quando vêm de fora (venda do hub). Sem eles, saem do código do pedido. */
  ticketCodes?: string[];
};

/** Entrada registrada na portaria: um ingresso entra uma vez só. */
export type CheckIn = { ticketCode: string; eventId: string; at: number; by: string };

export type LedgerType = "SALE" | "FEE" | "COMMISSION" | "WITHDRAWAL";

export type LedgerEntry = {
  id: string;
  at: number;
  type: LedgerType;
  description: string;
  detail: string;
  amountCents: number;
  balanceCents: number;
};

export type ChargeType = "RECURRING" | "ONE_TIME" | "INTERMEDIATION_FEE";

export type Charge = {
  id: string;
  description: string;
  type: ChargeType;
  amountCents: number;
  dueDate: number;
  paidAt?: number;
};

export type FeeTier = {
  /** Limite superior da faixa, inclusivo. Nulo = sem limite. */
  upToCents: number | null;
  type: "FIXED" | "PERCENTAGE";
  /** Centavos se FIXED, porcentagem se PERCENTAGE. */
  value: number;
};

export type FeePlan = { id: string; startsAt: number; endsAt: number; tiers: FeeTier[] };

export type CommissionPlan = { id: string; name: string; tiers: FeeTier[] };

export type PlatformData = {
  /** Vendas fictícias geradas para demonstração. */
  isDemo: boolean;
  events: ProducerEvent[];
  customers: Customer[];
  affiliates: Affiliate[];
  orders: Order[];
  ledger: LedgerEntry[];
  charges: Charge[];
  feePlans: FeePlan[];
  commissionPlans: CommissionPlan[];
  /** Opcional: dados guardados antes da portaria não têm. */
  checkIns?: CheckIn[];
};
