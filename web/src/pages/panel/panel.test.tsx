import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { emptyPlatformData, generateDemoData } from "../../platform/demoData";
import { formatDocument, isValidDocument } from "../../producer/legal";
import { ProducerSessionProvider, type ProducerSession } from "../../producer/session";
import PanelRoutes from "./PanelRoutes";
import { CHANNELS, SALES_DAYS, SAMPLE_EVENT, SUMMARY } from "./sampleData";

const ADMIN = { id: "admin", name: "Pessoa Administradora", email: "admin@exemplo.test", isAdmin: true, invited: false };

const LOGGED_IN: Partial<ProducerSession> = { name: "Maré Alta", loggedIn: true, adminEmail: ADMIN.email, team: [ADMIN] };

function renderPanel(path = "/", session: Partial<ProducerSession> = {}, state?: unknown) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: `/painel${path === "/" ? "" : path}`, state }]}>
      <ProducerSessionProvider initial={{ ...LOGGED_IN, ...session }}>
        <Routes>
          <Route path="/painel/*" element={<PanelRoutes />} />
        </Routes>
      </ProducerSessionProvider>
    </MemoryRouter>,
  );
}

const ACTIVE = (hasStore = false): Partial<ProducerSession> => ({
  status: "ACTIVE",
  ticketeria: hasStore ? "ACTIVE" : "NONE",
  platform: generateDemoData(Date.now(), hasStore),
});

beforeAll(() => {
  window.scrollTo = vi.fn();
});

