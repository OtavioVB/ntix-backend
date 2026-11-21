
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { clearSessionTokens, storeSessionTokens } from "../../api/auth";
import { createTicketBatch, publishProducerEvent, updateTicketBatch, type TicketBatch } from "../../api/events";

import { EMPTY_BATCH_FORM, mapBatchNotifications, toBatchRequest, validateBatch, withStatus, type BatchForm } from "./batches";

const EVENT_ENDS_AT = "2030-03-21T06:00:00.000Z";

const VALID_FORM: BatchForm = { name: "1º  lote - Pista", note: "", price: "120,50", quantity: "500", salesStartsAt: "2030-01-01T10:00", salesEndsAt: "2030-02-01T10:00" };

function apiBatch(overrides: Partial<TicketBatch> = {}): TicketBatch {
  return {
    id: "b1", name: "VIP", priceCents: 30000, quantity: 50, soldQuantity: 0, reservedQuantity: 0, availableQuantity: 50,
    salesStartsAt: "2030-01-01T13:00:00.000Z", salesEndsAt: "2030-02-01T13:00:00.000Z", status: "ACTIVE", saleState: "ON_SALE", grossCents: 0, paidOrders: 0,
    ...overrides,
  };
}

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function sentRequest(fetchMock: ReturnType<typeof vi.fn>, call: number) {
  const [url, init] = fetchMock.mock.calls[call] as [string, RequestInit];
  return { url, init, body: init.body ? JSON.parse(init.body as string) : undefined };
}

beforeAll(() => {
  window.scrollTo = vi.fn();
});

beforeEach(() => {
  window.sessionStorage.clear();
  storeSessionTokens({ accessToken: "token-123", refreshToken: "refresh-123" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearSessionTokens();
});

describe("regras do formulário de lote", () => {
  it("aceita o lote válido e envia o preço em centavos", () => {
    expect(validateBatch(VALID_FORM, EVENT_ENDS_AT)).toEqual({});

    const request = toBatchRequest(VALID_FORM);
    expect(request).toMatchObject({ name: "1º lote - Pista", priceCents: 12050, quantity: 500, status: "ACTIVE" });
    expect(request).not.toHaveProperty("note");
  });

  it("valida como a API: nome, preço mínimo, quantidade e janela até o fim do evento", () => {
    const errors = validateBatch({ ...EMPTY_BATCH_FORM, price: "0,50", quantity: "0" }, EVENT_ENDS_AT);
    expect(Object.keys(errors).sort()).toEqual(["name", "price", "quantity", "salesEndsAt"]);

    const late = validateBatch({ ...VALID_FORM, salesEndsAt: "2030-04-01T10:00" }, EVENT_ENDS_AT);
    expect(late.salesEndsAt).toBe("A venda do lote deve terminar até o fim do evento.");
  });

  it("pausa repetindo os campos atuais, porque a edição substitui tudo", () => {
    expect(withStatus(apiBatch({ note: "Meia" }), "INACTIVE")).toEqual({
      name: "VIP", note: "Meia", priceCents: 30000, quantity: 50,
      salesStartsAt: "2030-01-01T13:00:00.000Z", salesEndsAt: "2030-02-01T13:00:00.000Z", status: "INACTIVE",
    });
  });

  it("leva os códigos da API para os campos", () => {
    expect(mapBatchNotifications([
      { code: "BATCH_NAME_ALREADY_EXISTS", message: "Nome repetido." },
      { code: "BATCH_QUANTITY_BELOW_SOLD", message: "Abaixo do vendido." },
      { code: "EVENT_ALREADY_STARTED", message: "Já começou." },
    ])).toEqual({ name: "Nome repetido.", quantity: "Abaixo do vendido.", general: "Já começou." });
  });
});

describe("lotes e publicação na API", () => {
  it("cria, edita e publica nas rotas do evento", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, apiBatch()));
    vi.stubGlobal("fetch", fetchMock);

    await createTicketBatch("mare-cultural", "0b6e", toBatchRequest(VALID_FORM));
    await updateTicketBatch("mare-cultural", "0b6e", "b1", withStatus(apiBatch(), "INACTIVE"));
    await publishProducerEvent("mare-cultural", "0b6e");

    expect(sentRequest(fetchMock, 0).url).toMatch(/\/producers\/mare-cultural\/events\/0b6e\/batches$/);
    expect(sentRequest(fetchMock, 0).init.method).toBe("POST");
    expect(sentRequest(fetchMock, 1).url).toMatch(/\/batches\/b1$/);
    expect(sentRequest(fetchMock, 1).init.method).toBe("PUT");
    expect(sentRequest(fetchMock, 2).url).toMatch(/\/events\/0b6e\/publication$/);
    expect(sentRequest(fetchMock, 2).body).toBeUndefined();
  });
});

