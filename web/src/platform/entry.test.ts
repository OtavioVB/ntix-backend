import { describe, expect, it } from "vitest";
import { generateDemoData } from "./demoData";
import { admitTicket, entryStats, sampleTickets, ticketCodesOf } from "./entry";
import { recordHubSale } from "./hubSale";

const NOW = Date.UTC(2026, 10, 2, 12);
const data = generateDemoData(NOW, false);
const order = data.orders[0];
const [code] = ticketCodesOf(order);
const otherEvent = data.events.find((event) => event.id !== order.eventId)!;

describe("portaria (P-51)", () => {
  it("libera o ingresso uma vez e guarda quem validou", () => {
    const first = admitTicket(data, order.eventId, code, NOW, "Ana");
    expect(first.result.kind).toBe("ADMITTED");
    expect(entryStats(first.data, order.eventId).admitted).toBe(1);

    const again = admitTicket(first.data, order.eventId, code, NOW + 60_000, "Bruno");
    expect(again.result).toEqual({ kind: "ALREADY_USED", code, at: NOW, by: "Ana" });
    expect(again.data).toBe(first.data);
  });

  it("aceita o código com espaços e minúsculas, como chega de leitor ou teclado", () => {
    expect(admitTicket(data, order.eventId, `  ${code.toLowerCase()} `, NOW, "Ana").result.kind).toBe("ADMITTED");
  });

  it("recusa ingresso de outro evento sem dar entrada", () => {
    const outcome = admitTicket(data, otherEvent.id, code, NOW, "Ana");
    expect(outcome.result.kind).toBe("OTHER_EVENT");
    expect(outcome.data).toBe(data);
  });

  it("recusa código desconhecido ou que não parece ingresso", () => {
    expect(admitTicket(data, order.eventId, "NT-99999-01", NOW, "Ana").result.kind).toBe("NOT_FOUND");
    expect(admitTicket(data, order.eventId, "https://exemplo.test/?x=1", NOW, "Ana").result.kind).toBe("NOT_FOUND");
  });

  it("venda do hub gera os mesmos códigos que o cliente recebe", () => {
    const event = data.events[0];
    const [first, second] = event.batches;
    const sold = recordHubSale(data, {
      orderCode: "NXABC1",
      eventId: event.id,
      eventName: event.name,
      customer: { email: "c@exemplo.test", name: "Carla" },
      items: [
        { batchId: first.id, name: first.name, quantity: 2, unitPriceCents: 1000 },
        { batchId: second.id, name: second.name, quantity: 1, unitPriceCents: 2000 },
      ],
      at: NOW,
    });
    const codes = sold.orders.slice(-2).flatMap(ticketCodesOf);
    expect(codes).toEqual(["NXABC1-01", "NXABC1-02", "NXABC1-03"]);
    expect(admitTicket(sold, event.id, "NXABC1-03", NOW, "Ana").result.kind).toBe("ADMITTED");
  });

  it("sugere para teste só ingressos que ainda não entraram", () => {
    const { data: after } = admitTicket(data, order.eventId, code, NOW, "Ana");
    expect(sampleTickets(after, order.eventId, 5)).not.toContain(code);
  });
});
