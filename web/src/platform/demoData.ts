import { applyTiers, formatCents } from "./money";
import type { Affiliate, Batch, Channel, Charge, CommissionPlan, Customer, FeePlan, LedgerEntry, Order, PlatformData, ProducerEvent } from "./types";

export const DAY_MS = 86_400_000;

/** Valor de exemplo da ticketeria própria, enquanto a tabela real não existe. */
export const TICKETERIA_MONTHLY_CENTS = 30_000;

const FEE_PLAN_TIERS: FeePlan["tiers"] = [
  { upToCents: 10_000, type: "FIXED", value: 100 },
  { upToCents: null, type: "PERCENTAGE", value: 1 },
];

const COMMISSION_PLAN: CommissionPlan = {
  id: "commission-default",
  name: "Plano padrão de afiliados",
  tiers: [{ upToCents: null, type: "PERCENTAGE", value: 5 }],
};

const FIRST_NAMES = ["Ana", "Bruno", "Carla", "Diego", "Elisa", "Felipe", "Gabriela", "Heitor", "Isabela", "João", "Karen", "Lucas", "Marina", "Nícolas", "Olívia", "Pedro", "Quésia", "Rafael", "Sofia", "Tiago"];
const LAST_NAMES = ["Almeida", "Barros", "Campos", "Dias", "Esteves", "Farias", "Gomes", "Horta", "Ipiranga", "Jardim", "Lima", "Moura"];

function seededRandom(seed: number) {
  let state = seed;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let mixed = Math.imul(state ^ (state >>> 15), 1 | state);
    mixed = (mixed + Math.imul(mixed ^ (mixed >>> 7), 61 | mixed)) ^ mixed;
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4_294_967_296;
  };
}

function feePlans(now: number): FeePlan[] {
  return [
    { id: "plan-current", startsAt: now - 30 * DAY_MS, endsAt: now + 150 * DAY_MS, tiers: FEE_PLAN_TIERS },
  ];
}

/** Plataforma de quem acabou de ser aprovado: sem vendas, só com o plano negociado. */
export function emptyPlatformData(now: number): PlatformData {
  return {
    isDemo: false,
    events: [],
    customers: [],
    affiliates: [],
    orders: [],
    ledger: [],
    charges: [],
    feePlans: [{ id: "plan-current", startsAt: now, endsAt: now + 180 * DAY_MS, tiers: FEE_PLAN_TIERS }],
    commissionPlans: [COMMISSION_PLAN],
  };
}

function batch(id: string, name: string, priceCents: number, quantity: number, startsAt: number, endsAt: number): Batch {
  return { id, name, priceCents, quantity, startsAt, endsAt };
}

function demoEvents(now: number): ProducerEvent[] {
  const at = (days: number) => now + days * DAY_MS;
  return [
    {
      id: "event-festival", name: "Festival Maré Alta", venue: "Parque da Orla", date: at(-55), status: "ENDED", allowsAffiliates: true,
      batches: [batch("festival-1", "1º lote", 8_000, 260, at(-120), at(-95)), batch("festival-2", "2º lote", 11_000, 420, at(-95), at(-55))],
    },
    {
      id: "event-estreia", name: "Noite de Estreia", venue: "Teatro Municipal", date: at(41), status: "ON_SALE", allowsAffiliates: true,
      batches: [batch("estreia-1", "1º lote", 9_000, 320, at(-54), at(-30)), batch("estreia-2", "2º lote", 12_000, 420, at(-30), at(19)), batch("estreia-3", "3º lote", 15_000, 300, at(20), at(41))],
    },
    {
      id: "event-aurora", name: "Temporada Aurora", venue: "Casa Aurora", date: at(76), status: "ON_SALE", allowsAffiliates: false,
      batches: [batch("aurora-1", "Lote único", 6_500, 500, at(-26), at(76))],
    },
    {
      id: "event-final", name: "Final Regional", venue: "Arena Norte", date: at(120), status: "DRAFT", allowsAffiliates: true,
      batches: [],
    },
  ];
}

