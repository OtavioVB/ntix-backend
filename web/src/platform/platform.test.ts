import { describe, expect, it } from "vitest";
import { addBatch, contractTicketeria, createEvent, publishEvent, requestWithdrawal } from "./actions";
import { DAY_MS, TICKETERIA_MONTHLY_CENTS, emptyPlatformData, generateDemoData } from "./demoData";
import { ticketeriaInsights } from "./insights";
import { applyTiers, formatCents, formatSigned, parseCents } from "./money";
import { attentionItems, batchSold, channelTotals, currentBalance, customerStats, periodSummary, percentChange, salesSeries, summarize } from "./selectors";

const NOW = Date.UTC(2026, 10, 1, 15);

describe("faixas de taxa (P-46)", () => {
  const tiers = [
    { upToCents: 10_000, type: "FIXED" as const, value: 100 },
    { upToCents: null, type: "PERCENTAGE" as const, value: 1 },
  ];

  it("aplica a faixa ao valor inteiro, com limite inclusivo", () => {
    expect(applyTiers(tiers, 8_000).amountCents).toBe(100);
    expect(applyTiers(tiers, 10_000).amountCents).toBe(100);
    expect(applyTiers(tiers, 10_001).amountCents).toBe(100);
    expect(applyTiers(tiers, 12_000).amountCents).toBe(120);
  });

  it("registra a regra aplicada (P-47)", () => {
    expect(applyTiers(tiers, 8_000).rule).toContain("fixa");
    expect(applyTiers(tiers, 12_000).rule).toContain("1%");
  });
});

describe("dinheiro", () => {
  it("interpreta valores digitados em português", () => {
    expect(parseCents("1.234,50")).toBe(123_450);
    expect(parseCents("R$ 90")).toBe(9_000);
    expect(parseCents("10,5")).toBe(1_050);
    expect(parseCents("abc")).toBeNull();
    expect(parseCents("1,234")).toBeNull();
  });

  it("formata com sinal", () => {
    expect(formatSigned(12_000)).toBe("+ 120,00");
    expect(formatSigned(-120)).toBe("− 1,20");
  });
});

describe("dados de demonstração", () => {
  const data = generateDemoData(NOW, true);

  it("são determinísticos", () => {
    expect(generateDemoData(NOW, true).orders).toEqual(data.orders);
  });

  it("fecham o saldo: cada lançamento acumula sobre o anterior", () => {
    let running = 0;
    for (const entry of data.ledger) {
      running += entry.amountCents;
      expect(entry.balanceCents).toBe(running);
    }
    expect(currentBalance(data)).toBe(running);
    expect(running).toBeGreaterThanOrEqual(0);
  });

  it("explicam a carteira a partir dos pedidos", () => {
    const summary = summarize(data.orders);
    const total = (type: string) => data.ledger.filter((entry) => entry.type === type).reduce((sum, entry) => sum + entry.amountCents, 0);
    expect(total("SALE")).toBe(summary.grossCents);
    expect(total("FEE")).toBe(-summary.feeCents);
    expect(total("COMMISSION")).toBe(-summary.commissionCents);
    expect(summary.netCents).toBe(summary.grossCents - summary.feeCents - summary.commissionCents);
  });

  it("nunca vendem além da quantidade do lote", () => {
    for (const event of data.events) {
      for (const batch of event.batches) expect(batchSold(data.orders, batch.id)).toBeLessThanOrEqual(batch.quantity);
    }
  });

  it("só têm comissão em venda de afiliado, e nunca em evento que não aceita", () => {
    for (const order of data.orders) {
      expect(order.commissionCents > 0).toBe(order.channel === "AFFILIATE");
      if (order.channel === "AFFILIATE") {
        expect(data.events.find((event) => event.id === order.eventId)?.allowsAffiliates).toBe(true);
      }
    }
  });

  it("sem ticketeria contratada não há venda nem mensalidade da ticketeria", () => {
    const withoutStore = generateDemoData(NOW, false);
    expect(withoutStore.orders.some((order) => order.channel === "STORE")).toBe(false);
    expect(withoutStore.charges).toHaveLength(0);
    expect(data.orders.some((order) => order.channel === "STORE")).toBe(true);
    expect(data.charges[0].amountCents).toBe(TICKETERIA_MONTHLY_CENTS);
  });
});

