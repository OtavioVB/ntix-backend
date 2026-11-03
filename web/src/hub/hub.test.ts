import { describe, expect, it } from "vitest";
import { isValidCardNumber, isValidCvv, isValidExpiry, cardBrand, formatCardNumber, formatExpiry } from "../customer/payments";
import { confirmPayment, createOrder, demoOrders, itemsFromSelection, markTicketUsed, orderTotal, soldByBatch, ticketCount } from "../customer/orders";
import { BAGS, BOTTOMS, DEFAULT_AVATAR, DEFAULT_LOOKS, HATS, HAIR_STYLES, JEWELRY, SHOES, TOPS, normalizeAvatar, optionsFor, randomAvatar, switchStyle } from "../features/avatar/avatarOptions";
import { firstNameOf } from "./rules";
import { artToImage } from "./art";
import { CATEGORIES, buildCatalog } from "./catalog";
import { MAX_TICKETS_PER_ORDER, batchState, filterEvents, maxSelectable, onSaleBatches, priceFrom, remaining, scarcity } from "./rules";

const NOW = Date.UTC(2026, 10, 2, 15);
const DAY = 86_400_000;
const catalog = buildCatalog(NOW);
const byId = (slug: string) => catalog.find((event) => event.slug === slug)!;
const NO_FILTER = { query: "", category: "", city: "", when: "" } as const;

describe("catálogo", () => {
  it("cada evento tem imagem própria", () => {
    const images = new Set(catalog.map((event) => artToImage(event.art)));
    expect(images.size).toBe(catalog.length);
    for (const image of images) expect(image.startsWith("data:image/svg+xml")).toBe(true);
  });

  it("nenhum lote nasce vendendo além do total", () => {
    for (const event of catalog) for (const batch of event.batches) expect(batch.sold).toBeLessThanOrEqual(batch.quantity);
  });

  it("cobre todas as categorias e todo evento tem algo a vender ou está esgotado de verdade", () => {
    for (const category of CATEGORIES) expect(catalog.some((event) => event.category === category)).toBe(true);
    expect(catalog.filter((event) => onSaleBatches(event, NOW).length > 0).length).toBeGreaterThan(10);
  });
});

describe("disponibilidade por lote (P-24)", () => {
  const event = byId("festival-horizonte");
  const [first, second] = event.batches;

  it("respeita esgotado, período e vendas da sessão", () => {
    expect(batchState(first, NOW)).toBe("SOLD_OUT");
    expect(batchState(second, NOW)).toBe("ON_SALE");
    expect(batchState(second, second.startsAt - DAY)).toBe("UPCOMING");
    expect(batchState(second, second.endsAt)).toBe("CLOSED");
    expect(batchState(second, NOW, { [second.id]: remaining(second) })).toBe("SOLD_OUT");
  });

  it("limita a seleção ao que resta e ao máximo por pedido", () => {
    expect(maxSelectable(second, 0)).toBe(MAX_TICKETS_PER_ORDER);
    expect(maxSelectable(second, 4)).toBe(2);
    expect(maxSelectable(second, 0, { [second.id]: remaining(second) - 1 })).toBe(1);
  });

  it("o preço de partida ignora lote esgotado", () => {
    expect(priceFrom(event, NOW)).toBe(9_000);
    expect(priceFrom(byId("cidade-submersa"), NOW, { "cidade-submersa-1": 1 })).toBeNull();
  });

  it("só avisa escassez quando é verdade", () => {
    expect(scarcity(byId("cidade-submersa"), NOW)).toBe("LAST");
    expect(scarcity(byId("mar-de-sons"), NOW)).toBeNull();
    expect(scarcity(byId("cidade-submersa"), NOW, { "cidade-submersa-1": 1 })).toBe("SOLD_OUT");
  });
});

describe("filtros do hub", () => {
  it("ordena por data e esconde o que já passou", () => {
    const list = filterEvents(catalog, NO_FILTER, NOW);
    expect(list.length).toBe(catalog.length);
    expect(list.map((event) => event.date)).toEqual([...list.map((event) => event.date)].sort((a, b) => a - b));
    expect(filterEvents(catalog, NO_FILTER, NOW + 400 * DAY)).toHaveLength(0);
  });

  it("busca sem diferenciar acento e combina filtros", () => {
    expect(filterEvents(catalog, { ...NO_FILTER, query: "mare cultural" }, NOW).map((event) => event.slug)).toContain("festival-horizonte");
    expect(filterEvents(catalog, { ...NO_FILTER, category: "Teatro", city: "Rio de Janeiro" }, NOW).map((event) => event.slug)).toEqual(["improviso-geral"]);
    expect(filterEvents(catalog, { ...NO_FILTER, when: "7" }, NOW).map((event) => event.slug)).toEqual(["riso-solto"]);
  });
});

