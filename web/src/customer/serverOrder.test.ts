import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { clearCustomerTokens, storeCustomerTokens } from "../api/customerAuth";
import { postOrder, type ApiOrder } from "../api/orders";
import { placeServerOrder } from "./serverOrder";

const ORDER: ApiOrder = {
  code: "NX7K2QH9MP",
  status: "PENDING_PAYMENT",
  salesChannel: "HUB",
  createdAt: "2030-01-15T12:00:00.000Z",
  expiresAt: "2030-01-15T12:15:00.000Z",
  totalCents: 24000,
  event: { slug: "festival-horizonte", title: "Festival Horizonte", startsAt: "2030-03-20T21:00:00.000Z", venueName: "Parque das Dunas", city: "Recife", state: "PE" },
  items: [{ batchId: "b1", batchName: "Pista", quantity: 2, unitPriceCents: 12000, totalCents: 24000 }],
  tickets: [],
};

const ITEMS = [{ batchId: "b1", name: "Pista", quantity: 2, unitPriceCents: 12000 }];

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function sent(fetchMock: ReturnType<typeof vi.fn>, call = 0) {
  const [url, init] = fetchMock.mock.calls[call] as [string, RequestInit];
  return { url, init, headers: init.headers as Record<string, string>, body: init.body ? JSON.parse(init.body as string) : undefined };
}

beforeEach(() => {
  window.localStorage.clear();
  storeCustomerTokens({ accessToken: "access-1", refreshToken: "refresh-1" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearCustomerTokens();
});

describe("pedido na API", () => {
  it("envia o pedido com o token do comprador e sem nenhum preço no corpo", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(201, ORDER));
    vi.stubGlobal("fetch", fetchMock);

    const result = await postOrder({ eventSlug: "festival-horizonte", salesChannel: "HUB", items: [{ batchId: "b1", quantity: 2 }] });

    expect(result.ok).toBe(true);
    expect(sent(fetchMock).url).toMatch(/\/api\/v1\/orders$/);
    expect(sent(fetchMock).init.method).toBe("POST");
    expect(sent(fetchMock).headers.Authorization).toBe("Bearer access-1");
    expect(sent(fetchMock).headers).not.toHaveProperty("X-Execution-User");
    expect(sent(fetchMock).body).toEqual({ eventSlug: "festival-horizonte", salesChannel: "HUB", items: [{ batchId: "b1", quantity: 2 }] });
  });

  it("devolve o código, o prazo e o total do servidor ao criar", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(201, ORDER));
    vi.stubGlobal("fetch", fetchMock);

    const outcome = await placeServerOrder("festival-horizonte", ITEMS);

    expect(outcome).toEqual({ kind: "created", code: "NX7K2QH9MP", expiresAt: Date.parse("2030-01-15T12:15:00.000Z"), totalCents: 24000 });
    expect(sent(fetchMock).body.items).toEqual([{ batchId: "b1", quantity: 2 }]);
    expect(JSON.stringify(sent(fetchMock).body)).not.toContain("unitPriceCents");
  });

  it.each([
    [409, "BATCH_INSUFFICIENT_AVAILABILITY", "O lote \"Pista\" tem apenas 1 ingresso(s) disponível(is)."],
    [400, "BATCH_NOT_ON_SALE", "O lote \"2º lote\" não está em venda."],
    [404, "EVENT_NOT_FOUND", "O evento informado não existe."],
  ])("volta ao evento com a mensagem da API no %i (%s)", async (status, code, message) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(status, [{ code, message }])));

    expect(await placeServerOrder("festival-horizonte", ITEMS)).toEqual({ kind: "unavailable", message });
  });

  it("não volta ao evento quando a sessão expirou: a seleção não é o problema", async () => {
    window.localStorage.clear();
    clearCustomerTokens();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(401, undefined)));

    const outcome = await placeServerOrder("festival-horizonte", ITEMS);

    expect(outcome).toMatchObject({ kind: "refused" });
    expect(outcome.kind === "refused" && outcome.message).toContain("Sua sessão expirou");
  });

  it("recusa com a mensagem de rede quando a API não responde", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("falha")));

    const outcome = await placeServerOrder("festival-horizonte", ITEMS);

    expect(outcome.kind).toBe("refused");
    expect(outcome.kind === "refused" && outcome.message).toContain("Não foi possível falar com a Ntix");
  });
});