describe("indicadores", () => {
  const data = generateDemoData(NOW, true);

  it("o período recorta os pedidos e a série soma o mesmo total", () => {
    for (const days of [7, 30, 90]) {
      const { current, orders } = periodSummary(data.orders, NOW, days);
      const series = salesSeries(orders, NOW, days);
      expect(series.reduce((sum, bucket) => sum + bucket.tickets, 0)).toBe(current.tickets);
      expect(Math.round(series.reduce((sum, bucket) => sum + bucket.revenue, 0) * 100)).toBe(current.grossCents);
    }
  });

  it("os canais somam o total", () => {
    const summary = summarize(data.orders);
    expect(channelTotals(data.orders).reduce((sum, channel) => sum + channel.tickets, 0)).toBe(summary.tickets);
  });

  it("os clientes somam o total", () => {
    expect(customerStats(data).reduce((sum, row) => sum + row.tickets, 0)).toBe(summarize(data.orders).tickets);
  });

  it("calcula a variação e não inventa base de comparação", () => {
    expect(percentChange(150, 100)).toBe(50);
    expect(percentChange(50, 100)).toBe(-50);
    expect(percentChange(50, 0)).toBeNull();
  });

  it("aponta o que pede ação", () => {
    const items = attentionItems(data, NOW);
    expect(items.some((item) => item.text.includes("Final Regional ainda não tem lotes"))).toBe(true);
    expect(items.some((item) => item.text.includes("Mensalidade da ticketeria"))).toBe(true);
  });

  it("avisa quando o plano está para vencer", () => {
    const empty = emptyPlatformData(NOW);
    expect(attentionItems(empty, NOW)).toHaveLength(0);
    const nearEnd = NOW + 170 * DAY_MS;
    expect(attentionItems(empty, nearEnd)[0].text).toContain("plano de intermediação");
  });
});

describe("ações", () => {
  it("cria evento como rascunho e só publica com lote", () => {
    const created = createEvent(emptyPlatformData(NOW), { name: "Show", venue: "Casa", date: NOW + 30 * DAY_MS, allowsAffiliates: false });
    expect(created.event.status).toBe("DRAFT");
    expect(publishEvent(created.data, created.event.id).events[0].status).toBe("DRAFT");

    const withBatch = addBatch(created.data, created.event.id, { name: "1º lote", priceCents: 5_000, quantity: 10, startsAt: NOW, endsAt: NOW + DAY_MS });
    expect(publishEvent(withBatch, created.event.id).events[0].status).toBe("ON_SALE");
  });

  it("saque respeita o saldo e entra no extrato", () => {
    const data = generateDemoData(NOW, false);
    const balance = currentBalance(data);
    expect(requestWithdrawal(data, balance + 1, NOW)).toEqual({ error: `O saldo disponível é de ${formatCents(balance)}.` });
    expect(requestWithdrawal(data, 0, NOW)).toHaveProperty("error");

    const result = requestWithdrawal(data, 1_000, NOW);
    if ("error" in result) throw new Error(result.error);
    expect(currentBalance(result.data)).toBe(balance - 1_000);
  });

  it("contratar a ticketeria gera a primeira mensalidade em aberto", () => {
    const data = contractTicketeria(emptyPlatformData(NOW), NOW);
    expect(data.charges[0]).toMatchObject({ amountCents: TICKETERIA_MONTHLY_CENTS, type: "RECURRING" });
    expect(data.charges[0].paidAt).toBeUndefined();
  });
});

describe("simulador da ticketeria", () => {
  it("faz as contas sobre o que a pessoa informa", () => {
    const insights = ticketeriaInsights(300, 12_000);
    expect(insights.yearlyCents).toBe(360_000);
    expect(insights.costPerTicketCents).toBe(100);
    expect(insights.shareOfRevenue).toBeCloseTo(0.83, 1);
    expect(insights.ticketsToBreakEven).toBe(50);
  });

  it("não divide por zero", () => {
    expect(ticketeriaInsights(0, 0)).toMatchObject({ costPerTicketCents: 0, shareOfRevenue: 0, ticketsToBreakEven: 0 });
  });
});