describe("pedidos", () => {
  const event = byId("festival-horizonte");
  const items = itemsFromSelection(event, { [event.batches[1].id]: 2, [event.batches[3].id]: 1 });

  it("calcula total e quantidade", () => {
    expect(ticketCount(items)).toBe(3);
    expect(orderTotal(items)).toBe(2 * 18_000 + 34_000);
  });

  it("cartão pago emite um ingresso de código único por unidade (P-35)", () => {
    const order = createOrder(event, items, { method: "CARD", status: "PAID", paidAt: NOW, brand: "Visa", last4: "1111", installments: 1 }, NOW);
    expect(order.tickets).toHaveLength(3);
    expect(new Set(order.tickets.map((ticket) => ticket.code)).size).toBe(3);
    expect(order.tickets.every((ticket) => ticket.status === "VALID")).toBe(true);
  });

  it("Pix pendente não tem ingresso até ser confirmado", () => {
    const order = createOrder(event, items, { method: "PIX", status: "PENDING" }, NOW);
    expect(order.tickets).toHaveLength(0);
    const paid = confirmPayment(order, NOW + 1000);
    expect(paid.payment).toMatchObject({ status: "PAID", paidAt: NOW + 1000 });
    expect(paid.tickets).toHaveLength(3);
    expect(confirmPayment(paid, NOW + 2000)).toBe(paid);
  });

  it("marca o uso de um único ingresso", () => {
    const order = createOrder(event, items, { method: "PIX", status: "PAID", paidAt: NOW }, NOW);
    const used = markTicketUsed(order, order.tickets[1].code, NOW);
    expect(used.tickets.map((ticket) => ticket.status)).toEqual(["VALID", "USED", "VALID"]);
    expect(used.tickets[1].usedAt).toBe(NOW);
  });

  it("nunca guarda o número do cartão", () => {
    const order = createOrder(event, items, { method: "CARD", status: "PAID", brand: "Visa", last4: "1111" }, NOW);
    expect(JSON.stringify(order)).not.toContain("4111111111111111");
  });

  it("desconta o que o cliente comprou da disponibilidade", () => {
    const order = createOrder(event, items, { method: "PIX", status: "PAID" }, NOW);
    const extra = soldByBatch([order]);
    expect(remaining(event.batches[1], extra)).toBe(remaining(event.batches[1]) - 2);
  });

  it("o histórico de demonstração cobre usado, a usar e pendente", () => {
    const orders = demoOrders(NOW);
    const tickets = orders.flatMap((order) => order.tickets);
    expect(tickets.some((ticket) => ticket.status === "USED")).toBe(true);
    expect(tickets.some((ticket) => ticket.status === "VALID")).toBe(true);
    expect(orders.some((order) => order.payment.status === "PENDING" && order.tickets.length === 0)).toBe(true);
    expect(orders.some((order) => order.payment.method === "CARD")).toBe(true);
  });
});

describe("cartão", () => {
  it("valida o número pelo dígito verificador", () => {
    expect(isValidCardNumber("4111 1111 1111 1111")).toBe(true);
    expect(isValidCardNumber("4000 0000 0000 0002")).toBe(true);
    expect(isValidCardNumber("4111 1111 1111 1112")).toBe(false);
    expect(isValidCardNumber("1234")).toBe(false);
  });

  it("reconhece a bandeira e aplica máscaras", () => {
    expect(cardBrand("4111")).toBe("Visa");
    expect(cardBrand("5500")).toBe("Mastercard");
    expect(formatCardNumber("4111111111111111")).toBe("4111 1111 1111 1111");
    expect(formatExpiry("1228")).toBe("12/28");
  });

  it("recusa validade vencida e código malformado", () => {
    expect(isValidExpiry("12/40", NOW)).toBe(true);
    expect(isValidExpiry("10/26", NOW)).toBe(false);
    expect(isValidExpiry("13/40", NOW)).toBe(false);
    expect(isValidExpiry("1240", NOW)).toBe(false);
    expect(isValidCvv("123")).toBe(true);
    expect(isValidCvv("12")).toBe(false);
  });
});

