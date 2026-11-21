import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { clearSessionTokens, storeSessionTokens } from "../../api/auth";
import { getEventDailySales, type DailySales, type ProducerEvent, type TicketBatch } from "../../api/events";
import { ProducerSessionProvider, type ProducerSession } from "../../producer/session";
import { hubEventHref } from "../../producer/paths";
import PanelRoutes from "./PanelRoutes";

const ADMIN = { id: "admin", name: "Pessoa Administradora", email: "admin@exemplo.test", isAdmin: true, invited: false };
const LOGGED_IN: Partial<ProducerSession> = { name: "Maré Cultural", slug: "mare-cultural", loggedIn: true, adminEmail: ADMIN.email, team: [ADMIN] };

function apiEvent(overrides: Partial<ProducerEvent> = {}): ProducerEvent {
  return {
    id: "0b6e", slug: "festival-horizonte", title: "Festival Horizonte", category: "FESTIVALS",
    startsAt: "2030-03-20T21:00:00.000Z", endsAt: "2030-03-21T06:00:00.000Z", venueName: "Parque das Dunas", address: "Av. Beira-Mar, 1200",
    city: "Recife", state: "PE", ageRating: "AGE_16", status: "PUBLISHED", createdAt: "2026-11-20T12:00:00.000Z", updatedAt: "2026-11-20T12:00:00.000Z",
    salesSummary: { capacity: 1200, soldQuantity: 431, grossCents: 5172000, paidOrders: 298 },
    ...overrides,
  };
}

function apiBatch(overrides: Partial<TicketBatch> = {}): TicketBatch {
  return {
    id: "b1", name: "1º lote", priceCents: 12000, quantity: 500, soldQuantity: 300, reservedQuantity: 4, availableQuantity: 196,
    salesStartsAt: "2026-01-01T13:00:00.000Z", salesEndsAt: "2030-02-01T13:00:00.000Z", status: "ACTIVE", saleState: "ON_SALE",
    grossCents: 3600000, paidOrders: 210,
    ...overrides,
  };
}

function days(count: number, sold: (index: number) => number = () => 0): DailySales[] {
  return Array.from({ length: count }, (_, index) => ({ date: `2026-11-${String(index + 1).padStart(2, "0")}`, soldQuantity: sold(index), grossCents: sold(index) * 12000, paidOrders: sold(index) }));
}

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

type Handler = { method: string; path: RegExp; reply: () => Response };

