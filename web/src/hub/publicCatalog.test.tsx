import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { getPublicEvent, searchPublicEvents, type PublicBatch, type PublicEvent } from "../api/events";
import { CustomerProvider } from "../customer/session";
import HubRoutes from "../pages/hub/HubRoutes";
import { ProducerSessionProvider } from "../producer/session";
import { batchState, maxSelectable } from "./rules";
import { loadPublicCatalog, toHubBatch, toHubEvent } from "./publicCatalog";

const NOW = new Date("2030-01-15T12:00:00.000Z").getTime();

function batch(overrides: Partial<PublicBatch> = {}): PublicBatch {
  return {
    id: "b1", name: "Pista", priceCents: 12000, salesStartsAt: "2030-01-01T12:00:00.000Z", salesEndsAt: "2030-03-01T12:00:00.000Z",
    saleState: "ON_SALE", maxSelectable: 6, ...overrides,
  };
}

function publicEvent(overrides: Partial<PublicEvent> = {}): PublicEvent {
  return {
    slug: "festival-horizonte", title: "Festival Horizonte", description: "Três palcos.\n\nUm pôr do sol.", category: "FESTIVALS",
    startsAt: "2030-03-20T21:00:00.000Z", endsAt: "2030-03-21T06:00:00.000Z", doorsOpenAt: "2030-03-20T20:00:00.000Z",
    venueName: "Parque das Dunas", address: "Av. Beira-Mar, 1200", city: "Recife", state: "PE", ageRating: "AGE_16",
    producer: { slug: "mare-cultural", name: "Maré Cultural" }, saleState: "ON_SALE", maxTicketsPerOrder: 6, batches: [batch()],
    ...overrides,
  };
}

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

beforeAll(() => {
  window.scrollTo = vi.fn();
});

beforeEach(() => window.sessionStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe("vitrine pública no formato do hub", () => {
  it("leva o evento da API para o hub com categoria, classificação e parágrafos", () => {
    const hubEvent = toHubEvent(publicEvent());

    expect(hubEvent.slug).toBe("festival-horizonte");
    expect(hubEvent.category).toBe("Festivais");
    expect(hubEvent.ageRating).toBe("16 anos");
    expect(hubEvent.producer).toBe("Maré Cultural");
    expect(hubEvent.description).toEqual(["Três palcos.", "Um pôr do sol."]);
    expect(hubEvent.art.style).toBeDefined();
  });

  it("mantém nas regras do hub o mesmo estado e o mesmo limite que a API", () => {
    const onSale = toHubBatch(batch({ maxSelectable: 2 }), 6);
    const soldOut = toHubBatch(batch({ saleState: "SOLD_OUT", maxSelectable: 0 }), 6);
    const upcoming = toHubBatch(batch({ saleState: "UPCOMING", maxSelectable: 0, salesStartsAt: "2030-02-01T12:00:00.000Z" }), 6);

    expect(batchState(onSale, NOW)).toBe("ON_SALE");
    expect(maxSelectable(onSale, 0)).toBe(2);
    expect(batchState(soldOut, NOW)).toBe("SOLD_OUT");
    expect(batchState(upcoming, NOW)).toBe("UPCOMING");
  });

  it("consulta a vitrine sem token e com os filtros na query", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { items: [], page: 1, pageSize: 50, totalItems: 0 }));
    vi.stubGlobal("fetch", fetchMock);

    await searchPublicEvents({ producer: "mare-cultural", state: "PE", pageSize: 50 });
    await getPublicEvent("festival-horizonte");

    const [searchUrl, searchInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(searchUrl).toMatch(/\/api\/v1\/events\?producer=mare-cultural&state=PE&pageSize=50$/);
    expect(searchInit.headers as Record<string, string>).not.toHaveProperty("Authorization");
    expect((fetchMock.mock.calls[1] as [string])[0]).toMatch(/\/api\/v1\/events\/festival-horizonte$/);
  });

  it("carrega a lista e a página de cada evento, deixando de fora a que falhar", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(200, { items: [{ slug: "a" }, { slug: "b" }], page: 1, pageSize: 50, totalItems: 2 }))
      .mockResolvedValueOnce(jsonResponse(200, publicEvent({ slug: "a" })))
      .mockResolvedValueOnce(jsonResponse(404, [{ code: "EVENT_NOT_FOUND", message: "O evento informado não existe." }]));
    vi.stubGlobal("fetch", fetchMock);

    const events = await loadPublicCatalog(undefined);

    expect(events?.map((event) => event.slug)).toEqual(["a"]);
  });

  it("devolve nulo quando a lista falha", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(500, undefined)));

    expect(await loadPublicCatalog(undefined)).toBeNull();
  });
});

describe("hub com a API real", () => {
  it("abre a página do evento publicado com os lotes da API", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    vi.stubGlobal("fetch", vi.fn()
      .mockResolvedValueOnce(jsonResponse(200, { items: [{ slug: "festival-horizonte" }], page: 1, pageSize: 50, totalItems: 1 }))
      .mockResolvedValueOnce(jsonResponse(200, publicEvent())));

    render(
      <MemoryRouter initialEntries={["/eventos/festival-horizonte"]}>
        <ProducerSessionProvider>
          <CustomerProvider>
            <HubRoutes />
          </CustomerProvider>
        </ProducerSessionProvider>
      </MemoryRouter>,
    );

    expect(screen.getByRole("status")).toHaveTextContent("Carregando o evento");
    expect(await screen.findByRole("heading", { name: "Festival Horizonte" })).toBeInTheDocument();
    expect(screen.getByText("Pista")).toBeInTheDocument();
  });

  it("avisa quando a vitrine não carrega", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(500, undefined)));

    render(
      <MemoryRouter initialEntries={["/"]}>
        <ProducerSessionProvider>
          <CustomerProvider>
            <HubRoutes />
          </CustomerProvider>
        </ProducerSessionProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível carregar os eventos");
  });
});
