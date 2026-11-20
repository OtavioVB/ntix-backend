import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { clearCustomerTokens, storeCustomerTokens } from "../../api/customerAuth";
import type { PublicEvent } from "../../api/events";
import { CustomerProvider, type CartState, type CustomerState } from "../../customer/session";
import { ProducerSessionProvider } from "../../producer/session";
import HubRoutes from "./HubRoutes";

const ACCOUNT = { name: "Marina Souza", email: "marina@exemplo.com", document: "***.982.247-**" };

const PUBLIC_EVENT: PublicEvent = {
  slug: "festival-horizonte", title: "Festival Horizonte", description: "Três palcos.", category: "FESTIVALS",
  startsAt: "2030-03-20T21:00:00.000Z", endsAt: "2030-03-21T06:00:00.000Z", doorsOpenAt: "2030-03-20T20:00:00.000Z",
  venueName: "Parque das Dunas", address: "Av. Beira-Mar, 1200", city: "Recife", state: "PE", ageRating: "AGE_16",
  producer: { slug: "mare-cultural", name: "Maré Cultural" }, saleState: "ON_SALE", maxTicketsPerOrder: 6,
  batches: [{ id: "b1", name: "Pista", priceCents: 12000, salesStartsAt: "2029-01-01T12:00:00.000Z", salesEndsAt: "2030-03-01T12:00:00.000Z", saleState: "ON_SALE", maxSelectable: 6 }],
};

const CART: CartState = { eventId: "festival-horizonte", slug: "festival-horizonte", selection: { b1: 2 }, expiresAt: Date.now() + 10 * 60_000 };

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function createdOrder() {
  const now = Date.now();

  return {
    code: "NX7K2QH9MP", status: "PENDING_PAYMENT", salesChannel: "HUB", createdAt: new Date(now).toISOString(), expiresAt: new Date(now + 15 * 60_000).toISOString(),
    totalCents: 24000, event: { slug: "festival-horizonte", title: "Festival Horizonte", startsAt: PUBLIC_EVENT.startsAt, venueName: "Parque das Dunas", city: "Recife", state: "PE" },
    items: [{ batchId: "b1", batchName: "Pista", quantity: 2, unitPriceCents: 12000, totalCents: 24000 }], tickets: [],
  };
}

/** A vitrine real e o pedido, respondidos pela rota. `orderAnswer` decide a resposta do POST /orders. */
function stubApi(orderAnswer: () => Response | Promise<Response>) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith("/api/v1/orders") && init?.method === "POST") return orderAnswer();
    if (url.includes("/api/v1/events?")) return jsonResponse(200, { items: [{ slug: "festival-horizonte" }], page: 1, pageSize: 50, totalItems: 1 });
    if (url.endsWith("/api/v1/events/festival-horizonte")) return jsonResponse(200, PUBLIC_EVENT);

    return jsonResponse(404, undefined);
  });

  vi.stubGlobal("fetch", fetchMock);

  return fetchMock;
}

const orderCalls = (fetchMock: ReturnType<typeof stubApi>) => fetchMock.mock.calls.filter(([url]) => String(url).endsWith("/api/v1/orders"));

function renderCheckout(customer: Partial<CustomerState> = {}) {
  return render(
    <MemoryRouter initialEntries={["/eventos/festival-horizonte/checkout"]}>
      <ProducerSessionProvider>
        <CustomerProvider initial={{ account: ACCOUNT, loggedIn: true, cart: CART, ...customer }}>
          <Routes>
            <Route path="/*" element={<HubRoutes />} />
          </Routes>
        </CustomerProvider>
      </ProducerSessionProvider>
    </MemoryRouter>,
  );
}

beforeAll(() => {
  window.scrollTo = vi.fn();
  Element.prototype.scrollIntoView = vi.fn();
});