/** `fetch` simulado por rota: a página carrega evento, lotes e vendas em paralelo, sem ordem garantida. */
function routeFetch(handlers: Handler[]) {
  const fetchMock = vi.fn(async (url: string, init: RequestInit) => {
    const { pathname, search } = new URL(url);
    const handler = handlers.find((item) => item.method === (init.method ?? "GET") && item.path.test(pathname + search));
    if (!handler) throw new Error(`Rota não simulada: ${init.method} ${pathname}${search}`);
    return handler.reply();
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function calls(fetchMock: ReturnType<typeof routeFetch>, method: string, path: RegExp) {
  return fetchMock.mock.calls
    .map(([url, init]) => ({ url: new URL(url as string), init: init as RequestInit }))
    .filter(({ url, init }) => (init.method ?? "GET") === method && path.test(url.pathname + url.search));
}

const EVENT_PATH = /\/events\/0b6e$/;
const BATCHES_PATH = /\/events\/0b6e\/batches$/;
const DAILY_PATH = /\/events\/0b6e\/sales\/daily\?/;

function detailHandlers(event: ProducerEvent, batches: TicketBatch[] = [apiBatch()]): Handler[] {
  return [
    { method: "GET", path: EVENT_PATH, reply: () => jsonResponse(200, event) },
    { method: "GET", path: BATCHES_PATH, reply: () => jsonResponse(200, batches) },
    { method: "GET", path: DAILY_PATH, reply: () => jsonResponse(200, { from: "2026-11-01", to: "2026-11-30", days: days(30, (index) => (index === 2 ? 12 : 0)) }) },
  ];
}

function Where() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ProducerSessionProvider initial={LOGGED_IN}>
        <Routes>
          <Route path="/painel/*" element={<><PanelRoutes /><Where /></>} />
        </Routes>
      </ProducerSessionProvider>
    </MemoryRouter>,
  );
}

const location = () => screen.getByTestId("location").textContent;

beforeAll(() => {
  window.scrollTo = vi.fn();
});

beforeEach(() => {
  window.sessionStorage.clear();
  window.sessionStorage.setItem("ntix:api-mode", "real");
  storeSessionTokens({ accessToken: "token-123", refreshToken: "refresh-123" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearSessionTokens();
});

describe("vendas por dia na API", () => {
  it("pede o período em datas e usa o access token", async () => {
    const fetchMock = routeFetch([{ method: "GET", path: DAILY_PATH, reply: () => jsonResponse(200, { from: "2026-11-01", to: "2026-11-03", days: days(3) }) }]);

    const result = await getEventDailySales("mare-cultural", "0b6e", { from: "2026-11-01", to: "2026-11-03" });

    const [call] = calls(fetchMock, "GET", DAILY_PATH);
    expect(call.url.search).toBe("?from=2026-11-01&to=2026-11-03");
    expect((call.init.headers as Record<string, string>).Authorization).toBe("Bearer token-123");
    expect(result.ok && result.data.days).toHaveLength(3);
  });
});

describe("lista de eventos (/eventos)", () => {
  it("mostra um card por evento com os vendidos, a capacidade e as vendas brutas", async () => {
    routeFetch([{ method: "GET", path: /\/producers\/mare-cultural\/events$/, reply: () => jsonResponse(200, [apiEvent()]) }]);

    renderAt("/painel/eventos");

    const card = await screen.findByRole("link", { name: /Festival Horizonte/ });
    expect(within(card).getByText("Publicado")).toBeInTheDocument();
    expect(within(card).getByRole("img", { name: "431 de 1200 ingressos vendidos" })).toBeInTheDocument();
    expect(within(card).getByText("431 de 1200")).toBeInTheDocument();
    expect(within(card).getByText(/51\.720,00/)).toBeInTheDocument();
    expect(card).toHaveAttribute("href", "/painel/eventos/0b6e");
  });

  it("ordena publicados, rascunhos e encerrados, e o evento sem lote diz isso", async () => {
    routeFetch([{
      method: "GET",
      path: /\/events$/,
      reply: () => jsonResponse(200, [
        apiEvent({ id: "e1", title: "Antigo", status: "ENDED", startsAt: "2026-01-01T21:00:00.000Z" }),
        apiEvent({ id: "d1", title: "Rascunho Novo", status: "DRAFT", salesSummary: { capacity: 0, soldQuantity: 0, grossCents: 0, paidOrders: 0 } }),
        apiEvent({ id: "p1", title: "À Venda" }),
      ]),
    }]);

    renderAt("/painel/eventos");

    await screen.findByText("Antigo");
    expect(screen.getAllByRole("heading", { level: 2 }).map((heading) => heading.textContent)).toEqual(["À Venda", "Rascunho Novo", "Antigo"]);
    expect(within(screen.getByRole("link", { name: /Rascunho Novo/ })).getByText("Sem lotes ainda")).toBeInTheDocument();
    expect(within(screen.getByRole("link", { name: /Antigo/ })).getByText("Encerrado")).toBeInTheDocument();
  });

  it("filtra pela situação na API e guarda o filtro na URL", async () => {
    const fetchMock = routeFetch([
      { method: "GET", path: /\/events\?status=ENDED$/, reply: () => jsonResponse(200, []) },
      { method: "GET", path: /\/events$/, reply: () => jsonResponse(200, [apiEvent()]) },
    ]);
    const user = userEvent.setup();

    renderAt("/painel/eventos");
    await screen.findByText("Festival Horizonte");

    await user.click(screen.getByRole("radio", { name: "Encerrados" }));

    expect(await screen.findByText("Nenhum evento nesta situação")).toBeInTheDocument();
    expect(location()).toBe("/painel/eventos?situacao=encerrados");
    expect(calls(fetchMock, "GET", /\?status=ENDED$/)).toHaveLength(1);
  });

  it("abre com o filtro da URL", async () => {
    const fetchMock = routeFetch([{ method: "GET", path: /\/events\?status=DRAFT$/, reply: () => jsonResponse(200, []) }]);

    renderAt("/painel/eventos?situacao=rascunho");

    expect(await screen.findByText("Nenhum evento nesta situação")).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Rascunho" })).toBeChecked();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("sem eventos, convida a criar o primeiro", async () => {
    routeFetch([{ method: "GET", path: /\/events$/, reply: () => jsonResponse(200, []) }]);

    renderAt("/painel/eventos");

    expect(await screen.findByRole("heading", { name: "Nenhum evento ainda" })).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Criar evento" })[0]).toHaveAttribute("href", "/painel/eventos/novo");
  });

  it("mostra a falha ao carregar", async () => {
    routeFetch([{ method: "GET", path: /\/events$/, reply: () => jsonResponse(500, undefined) }]);

    renderAt("/painel/eventos");

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });
});

describe("criação e edição em páginas próprias", () => {
  it("cria em rascunho e leva à página do evento criado", async () => {
    const fetchMock = routeFetch([
      { method: "POST", path: /\/producers\/mare-cultural\/events$/, reply: () => jsonResponse(201, apiEvent({ status: "DRAFT" })) },
      ...detailHandlers(apiEvent({ status: "DRAFT" })),
    ]);
    const user = userEvent.setup();

    renderAt("/painel/eventos/novo");

    expect(screen.getByRole("heading", { name: "Novo evento" })).toBeInTheDocument();
    await user.type(screen.getByLabelText("Título"), "Festival Horizonte");
    await user.selectOptions(screen.getByLabelText("Categoria"), "FESTIVALS");
    await user.type(screen.getByLabelText("Começa em"), "2030-03-20T18:00");
    await user.type(screen.getByLabelText("Termina em"), "2030-03-21T03:00");
    await user.type(screen.getByLabelText("Local"), "Parque das Dunas");
    await user.type(screen.getByLabelText("Endereço"), "Av. Beira-Mar, 1200");
    await user.type(screen.getByLabelText("Cidade"), "Recife");
    await user.selectOptions(screen.getByLabelText("UF"), "PE");
    await user.selectOptions(screen.getByLabelText("Classificação etária"), "AGE_16");
    await user.click(screen.getByRole("button", { name: "Criar em rascunho" }));

    await waitFor(() => expect(location()).toBe("/painel/eventos/0b6e"));
    const [created] = calls(fetchMock, "POST", /\/events$/);
    expect(JSON.parse(created.init.body as string)).toMatchObject({ title: "Festival Horizonte", state: "PE", ageRating: "AGE_16" });
  });

  it("edita com o nome do evento no título e volta à página do evento", async () => {
    const fetchMock = routeFetch([
      { method: "PUT", path: EVENT_PATH, reply: () => jsonResponse(200, apiEvent()) },
      ...detailHandlers(apiEvent({ description: "## Atrações\n\n- **Banda Maré**" })),
    ]);
    const user = userEvent.setup();

    renderAt("/painel/eventos/0b6e/editar");

    expect(await screen.findByRole("heading", { name: "Editar Festival Horizonte" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Título")).toHaveValue("Festival Horizonte"));
    await user.click(screen.getByRole("button", { name: "Salvar alterações" }));

    await waitFor(() => expect(location()).toBe("/painel/eventos/0b6e"));
    const [updated] = calls(fetchMock, "PUT", EVENT_PATH);
    expect(JSON.parse(updated.init.body as string).description).toBe("## Atrações\n\n- **Banda Maré**");
  });

  it("mostra a recusa da API no formulário", async () => {
    routeFetch([
      { method: "PUT", path: EVENT_PATH, reply: () => jsonResponse(400, [{ code: "EVENT_ALREADY_STARTED", message: "O evento já começou e não pode mais ser alterado." }]) },
      ...detailHandlers(apiEvent()),
    ]);
    const user = userEvent.setup();

    renderAt("/painel/eventos/0b6e/editar");

    await waitFor(() => expect(screen.getByLabelText("Título")).toHaveValue("Festival Horizonte"));
    await user.click(screen.getByRole("button", { name: "Salvar alterações" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("O evento já começou e não pode mais ser alterado.");
  });

  it("evento que já começou não abre a edição", async () => {
    routeFetch(detailHandlers(apiEvent({ startsAt: "2026-01-01T21:00:00.000Z", endsAt: "2026-01-02T06:00:00.000Z", status: "ENDED" })));

    renderAt("/painel/eventos/0b6e/editar");

    await waitFor(() => expect(location()).toBe("/painel/eventos/0b6e"));
    expect(screen.queryByRole("link", { name: "Editar evento" })).not.toBeInTheDocument();
  });
});

describe("página do evento (/eventos/:id)", () => {
  it("mostra o resumo, o gráfico por dia, os lotes com as vendas e a vitrine", async () => {
    routeFetch(detailHandlers(apiEvent()));

    renderAt("/painel/eventos/0b6e");

    expect(await screen.findByRole("heading", { level: 1, name: "Festival Horizonte" })).toBeInTheDocument();
    const stats = within(screen.getByText("Ingressos vendidos").closest("dl")!);
    expect(stats.getByText("Ingressos vendidos").nextSibling).toHaveTextContent("431 de 1200");
    expect(stats.getByText("Vendas brutas").nextSibling).toHaveTextContent("antes das taxas");
    expect(stats.getByText("Pedidos").nextSibling).toHaveTextContent("298");
    expect(await screen.findByRole("img", { name: /Ingressos vendidos por dia: 12 no período/ })).toBeInTheDocument();

    const batch = (await screen.findByText("1º lote")).closest("li")!;
    expect(within(batch).getByText("300 de 500")).toBeInTheDocument();
    expect(within(batch).getByText("Reservados").nextSibling).toHaveTextContent("4");
    expect(within(batch).getByText("Disponíveis").nextSibling).toHaveTextContent("196");
    expect(within(batch).getByText("Vendas brutas").nextSibling).toHaveTextContent("36.000,00");
    expect(within(batch).getByText("Pedidos").nextSibling).toHaveTextContent("210");

    expect(screen.getByRole("link", { name: "Ver na vitrine" })).toHaveAttribute("href", hubEventHref("festival-horizonte"));
    expect(screen.getByRole("link", { name: "Editar evento" })).toHaveAttribute("href", "/painel/eventos/0b6e/editar");
    expect(screen.queryByRole("button", { name: "Publicar evento" })).not.toBeInTheDocument();
  });

  it("troca o período do gráfico", async () => {
    const fetchMock = routeFetch(detailHandlers(apiEvent()));
    const user = userEvent.setup();

    renderAt("/painel/eventos/0b6e");
    await screen.findByRole("img", { name: /Ingressos vendidos por dia/ });

    await user.click(screen.getByRole("radio", { name: "7 dias" }));

    await waitFor(() => expect(calls(fetchMock, "GET", DAILY_PATH)).toHaveLength(2));
    const last = calls(fetchMock, "GET", DAILY_PATH)[1].url.searchParams;
    const span = (new Date(`${last.get("to")}T00:00:00Z`).getTime() - new Date(`${last.get("from")}T00:00:00Z`).getTime()) / 86_400_000;
    expect(span).toBe(6);
  });

  it("publica o rascunho com lote ativo", async () => {
    let published = false;
    const fetchMock = routeFetch([
      { method: "POST", path: /\/events\/0b6e\/publication$/, reply: () => { published = true; return jsonResponse(200, apiEvent()); } },
      { method: "GET", path: EVENT_PATH, reply: () => jsonResponse(200, apiEvent({ status: published ? "PUBLISHED" : "DRAFT" })) },
      ...detailHandlers(apiEvent()).slice(1),
    ]);
    const user = userEvent.setup();

    renderAt("/painel/eventos/0b6e");

    await user.click(await screen.findByRole("button", { name: "Publicar evento" }));

    expect(await screen.findByRole("link", { name: "Ver na vitrine" })).toBeInTheDocument();
    expect(calls(fetchMock, "POST", /\/publication$/)).toHaveLength(1);
    expect(screen.queryByRole("button", { name: "Publicar evento" })).not.toBeInTheDocument();
  });

  it("sem lote ativo, a publicação fica desabilitada e explica o motivo", async () => {
    routeFetch(detailHandlers(apiEvent({ status: "DRAFT" }), []));

    renderAt("/painel/eventos/0b6e");

    expect(await screen.findByRole("button", { name: "Publicar evento" })).toBeDisabled();
    expect(screen.getByText("Adicione um lote para poder publicar.")).toBeInTheDocument();
  });

  it("mostra a recusa da publicação", async () => {
    routeFetch([
      { method: "POST", path: /\/publication$/, reply: () => jsonResponse(400, [{ code: "EVENT_WITHOUT_PAYMENT_METHOD", message: "Configure uma integração de pagamento para Pix na aba Integrações antes de publicar o evento." }]) },
      ...detailHandlers(apiEvent({ status: "DRAFT" })),
    ]);
    const user = userEvent.setup();

    renderAt("/painel/eventos/0b6e");

    await user.click(await screen.findByRole("button", { name: "Publicar evento" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("aba Integrações");
  });

  it("pausa um lote e recarrega a página", async () => {
    const fetchMock = routeFetch([
      { method: "PUT", path: /\/batches\/b1$/, reply: () => jsonResponse(200, apiBatch({ status: "INACTIVE", saleState: "PAUSED" })) },
      ...detailHandlers(apiEvent()),
    ]);
    const user = userEvent.setup();

    renderAt("/painel/eventos/0b6e");

    await user.click(await screen.findByRole("button", { name: "Pausar 1º lote" }));

    await waitFor(() => expect(calls(fetchMock, "GET", BATCHES_PATH)).toHaveLength(2));
    expect(JSON.parse(calls(fetchMock, "PUT", /\/batches\/b1$/)[0].init.body as string).status).toBe("INACTIVE");
  });

  it("evento encerrado não aceita lote nem edição", async () => {
    routeFetch(detailHandlers(apiEvent({ startsAt: "2026-01-01T21:00:00.000Z", endsAt: "2026-01-02T06:00:00.000Z", status: "ENDED" })));

    renderAt("/painel/eventos/0b6e");

    expect(await screen.findByText("Encerrado")).toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Adicionar lote" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Editar evento" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ver na vitrine" })).not.toBeInTheDocument();
  });

  it("evento de outra produtora volta para a lista", async () => {
    routeFetch([
      { method: "GET", path: EVENT_PATH, reply: () => jsonResponse(404, [{ code: "EVENT_NOT_FOUND", message: "O evento informado não existe." }]) },
      { method: "GET", path: BATCHES_PATH, reply: () => jsonResponse(404, [{ code: "EVENT_NOT_FOUND", message: "O evento informado não existe." }]) },
      { method: "GET", path: DAILY_PATH, reply: () => jsonResponse(404, [{ code: "EVENT_NOT_FOUND", message: "O evento informado não existe." }]) },
      { method: "GET", path: /\/events$/, reply: () => jsonResponse(200, []) },
    ]);

    renderAt("/painel/eventos/0b6e");

    await waitFor(() => expect(location()).toBe("/painel/eventos"));
  });

  it("mostra a descrição formatada", async () => {
    routeFetch(detailHandlers(apiEvent({ description: "## Atrações\n\n- **Banda Maré** às 21h" })));

    renderAt("/painel/eventos/0b6e");

    expect(await screen.findByRole("heading", { level: 2, name: "Atrações" })).toBeInTheDocument();
    expect(screen.getByText("Banda Maré").tagName).toBe("STRONG");
  });
});

describe("demonstração intacta", () => {
  it("sem ?api=real, a aba continua na demonstração sem chamar a API", () => {
    window.sessionStorage.removeItem("ntix:api-mode");
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    renderAt("/painel/eventos");

    expect(screen.getByRole("heading", { name: "Eventos e lotes" })).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
