import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { clearSessionTokens, storeSessionTokens } from "../../api/auth";
import { createProducerEvent, getProducerEvents, updateProducerEvent, type ProducerEvent } from "../../api/events";
import { ProducerSessionProvider, type ProducerSession } from "../../producer/session";
import PanelRoutes from "./PanelRoutes";
import { EMPTY_EVENT_FORM, isoToLocal, localToIso, mapEventNotifications, toEventRequest, validateEvent, type EventForm } from "./events";

const ADMIN = { id: "admin", name: "Pessoa Administradora", email: "admin@exemplo.test", isAdmin: true, invited: false };
const LOGGED_IN: Partial<ProducerSession> = { name: "Maré Cultural", slug: "mare-cultural", loggedIn: true, adminEmail: ADMIN.email, team: [ADMIN] };

const NOW = new Date("2026-11-20T12:00:00.000Z").getTime();

const VALID_FORM: EventForm = {
  title: "Festival   Horizonte",
  description: "",
  category: "FESTIVALS",
  startsAt: "2027-03-20T18:00",
  endsAt: "2027-03-21T03:00",
  doorsOpenAt: "",
  venueName: "Parque das Dunas",
  address: "Av. Beira-Mar, 1200",
  city: "Recife",
  state: "PE",
  ageRating: "AGE_16",
  imageUrl: "",
};

function apiEvent(overrides: Partial<ProducerEvent> = {}): ProducerEvent {
  return {
    id: "0b6e",
    slug: "festival-horizonte",
    title: "Festival Horizonte",
    category: "FESTIVALS",
    startsAt: "2027-03-20T21:00:00.000Z",
    endsAt: "2027-03-21T06:00:00.000Z",
    venueName: "Parque das Dunas",
    address: "Av. Beira-Mar, 1200",
    city: "Recife",
    state: "PE",
    ageRating: "AGE_16",
    status: "DRAFT",
    createdAt: "2026-11-20T12:00:00.000Z",
    updatedAt: "2026-11-20T12:00:00.000Z",
    salesSummary: { capacity: 0, soldQuantity: 0, grossCents: 0, paidOrders: 0 },
    ...overrides,
  };
}

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function sentRequest(fetchMock: ReturnType<typeof vi.fn>, call = 0) {
  const [url, init] = fetchMock.mock.calls[call] as [string, RequestInit];
  return { url, init, headers: init.headers as Record<string, string>, body: init.body ? JSON.parse(init.body as string) : undefined };
}

function renderEvents() {
  return render(
    <MemoryRouter initialEntries={["/painel/eventos"]}>
      <ProducerSessionProvider initial={LOGGED_IN}>
        <Routes>
          <Route path="/painel/*" element={<PanelRoutes />} />
          <Route path="/entrar" element={<h1>Entrar</h1>} />
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

describe("regras do formulário de evento", () => {
  it("aceita o evento válido e normaliza o corpo como o EventV1Request", () => {
    expect(validateEvent(VALID_FORM, NOW)).toEqual({});

    const request = toEventRequest(VALID_FORM);
    expect(request.title).toBe("Festival Horizonte");
    expect(request.startsAt).toBe(localToIso("2027-03-20T18:00"));
    expect(request).not.toHaveProperty("description");
    expect(request).not.toHaveProperty("doorsOpenAt");
    expect(request).not.toHaveProperty("imageUrl");
  });

  it("valida como a API: título, período, portões, local e imagem", () => {
    const errors = validateEvent({
      ...EMPTY_EVENT_FORM,
      title: "AB",
      startsAt: "2026-11-19T10:00",
      endsAt: "2026-11-19T09:00",
      doorsOpenAt: "2026-11-19T11:00",
      state: "XX",
      imageUrl: "http://exemplo.com/a.jpg",
    }, NOW);

    expect(Object.keys(errors).sort()).toEqual(["address", "ageRating", "category", "city", "doorsOpenAt", "endsAt", "imageUrl", "startsAt", "state", "title", "venueName"].sort());
    expect(errors.startsAt).toBe("O evento deve começar no futuro.");
  });

  it("recusa duração acima de 30 dias e portões abertos mais de 12 horas antes", () => {
    const errors = validateEvent({ ...VALID_FORM, endsAt: "2027-04-25T18:00", doorsOpenAt: "2027-03-20T05:00" }, NOW);

    expect(errors.endsAt).toBeDefined();
    expect(errors.doorsOpenAt).toBeDefined();
  });

  it("converte entre datetime-local e ISO sem perder o minuto", () => {
    expect(isoToLocal(localToIso("2027-03-20T18:30"))).toBe("2027-03-20T18:30");
    expect(isoToLocal(undefined)).toBe("");
  });

  it("leva os códigos da API para os campos e o resto para a mensagem geral", () => {
    const errors = mapEventNotifications([
      { code: "EVENT_PERIOD_INVALID", message: "Período inválido." },
      { code: "EVENT_VENUE_CITY_INVALID", message: "Cidade inválida." },
      { code: "EVENT_ALREADY_STARTED", message: "O evento já começou." },
    ]);

    expect(errors).toEqual({ startsAt: "Período inválido.", city: "Cidade inválida.", general: "O evento já começou." });
  });
});

describe("eventos na API", () => {
  it("lista com o filtro de situação e o access token", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, [apiEvent()]));
    vi.stubGlobal("fetch", fetchMock);

    const result = await getProducerEvents("mare-cultural", "DRAFT");

    const { url, init, headers } = sentRequest(fetchMock);
    expect(url).toMatch(/\/api\/v1\/producers\/mare-cultural\/events\?status=DRAFT$/);
    expect(init.method).toBe("GET");
    expect(headers.Authorization).toBe("Bearer token-123");
    expect(headers).not.toHaveProperty("X-Execution-User");
    expect(result.ok && result.data[0].slug).toBe("festival-horizonte");
  });

  it("cria com POST e edita com PUT no id do evento", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(201, apiEvent()));
    vi.stubGlobal("fetch", fetchMock);

    await createProducerEvent("mare-cultural", toEventRequest(VALID_FORM));
    await updateProducerEvent("mare-cultural", "0b6e", toEventRequest(VALID_FORM));

    expect(sentRequest(fetchMock, 0).init.method).toBe("POST");
    expect(sentRequest(fetchMock, 0).url).toMatch(/\/producers\/mare-cultural\/events$/);
    expect(sentRequest(fetchMock, 0).body.category).toBe("FESTIVALS");
    expect(sentRequest(fetchMock, 1).init.method).toBe("PUT");
    expect(sentRequest(fetchMock, 1).url).toMatch(/\/events\/0b6e$/);
  });
});

describe("aba Eventos", () => {
  it("em demonstração continua com o evento de exemplo, sem chamar a API", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    renderEvents();

    expect(screen.getByRole("heading", { name: "Eventos e lotes" })).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

});