beforeEach(() => {
  window.sessionStorage.setItem("ntix:api-mode", "real");
  window.localStorage.clear();
  storeCustomerTokens({ accessToken: "access-1", refreshToken: "refresh-1" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearCustomerTokens();
  window.sessionStorage.clear();
});

describe("checkout com a API real", () => {
  it("pede o pedido uma vez e mostra o prazo e o total do servidor", async () => {
    const fetchMock = stubApi(() => jsonResponse(201, createdOrder()));

    renderCheckout();

    expect(await screen.findByRole("button", { name: /Gerar Pix de/ })).toHaveTextContent("240,00");
    expect(screen.getByRole("timer").textContent).toMatch(/^(15:00|14:5\d)$/);
    expect(orderCalls(fetchMock)).toHaveLength(1);

    const [, init] = orderCalls(fetchMock)[0] as [string, RequestInit];

    expect(JSON.parse(init.body as string)).toEqual({ eventSlug: "festival-horizonte", salesChannel: "HUB", items: [{ batchId: "b1", quantity: 2 }] });
  });

  it("usa o código do pedido do servidor ao gerar o Pix", async () => {
    stubApi(() => jsonResponse(201, createdOrder()));
    const user = userEvent.setup();

    renderCheckout();
    await user.click(await screen.findByRole("button", { name: /Gerar Pix de/ }));

    expect((await screen.findAllByText(/NX7K2QH9MP/)).length).toBeGreaterThan(0);
  });

  it("volta ao evento com a mensagem da API quando o estoque não basta", async () => {
    stubApi(() => jsonResponse(409, [{ code: "BATCH_INSUFFICIENT_AVAILABILITY", message: "O lote \"Pista\" tem apenas 1 ingresso(s) disponível(is)." }]));

    renderCheckout();

    expect(await screen.findByRole("alert")).toHaveTextContent("O lote \"Pista\" tem apenas 1 ingresso(s) disponível(is). Escolha de novo para continuar.");
    expect(screen.getByRole("heading", { level: 1, name: "Festival Horizonte" })).toBeInTheDocument();
  });

  it("fica no checkout com o aviso quando a sessão expirou, sem abrir o pagamento", async () => {
    clearCustomerTokens();
    stubApi(() => jsonResponse(401, undefined));

    renderCheckout();

    const alert = await screen.findByRole("alert");

    expect(alert).toHaveTextContent("Sua sessão expirou");
    expect(within(alert).getByRole("link", { name: "Voltar ao evento" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Gerar Pix de/ })).not.toBeInTheDocument();
  });

  it("reaproveita o pedido já criado do carrinho em vez de criar outro", async () => {
    const fetchMock = stubApi(() => jsonResponse(201, createdOrder()));

    renderCheckout({ cart: { ...CART, order: { code: "NX7K2QH9MP", expiresAt: Date.now() + 5 * 60_000, totalCents: 24000 } } });

    expect(await screen.findByRole("button", { name: /Gerar Pix de/ })).toHaveTextContent("240,00");
    await waitFor(() => expect(screen.getByRole("timer").textContent).toMatch(/^0[45]:/));
    expect(orderCalls(fetchMock)).toHaveLength(0);
  });

  it("cria outro pedido quando a reserva guardada no carrinho já venceu", async () => {
    const fetchMock = stubApi(() => jsonResponse(201, createdOrder()));

    renderCheckout({ cart: { ...CART, order: { code: "NX0000000A", expiresAt: Date.now() - 1000, totalCents: 24000 } } });

    expect(await screen.findByRole("button", { name: /Gerar Pix de/ })).toBeInTheDocument();
    expect(orderCalls(fetchMock)).toHaveLength(1);
  });

  it("não pede pedido enquanto a pessoa não estiver conectada", async () => {
    const fetchMock = stubApi(() => jsonResponse(201, createdOrder()));

    renderCheckout({ account: null, loggedIn: false });

    expect(await screen.findByRole("heading", { name: "Falta pouco." })).toBeInTheDocument();
    expect(orderCalls(fetchMock)).toHaveLength(0);
  });
});

describe("checkout com a API real, enquanto reserva", () => {
  it("avisa que está reservando e não abre o pagamento antes da resposta", async () => {
    stubApi(() => new Promise<Response>(() => undefined));

    renderCheckout();

    expect(await screen.findByText("Reservando seus ingressos…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Gerar Pix de/ })).not.toBeInTheDocument();
  });
});