/** Vendas fictícias determinísticas: o mesmo instante gera sempre os mesmos dados. */
export function generateDemoData(now: number, hasStore: boolean): PlatformData {
  const random = seededRandom(20261101);
  const pick = <T,>(items: readonly T[]) => items[Math.floor(random() * items.length)];

  const customers: Customer[] = Array.from({ length: 220 }, (_, index) => ({
    id: `customer-${index + 1}`,
    name: `${pick(FIRST_NAMES)} ${pick(LAST_NAMES)}`,
    email: `cliente${index + 1}@exemplo.test`,
  }));
  const affiliates: Affiliate[] = ["Rede Palco", "Agenda Cultural", "Bia Eventos", "Turma do Rolê", "Guia da Noite", "Coletivo Norte"].map((name, index) => ({
    id: `affiliate-${index + 1}`,
    name,
    shareCode: `a7f${(index + 11).toString(16)}c${index}`,
  }));

  const events = demoEvents(now);
  const sold = new Map<string, number>();
  const orders: Order[] = [];
  const ledger: LedgerEntry[] = [];
  let balance = 0;
  let sequence = 0;

  const record = (at: number, type: LedgerEntry["type"], description: string, detail: string, amountCents: number) => {
    balance += amountCents;
    sequence += 1;
    ledger.push({ id: `entry-${sequence}`, at, type, description, detail, amountCents, balanceCents: balance });
  };

  for (let daysAgo = 120; daysAgo >= 0; daysAgo--) {
    const dayStart = now - daysAgo * DAY_MS;
    const weekend = new Date(dayStart).getDay() % 6 === 0;
    const ordersToday = Math.round(2 + random() * 5 + (weekend ? 4 : 0) + (120 - daysAgo) / 40);

    for (let index = 0; index < ordersToday; index++) {
      const createdAt = dayStart - Math.floor(random() * 0.9 * DAY_MS);
      const open = events.flatMap((event) =>
        event.batches
          .filter((candidate) => candidate.startsAt <= createdAt && createdAt < candidate.endsAt && (sold.get(candidate.id) ?? 0) < candidate.quantity)
          .map((candidate) => ({ event, batch: candidate })),
      );
      if (open.length === 0) continue;

      const { event, batch: chosen } = pick(open);
      const quantity = Math.min(1 + Math.floor(random() * random() * 4), chosen.quantity - (sold.get(chosen.id) ?? 0));
      sold.set(chosen.id, (sold.get(chosen.id) ?? 0) + quantity);

      const roll = random();
      let channel: Channel = "HUB";
      if (event.allowsAffiliates && roll < 0.28) channel = "AFFILIATE";
      else if (hasStore && roll < 0.68) channel = "STORE";

      const fee = applyTiers(FEE_PLAN_TIERS, chosen.priceCents);
      const commission = channel === "AFFILIATE" ? applyTiers(COMMISSION_PLAN.tiers, chosen.priceCents) : null;
      const totalCents = quantity * chosen.priceCents;
      const feeCents = quantity * fee.amountCents;
      const commissionCents = commission ? quantity * commission.amountCents : 0;
      const affiliateId = channel === "AFFILIATE" ? pick(affiliates).id : undefined;
      const code = `NT-${(10_000 + orders.length).toString()}`;

      orders.push({
        id: `order-${orders.length + 1}`, code, eventId: event.id, batchId: chosen.id, customerId: pick(customers).id, channel, affiliateId,
        quantity, unitPriceCents: chosen.priceCents, totalCents, feeCents, feeRule: fee.rule, commissionCents, commissionRule: commission?.rule, createdAt,
      });
      record(createdAt, "SALE", "Venda de ingresso", `${code} · ${event.name} · ${chosen.name}`, totalCents);
      record(createdAt, "FEE", "Taxa de intermediação", `${code} · ${fee.rule}`, -feeCents);
      if (commission) record(createdAt, "COMMISSION", "Comissão de afiliado", `${code} · ${commission.rule}`, -commissionCents);
    }

    if (daysAgo === 90 || daysAgo === 60 || daysAgo === 30) {
      const amount = Math.floor(balance * 0.7 / 100) * 100;
      record(dayStart, "WITHDRAWAL", "Saque", `Conta cadastrada · ${formatCents(amount)}`, -amount);
    }
  }

  const charges: Charge[] = hasStore
    ? [3, 2, 1, 0].map((monthsAgo) => ({
        id: `charge-${monthsAgo}`,
        description: "Mensalidade da ticketeria própria",
        type: "RECURRING",
        amountCents: TICKETERIA_MONTHLY_CENTS,
        dueDate: now - monthsAgo * 30 * DAY_MS + 9 * DAY_MS,
        paidAt: monthsAgo > 0 ? now - monthsAgo * 30 * DAY_MS + 3 * DAY_MS : undefined,
      }))
    : [];

  return { isDemo: true, events, customers, affiliates, orders, ledger, charges, feePlans: feePlans(now), commissionPlans: [COMMISSION_PLAN] };
}
