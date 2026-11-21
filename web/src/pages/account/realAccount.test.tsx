import { render, screen } from "@testing-library/react";
import type { ReactElement } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { clearCustomerTokens, storeCustomerTokens } from "../../api/customerAuth";
import { listMyOrders, listMyTickets, getMyTicket } from "../../api/customerArea";
import { RealOrderDetailPage, RealOrdersPage, RealTicketDetailPage, RealTicketsPage } from "./RealAccountPages";

const ORDER = {
  code: "NX7K2QH9MP",
  status: "PAID",
  createdAt: "2030-01-15T12:00:00.000Z",
  paidAt: "2030-01-15T12:04:00.000Z",
  totalCents: 24000,
  ticketCount: 2,
  event: { slug: "festival-horizonte", title: "Festival Horizonte", startsAt: "2030-03-20T21:00:00.000Z", city: "Recife", state: "PE" },
  producer: { slug: "mare-cultural", name: "Maré Cultural" },
};

const TICKET = {
  code: "K7M2QX9HP4TA",
  status: "VALID",
  batchName: "1º lote - Pista",
  holderName: "Marina Souza",
  orderCode: "NX7K2QH9MP",
  event: {
    slug: "festival-horizonte",
    title: "Festival Horizonte",
    startsAt: "2030-03-20T21:00:00.000Z",
    venueName: "Parque das Dunas",
    address: "Av. Beira-Mar, 1200",
    city: "Recife",
    state: "PE",
  },
};

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function sentUrl(fetchMock: ReturnType<typeof vi.fn>, call = 0): string {
  return (fetchMock.mock.calls[call] as [string, RequestInit])[0];
}

function sentHeaders(fetchMock: ReturnType<typeof vi.fn>, call = 0): Record<string, string> {
  return (fetchMock.mock.calls[call] as [string, RequestInit])[1].headers as Record<string, string>;
}

function at(path: string, element: ReactElement, route: string) {
  return (
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path={route} element={element} />
      </Routes>
    </MemoryRouter>
  );
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
  window.sessionStorage.setItem("ntix:api-mode", "real");
  storeCustomerTokens({ accessToken: "access-1", refreshToken: "refresh-1" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearCustomerTokens();
  window.sessionStorage.clear();
});

describe("API da área do comprador", () => {
  it("lista pedidos com o token do comprador e o filtro e a paginação na query", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { items: [], page: 2, pageSize: 10, totalItems: 0 }));
    vi.stubGlobal("fetch", fetchMock);

    await listMyOrders({ status: "PAID", page: 2, pageSize: 10 });

    expect(sentUrl(fetchMock)).toMatch(/\/api\/v1\/customers\/me\/orders\?status=PAID&page=2&pageSize=10$/);
    expect(sentHeaders(fetchMock).Authorization).toBe("Bearer access-1");
    expect(sentHeaders(fetchMock)["X-Origin-Service"]).toBe("ntix-web");
  });

  it("pede ingressos passados pela query when=PAST", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { items: [], page: 1, pageSize: 20, totalItems: 0 }));
    vi.stubGlobal("fetch", fetchMock);

    await listMyTickets("PAST");

    expect(sentUrl(fetchMock)).toMatch(/\/api\/v1\/customers\/me\/tickets\?when=PAST$/);
  });

  it("codifica o código do ingresso na rota de detalhe", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(404, [{ code: "TICKET_NOT_FOUND", message: "O ingresso informado não existe." }]));
    vi.stubGlobal("fetch", fetchMock);

    const result = await getMyTicket("K7M2 QX");

    expect(sentUrl(fetchMock)).toMatch(/\/me\/tickets\/K7M2%20QX$/);
    expect(result).toEqual({ ok: false, status: 404, notifications: [{ code: "TICKET_NOT_FOUND", message: "O ingresso informado não existe." }] });
  });
});

describe("telas da conta com a API real", () => {
  it("mostra os pedidos do servidor com o status e o total dele", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(200, { items: [ORDER], page: 1, pageSize: 20, totalItems: 1 })));

    render(<MemoryRouter><RealOrdersPage /></MemoryRouter>);

    expect(await screen.findByText("Festival Horizonte")).toBeInTheDocument();
    expect(screen.getByText("Pago")).toBeInTheDocument();
    expect(screen.getByText(/2 ingresso/)).toBeInTheDocument();
  });

  it("mostra a recusa da API na lista de pedidos", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(400, [{ code: "ORDER_FILTER_INVALID", message: "A página deve ser a partir de 1." }])));

    render(<MemoryRouter><RealOrdersPage /></MemoryRouter>);

    expect(await screen.findByRole("alert")).toHaveTextContent("A página deve ser a partir de 1.");
  });

  it("mostra os ingressos próximos do servidor", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(200, { items: [TICKET], page: 1, pageSize: 20, totalItems: 1 })));

    render(<MemoryRouter><RealTicketsPage /></MemoryRouter>);

    expect(await screen.findByText("Festival Horizonte")).toBeInTheDocument();
    expect(screen.getByText("Válido")).toBeInTheDocument();
  });

  it("abre o detalhe do ingresso com o código e o titular da API", async () => {
    const detail = { ...TICKET, ageRating: "16", issuedAt: "2030-01-15T12:04:00.000Z" };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(200, detail)));

    render(at("/conta/ingressos/K7M2QX9HP4TA", <RealTicketDetailPage />, "/conta/ingressos/:code"));

    expect(await screen.findByText("Marina Souza")).toBeInTheDocument();
    expect(screen.getByText("K7M2QX9HP4TA")).toBeInTheDocument();
    expect(screen.getByText("16 anos")).toBeInTheDocument();
  });

  it("mostra o pedido com os ingressos que a API devolveu", async () => {
    const detail = {
      code: "NX7K2QH9MP",
      status: "PAID",
      salesChannel: "HUB",
      createdAt: ORDER.createdAt,
      expiresAt: ORDER.createdAt,
      totalCents: 24000,
      event: { slug: "festival-horizonte", title: "Festival Horizonte", startsAt: "2030-03-20T21:00:00.000Z", venueName: "Parque das Dunas", city: "Recife", state: "PE" },
      items: [{ batchId: "b1", batchName: "Pista", quantity: 2, unitPriceCents: 12000, totalCents: 24000 }],
      tickets: [{ code: "K7M2QX9HP4TA", batchName: "Pista", holderName: "Marina Souza", status: "VALID" }],
    };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(200, detail)));

    render(at("/conta/pedidos/NX7K2QH9MP", <RealOrderDetailPage />, "/conta/pedidos/:code"));

    expect(await screen.findByRole("heading", { name: "Festival Horizonte" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /K7M2QX9HP4TA/ })).toHaveAttribute("href", "/conta/ingressos/K7M2QX9HP4TA");
  });
});
