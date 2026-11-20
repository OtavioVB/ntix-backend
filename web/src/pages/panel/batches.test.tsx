import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { clearSessionTokens, storeSessionTokens } from "../../api/auth";
import { createTicketBatch, publishProducerEvent, updateTicketBatch, type ProducerEvent, type TicketBatch } from "../../api/events";
import { ProducerSessionProvider, type ProducerSession } from "../../producer/session";
import PanelRoutes from "./PanelRoutes";
import { EMPTY_BATCH_FORM, mapBatchNotifications, toBatchRequest, validateBatch, withStatus, type BatchForm } from "./batches";

const ADMIN = { id: "admin", name: "Pessoa Administradora", email: "admin@exemplo.test", isAdmin: true, invited: false };
const LOGGED_IN: Partial<ProducerSession> = { name: "Maré Cultural", slug: "mare-cultural", loggedIn: true, adminEmail: ADMIN.email, team: [ADMIN] };

const EVENT_ENDS_AT = "2030-03-21T06:00:00.000Z";

const VALID_FORM: BatchForm = { name: "1º  lote - Pista", note: "", price: "120,50", quantity: "500", salesStartsAt: "2030-01-01T10:00", salesEndsAt: "2030-02-01T10:00" };

function apiEvent(overrides: Partial<ProducerEvent> = {}): ProducerEvent {
  return {
    id: "0b6e", slug: "festival-horizonte", title: "Festival Horizonte", category: "FESTIVALS",
    startsAt: "2030-03-20T21:00:00.000Z", endsAt: EVENT_ENDS_AT, venueName: "Parque das Dunas", address: "Av. Beira-Mar, 1200",
    city: "Recife", state: "PE", ageRating: "AGE_16", status: "DRAFT", createdAt: "2026-11-20T12:00:00.000Z", updatedAt: "2026-11-20T12:00:00.000Z",
    ...overrides,
  };
}

function apiBatch(overrides: Partial<TicketBatch> = {}): TicketBatch {
  return {
    id: "b1", name: "VIP", priceCents: 30000, quantity: 50, soldQuantity: 0, reservedQuantity: 0, availableQuantity: 50,
    salesStartsAt: "2030-01-01T13:00:00.000Z", salesEndsAt: "2030-02-01T13:00:00.000Z", status: "ACTIVE", saleState: "ON_SALE",
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

function renderEvents() {
  return render(
    <MemoryRouter initialEntries={["/painel/eventos"]}>
      <ProducerSessionProvider initial={LOGGED_IN}>
        <Routes>
          <Route path="/painel/*" element={<PanelRoutes />} />
        </Routes>
      </ProducerSessionProvider>
    </MemoryRouter>,
  );
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

describe("aba Eventos com lotes", () => {
  it("mostra os lotes do evento e publica", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(200, [apiEvent()]))
      .mockResolvedValueOnce(jsonResponse(200, [apiBatch()]))
      .mockResolvedValueOnce(jsonResponse(200, apiEvent({ status: "PUBLISHED" })))
      .mockResolvedValue(jsonResponse(200, [apiEvent({ status: "PUBLISHED" })]));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    renderEvents();

    await user.click(await screen.findByRole("button", { name: "Lotes de Festival Horizonte" }));
    expect(await screen.findByText("VIP")).toBeInTheDocument();
    expect(screen.getByText("Em venda")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Publicar evento" }));

    expect(sentRequest(fetchMock, 2).url).toMatch(/\/publication$/);
    expect(await screen.findAllByText("Publicado")).not.toHaveLength(0);
    expect(screen.queryByRole("button", { name: "Publicar evento" })).not.toBeInTheDocument();
  });

  it("mostra a recusa da publicação", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(200, [apiEvent()]))
      .mockResolvedValueOnce(jsonResponse(200, []))
      .mockResolvedValueOnce(jsonResponse(400, [{ code: "EVENT_WITHOUT_PAYMENT_METHOD", message: "Configure uma integração de pagamento para Pix na aba Integrações antes de publicar o evento." }]));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    renderEvents();

    await user.click(await screen.findByRole("button", { name: "Lotes de Festival Horizonte" }));
    await user.click(await screen.findByRole("button", { name: "Publicar evento" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("aba Integrações");
  });
});