describe("avatar", () => {
  const labels = <T extends string>(options: readonly { id: T; label: string; style?: "FEMININE" | "MASCULINE" }[], style: "FEMININE" | "MASCULINE") => optionsFor(options, style).map((option) => option.label);

  it("separa roupas femininas e masculinas", () => {
    expect(labels(TOPS, "FEMININE")).toEqual(expect.arrayContaining(["Corset", "Blusinha ombro a ombro", "Cropped", "Vestido de alcinha"]));
    expect(labels(TOPS, "FEMININE")).not.toContain("Regata");
    expect(labels(TOPS, "MASCULINE")).toEqual(expect.arrayContaining(["Regata", "Camisa larga", "Camisa aberta", "Camiseta de banda"]));
    expect(labels(TOPS, "MASCULINE")).not.toContain("Corset");
    expect(labels(BOTTOMS, "FEMININE")).toContain("Minissaia");
    expect(labels(BOTTOMS, "MASCULINE")).not.toContain("Minissaia");
    expect(labels(BOTTOMS, "MASCULINE")).toContain("Bermuda");
  });

  it("separa acessórios e cabelos", () => {
    expect(labels(JEWELRY, "FEMININE")).toContain("Argolas");
    expect(labels(JEWELRY, "MASCULINE")).toContain("Corrente");
    expect(labels(BAGS, "FEMININE")).toContain("Bolsinha");
    expect(labels(BAGS, "MASCULINE")).toContain("Pochete");
    expect(labels(HATS, "FEMININE")).toContain("Tiara de flores");
    expect(labels(HATS, "MASCULINE")).toContain("Boné");
    expect(labels(HAIR_STYLES, "FEMININE").length).toBeGreaterThanOrEqual(5);
    expect(labels(HAIR_STYLES, "MASCULINE").length).toBeGreaterThanOrEqual(5);
    expect(SHOES.length).toBeGreaterThanOrEqual(4);
  });

  it("trocar de estilo veste o look do novo estilo e mantém pele e olhos", () => {
    const changed = switchStyle({ ...DEFAULT_LOOKS.FEMININE, skin: "#553320", eyes: "#3d7fb8", top: "corset" }, "MASCULINE");
    expect(changed).toMatchObject({ style: "MASCULINE", skin: "#553320", eyes: "#3d7fb8" });
    expect(labels(TOPS, "MASCULINE")).toContain(TOPS.find((top) => top.id === changed.top)!.label);
  });

  it("nunca deixa peça de um estilo no outro", () => {
    const fixed = normalizeAvatar({ style: "MASCULINE", top: "corset", bottom: "mini", jewelry: "hoops", hat: "flowers", beard: "full" });
    expect(fixed).toMatchObject({ top: DEFAULT_LOOKS.MASCULINE.top, bottom: DEFAULT_LOOKS.MASCULINE.bottom, jewelry: "none", hat: "none", beard: "full" });
    expect(normalizeAvatar({ style: "FEMININE", beard: "full" }).beard).toBe("none");
  });

  it("aproveita avatares salvos em versões antigas", () => {
    expect(normalizeAvatar({ skin: "#553320", hairStyle: "bun", hairColor: "#d65a8f", outfit: "dress", outfitColor: "#b3342f" })).toMatchObject({ style: "FEMININE", skin: "#553320", hairStyle: "spacebuns", top: "slip", topColor: "#b3342f" });
    expect(normalizeAvatar({ hairStyle: "quiff", outfit: "hoodie" })).toMatchObject({ style: "MASCULINE", top: "oversized" });
  });

  it("não quebra com dados desconhecidos", () => {
    expect(normalizeAvatar({ hairStyle: "inexistente", top: 42 })).toMatchObject({ hairStyle: DEFAULT_AVATAR.hairStyle, top: DEFAULT_AVATAR.top });
    expect(normalizeAvatar(null)).toEqual(DEFAULT_AVATAR);
  });

  it("o sorteio respeita o estilo", () => {
    for (let index = 0; index < 40; index++) {
      const look = randomAvatar(Math.random, "MASCULINE");
      expect(normalizeAvatar(look)).toEqual(look);
      expect(look.style).toBe("MASCULINE");
    }
    for (let index = 0; index < 40; index++) {
      const look = randomAvatar(Math.random, "FEMININE");
      expect(look.beard).toBe("none");
      expect(normalizeAvatar(look)).toEqual(look);
    }
  });
});

describe("primeiro nome", () => {
  it("usa só o primeiro nome", () => {
    expect(firstNameOf("  Carla Maria de Souza ")).toBe("Carla");
    expect(firstNameOf("Bruno")).toBe("Bruno");
  });
});
