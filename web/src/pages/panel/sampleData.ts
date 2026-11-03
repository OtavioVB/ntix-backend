/* Dados de exemplo do modo ensaio. Tudo deriva daqui, para o painel fechar entre si:
   a soma dos dias bate com os lotes, os canais e o extrato. As regras de taxa seguem o
   exemplo da premissa P-46 e a comissão de 5% é ilustrativa. */

const DAILY_TICKETS = [4, 6, 5, 9, 12, 7, 6, 8, 14, 18, 11, 9, 7, 10, 13, 16, 16, 15, 11, 9, 12, 14, 15, 18, 17, 12, 10, 11, 13, 14];

const FIRST_DAY = Date.UTC(2026, 9, 3);
const DAY_MS = 86_400_000;
const FIRST_BATCH_SIZE = 200;
const FIRST_BATCH_PRICE = 90;
const SECOND_BATCH_PRICE = 120;
const AFFILIATE_RATE = 0.05;

export function money(value: number): string {
  return value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function shortDate(time: number): string {
  return new Date(time)
    .toLocaleDateString("pt-BR", { day: "2-digit", month: "short", timeZone: "UTC" })
    .replace(" de ", "/")
    .replace(".", "");
}

export type SalesDay = { label: string; tickets: number; revenue: number };

function buildDays(): SalesDay[] {
  let sold = 0;
  return DAILY_TICKETS.map((tickets, index) => {
    const inFirstBatch = Math.max(0, Math.min(tickets, FIRST_BATCH_SIZE - sold));
    sold += tickets;
    return {
      label: shortDate(FIRST_DAY + index * DAY_MS),
      tickets,
      revenue: inFirstBatch * FIRST_BATCH_PRICE + (tickets - inFirstBatch) * SECOND_BATCH_PRICE,
    };
  });
}

export const SALES_DAYS = buildDays();

const totalTickets = DAILY_TICKETS.reduce((sum, tickets) => sum + tickets, 0);
const secondBatchSold = totalTickets - FIRST_BATCH_SIZE;
const grossRevenue = SALES_DAYS.reduce((sum, day) => sum + day.revenue, 0);

export const CHANNELS = [
  { name: "Hub Ntix", tickets: 230 },
  { name: "Links de afiliados", tickets: 112 },
];

const affiliateTickets = CHANNELS[1].tickets;

/* Faixas da premissa P-46: até R$ 100,00, fixa de R$ 1,00; acima, 1%. */
const intermediationFee = FIRST_BATCH_SIZE * 1 + secondBatchSold * SECOND_BATCH_PRICE * 0.01;
const affiliateCommission = affiliateTickets * SECOND_BATCH_PRICE * AFFILIATE_RATE;

export const SUMMARY = {
  tickets: totalTickets,
  gross: grossRevenue,
  fee: intermediationFee,
  commission: affiliateCommission,
  net: grossRevenue - intermediationFee - affiliateCommission,
};

export const SAMPLE_EVENT = {
  name: "Noite de Estreia",
  when: "Sáb, 12/dez · 20h",
  place: "Teatro Municipal",
  daysLeft: 41,
  batches: [
    { name: "1º lote", status: "Encerrado", live: false, price: FIRST_BATCH_PRICE, sold: FIRST_BATCH_SIZE, total: 200, window: "até 22/out" },
    { name: "2º lote", status: "Em venda", live: true, price: SECOND_BATCH_PRICE, sold: secondBatchSold, total: 300, window: "até 20/nov" },
    { name: "3º lote", status: "Agendado", live: false, price: 150, sold: 0, total: 300, window: "abre em 21/nov" },
  ],
};