describe("modo ensaio", () => {
  it("deixa claro que os dados são de exemplo e como liberar tudo", () => {
    renderPanel();

    const bar = screen.getByRole("region", { name: "Situação do cadastro" });
    expect(bar).toHaveTextContent("Modo ensaio");
    expect(bar).toHaveTextContent("dados são de exemplo");
    expect(within(bar).getByRole("link", { name: "Enviar para aprovação" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Ensaio geral, Maré Alta.");
  });

  it("lista todos os domínios no menu e marca os bloqueados", () => {
    renderPanel();

    const nav = screen.getByRole("navigation", { name: "Áreas da plataforma" });
    for (const label of ["Visão geral", "Eventos e lotes", "Ticketeria", "Equipe"]) {
      expect(within(nav).getByRole("link", { name: label })).toBeInTheDocument();
    }
    for (const label of ["Vendas e ingressos", "Clientes", "Afiliados", "Carteira", "Cobranças", "Plano de intermediação"]) {
      expect(within(nav).getByRole("link", { name: `${label} (liberado após aprovação)` })).toBeInTheDocument();
    }
  });

  it("mantém a área bloqueada atrás da cortina e fora do leitor de tela", () => {
    renderPanel("/carteira");

    expect(screen.getByRole("heading", { level: 1, name: "Carteira" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ver o que falta para liberar" })).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("exige login", () => {
    renderPanel("/", { loggedIn: false });

    expect(screen.queryByRole("region", { name: "Situação do cadastro" })).not.toBeInTheDocument();
  });

  it("não deixa criar evento antes da aprovação", () => {
    renderPanel("/eventos/novo");

    expect(screen.queryByLabelText("Nome do evento")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Eventos e lotes" })).toBeInTheDocument();
  });
});

describe("ticketeria", () => {
  it("é oferta com preço de exemplo, não uma loja já disponível", () => {
    renderPanel("/ticketeria");

    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Vá além do hub");
    expect(screen.getByText("Valor de exemplo")).toBeInTheDocument();
    expect(screen.getByText(/R\$\s*300,00/)).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Pré-visualização da loja" })).toBeInTheDocument();
  });

  it("recalcula o simulador com os números da pessoa", async () => {
    const user = userEvent.setup();
    renderPanel("/ticketeria");

    const price = screen.getByLabelText("Preço médio do ingresso (R$)");
    await user.clear(price);
    await user.type(price, "60,00");

    expect(screen.getByText(/100 ingressos por mês/)).toBeInTheDocument();
  });

  it("entra na contratação e aparece na aprovação", async () => {
    const user = userEvent.setup();
    renderPanel("/ticketeria");

    await user.click(screen.getByRole("button", { name: "Quero a minha ticketeria" }));
    expect(screen.getByText("Incluída na sua contratação.")).toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: "Ir para a aprovação" }));
    expect(screen.getByText("Ticketeria própria incluída:")).toBeInTheDocument();
  });

  it("depois da aprovação, sem contratar, continua sendo oferta", async () => {
    const user = userEvent.setup();
    renderPanel("/ticketeria", ACTIVE(false));

    await user.click(screen.getByRole("button", { name: "Contratar agora" }));

    expect(screen.getByRole("heading", { level: 1, name: "Sua ticketeria" })).toBeInTheDocument();
  });

  it("gera a mensalidade ao contratar depois da aprovação", async () => {
    const user = userEvent.setup();
    renderPanel("/ticketeria", ACTIVE(false));

    await user.click(screen.getByRole("button", { name: "Contratar agora" }));
    await user.click(screen.getByRole("link", { name: "Cobranças" }));

    expect(screen.getByText("Mensalidade da ticketeria própria")).toBeInTheDocument();
  });
});

describe("equipe", () => {
  it("funciona no ensaio: mostra o administrador do cadastro, convida e remove", async () => {
    const user = userEvent.setup();
    renderPanel("/equipe");

    const list = screen.getByRole("heading", { name: "1 pessoa com acesso" }).closest("section")!;
    expect(within(list).getByText("Pessoa Administradora")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Remover Pessoa Administradora/ })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText("Nome"), "Pessoa Dois");
    await user.type(screen.getByLabelText("E-mail"), "dois@exemplo.test");
    await user.click(screen.getByRole("button", { name: "Enviar convite" }));
    expect(screen.getByText("2 pessoas com acesso")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Remover Pessoa Dois da equipe" }));
    expect(screen.getByText("1 pessoa com acesso")).toBeInTheDocument();
  });

  it("recusa e-mail repetido", async () => {
    const user = userEvent.setup();
    renderPanel("/equipe");

    await user.type(screen.getByLabelText("Nome"), "Outra");
    await user.type(screen.getByLabelText("E-mail"), ADMIN.email);
    await user.click(screen.getByRole("button", { name: "Enviar convite" }));

    expect(screen.getByText("Esta pessoa já faz parte da equipe.")).toBeInTheDocument();
  });
});

describe("cortina de abertura", () => {
  it("abre só para quem acabou de chegar e não fica cobrindo a tela", async () => {
    const { container, unmount } = renderPanel("/", {}, { openCurtain: true });

    await waitFor(() => expect(container.querySelector(".n-curtain-opening")).toBeInTheDocument());
    await waitFor(() => expect(container.querySelector(".n-curtain-opening")).not.toBeInTheDocument(), { timeout: 2500 });
    unmount();

    const direct = renderPanel();
    expect(direct.container.querySelector(".n-curtain-opening")).not.toBeInTheDocument();
  });
});

describe("envio para aprovação", () => {
  it("avisa o que falta quando o cadastro está incompleto", async () => {
    const user = userEvent.setup();
    renderPanel("/aprovacao");

    await user.click(screen.getByRole("button", { name: "Enviar para aprovação" }));

    expect(screen.getByRole("status")).toHaveTextContent("Ainda falta");
    expect(screen.getByRole("region", { name: "Situação do cadastro" })).toHaveTextContent("Modo ensaio");
  });

  it("recusa documento inválido", async () => {
    const user = userEvent.setup();
    renderPanel("/aprovacao");

    await user.type(screen.getByLabelText("CNPJ"), "11222333000100");
    await user.tab();

    expect(screen.getByText("Confira o CNPJ informado.")).toBeInTheDocument();
  });

  it("vai do envio à aprovação e abre a plataforma de produção", async () => {
    const user = userEvent.setup();
    renderPanel("/aprovacao");

    await user.click(screen.getByRole("checkbox", { name: /Quero contratar/ }));
    await user.click(screen.getByRole("checkbox", { name: /aceito os termos/ }));
    await user.type(screen.getByLabelText("CNPJ"), "11222333000181");
    await user.type(screen.getByLabelText("Razão social"), "Maré Alta Produções Ltda");
    await user.click(screen.getByRole("button", { name: "Enviar para aprovação" }));

    expect(await screen.findByText("Em aprovação", { selector: ".n-stamp" })).toBeInTheDocument();
    const bar = screen.getByRole("region", { name: "Situação do cadastro" });
    expect(bar).toHaveTextContent("Cadastro em análise");
    expect(within(bar).queryByRole("link")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Simular aprovação da Ntix" }));

    expect(screen.queryByRole("region", { name: "Situação do cadastro" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Ambiente de demonstração" })).toBeInTheDocument();
    expect(screen.getByText("Receita líquida")).toBeInTheDocument();
    expect(screen.queryByText(/liberado após aprovação/)).not.toBeInTheDocument();
  });
});

describe("modo de produção", () => {
  it("o dashboard usa o período escolhido", async () => {
    const user = userEvent.setup();
    renderPanel("/", ACTIVE());

    const net = () => document.querySelector(".hero-figure")?.textContent;
    const thirtyDays = net();
    await user.click(screen.getByRole("radio", { name: "90 dias" }));

    expect(net()).not.toBe(thirtyDays);
  });

  it("sem ticketeria, o canal próprio não aparece como se existisse", () => {
    renderPanel("/", ACTIVE(false));

    const channels = screen.getByRole("heading", { name: "De onde vêm as vendas" }).closest("section")!;
    expect(within(channels).queryByText("Ticketeria própria")).not.toBeInTheDocument();
    expect(within(channels).getByText(/veja como ficaria a sua ticketeria/)).toBeInTheDocument();
  });

  it("com ticketeria, o canal próprio aparece", () => {
    renderPanel("/", ACTIVE(true));

    const channels = screen.getByRole("heading", { name: "De onde vêm as vendas" }).closest("section")!;
    expect(within(channels).getByText("Ticketeria própria")).toBeInTheDocument();
  });

  it("conta nova vê os primeiros passos em vez de números zerados", () => {
    renderPanel("/", { status: "ACTIVE", platform: emptyPlatformData(Date.now()) });

    expect(screen.getByText("Cortinas abertas. Hora da estreia.")).toBeInTheDocument();
    expect(screen.queryByText("Receita líquida")).not.toBeInTheDocument();
  });

  it("alterna entre conta nova e vendas fictícias", async () => {
    const user = userEvent.setup();
    renderPanel("/", ACTIVE());

    await user.click(screen.getByRole("button", { name: "Ver como uma conta nova" }));
    expect(screen.getByText("Cortinas abertas. Hora da estreia.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Carregar vendas fictícias" }));
    expect(screen.getByText("Receita líquida")).toBeInTheDocument();
  });

  it("cria evento, adiciona lote e publica", async () => {
    const user = userEvent.setup();
    renderPanel("/eventos/novo", { status: "ACTIVE", platform: emptyPlatformData(Date.now()) });

    await user.click(screen.getByRole("button", { name: "Criar como rascunho" }));
    expect(screen.getByText("Informe o nome do evento.")).toBeInTheDocument();

    await user.type(screen.getByLabelText("Nome do evento"), "Show de Teste");
    await user.type(screen.getByLabelText("Local"), "Casa de Teste");
    await user.type(screen.getByLabelText("Data"), "2099-12-31");
    await user.click(screen.getByRole("button", { name: "Criar como rascunho" }));

    expect(screen.getByRole("heading", { level: 1, name: "Show de Teste" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Publicar evento" })).toBeDisabled();

    await user.type(screen.getByLabelText("Nome do lote"), "1º lote");
    await user.type(screen.getByLabelText("Preço (R$)"), "50,00");
    await user.type(screen.getByLabelText("Quantidade"), "100");
    await user.type(screen.getByLabelText("Início das vendas"), "2099-01-01");
    await user.type(screen.getByLabelText("Fim das vendas"), "2099-12-30");
    await user.click(screen.getByRole("button", { name: "Adicionar lote" }));

    await user.click(screen.getByRole("button", { name: "Publicar evento" }));
    expect(screen.queryByRole("button", { name: "Publicar evento" })).not.toBeInTheDocument();
    expect(screen.getByText("0 de 100")).toBeInTheDocument();
  });

  it("filtra as vendas e explica o valor do pedido", async () => {
    const user = userEvent.setup();
    const session = ACTIVE();
    renderPanel("/vendas", session);

    const first = session.platform!.orders.at(-1)!;
    await user.type(screen.getByLabelText("Buscar"), first.code);
    await user.click(screen.getByRole("button", { name: first.code }));

    const detail = screen.getByRole("heading", { level: 2, name: first.code }).closest("section")!;
    expect(within(detail).getByText("Taxa de intermediação")).toBeInTheDocument();
    expect(within(detail).getByText(`${first.code}-01`)).toBeInTheDocument();
  });

  it("registra saque e recusa valor acima do saldo", async () => {
    const user = userEvent.setup();
    renderPanel("/carteira", ACTIVE());

    await user.type(screen.getByLabelText("Valor (R$)"), "99999999,00");
    await user.click(screen.getByRole("button", { name: "Solicitar saque" }));
    expect(screen.getByText(/O saldo disponível é de/)).toBeInTheDocument();

    await user.clear(screen.getByLabelText("Valor (R$)"));
    await user.type(screen.getByLabelText("Valor (R$)"), "10,00");
    await user.click(screen.getByRole("button", { name: "Solicitar saque" }));
    expect(screen.getByText(/registrado no extrato/)).toBeInTheDocument();
  });

  it("abre todas as áreas sem cortina", () => {
    for (const [path, heading] of [
      ["/clientes", "Clientes"],
      ["/afiliados", "Afiliados"],
      ["/cobrancas", "Cobranças"],
      ["/plano", "Plano de intermediação"],
      ["/eventos", "Eventos e lotes"],
    ]) {
      const view = renderPanel(path, ACTIVE());
      expect(screen.getByRole("heading", { level: 1, name: heading })).toBeInTheDocument();
      expect(view.container.querySelector(".n-curtain")).not.toBeInTheDocument();
      view.unmount();
    }
  });

  it("volta da aprovação para o painel", () => {
    renderPanel("/aprovacao", ACTIVE());

    expect(screen.getByText("Receita líquida")).toBeInTheDocument();
  });
});

describe("dados de exemplo do ensaio", () => {
  it("fecham entre dias, lotes e canais, sem ticketeria", () => {
    const soldInBatches = SAMPLE_EVENT.batches.reduce((sum, batch) => sum + batch.sold, 0);
    const soldInChannels = CHANNELS.reduce((sum, channel) => sum + channel.tickets, 0);

    expect(SUMMARY.tickets).toBe(342);
    expect(soldInBatches).toBe(SUMMARY.tickets);
    expect(soldInChannels).toBe(SUMMARY.tickets);
    expect(CHANNELS.map((channel) => channel.name)).toEqual(["Hub Ntix", "Links de afiliados"]);
    expect(SALES_DAYS).toHaveLength(30);
    expect(SALES_DAYS[0].label).toBe("03/out");
    expect(SALES_DAYS[29].label).toBe("01/nov");
  });

  it("calculam o líquido a partir das regras", () => {
    expect(SUMMARY.gross).toBe(200 * 90 + 142 * 120);
    expect(SUMMARY.fee).toBeCloseTo(370.4);
    expect(SUMMARY.commission).toBeCloseTo(672);
    expect(SUMMARY.net).toBeCloseTo(33997.6);
  });
});

describe("documento", () => {
  it.each([
    ["COMPANY", "11.222.333/0001-81", true],
    ["COMPANY", "11.222.333/0001-80", false],
    ["COMPANY", "11.111.111/1111-11", false],
    ["INDIVIDUAL", "529.982.247-25", true],
    ["INDIVIDUAL", "529.982.247-24", false],
    ["INDIVIDUAL", "111.111.111-11", false],
    ["INDIVIDUAL", "123", false],
  ] as const)("%s %s é %s", (type, value, expected) => {
    expect(isValidDocument(type, value)).toBe(expected);
  });

  it("aplica a máscara de cada tipo", () => {
    expect(formatDocument("COMPANY", "11222333000181")).toBe("11.222.333/0001-81");
    expect(formatDocument("INDIVIDUAL", "52998224725")).toBe("529.982.247-25");
  });
});

describe("portaria", () => {
  it("fica atrás da cortina no ensaio", () => {
    renderPanel("/portaria");
    expect(screen.getByRole("heading", { name: "Portaria" })).toBeInTheDocument();
    expect(screen.getByText("Valide os ingressos na entrada com a câmera do celular.")).toBeInTheDocument();
  });

  it("valida pelo código digitado: libera uma vez e recusa a segunda", async () => {
    const user = userEvent.setup();
    const session = ACTIVE();
    const order = session.platform!.orders[0];
    const event = session.platform!.events.find((candidate) => candidate.id === order.eventId)!;
    const code = `${order.code}-01`;
    renderPanel("/portaria", session);

    await user.selectOptions(screen.getByLabelText("Evento"), event.id);
    // Sem câmera (jsdom), a tela diz isso e deixa digitar.
    expect(screen.getByText("Câmera indisponível aqui")).toBeInTheDocument();

    await user.type(screen.getByLabelText("Código do ingresso"), code);
    await user.click(screen.getByRole("button", { name: "Validar" }));
    expect(within(screen.getByRole("alert")).getByText("Entrada liberada")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: /^1 de \d+ ingressos já entraram$/ })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Ler próximo" }));
    await user.type(screen.getByLabelText("Código do ingresso"), code);
    await user.click(screen.getByRole("button", { name: "Validar" }));
    const alert = screen.getByRole("alert");
    expect(within(alert).getByText("Ingresso já utilizado")).toBeInTheDocument();
    expect(within(alert).getByText(/validado por Pessoa Administradora/)).toBeInTheDocument();
  });
});
