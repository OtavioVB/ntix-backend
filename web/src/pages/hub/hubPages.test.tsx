import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { CustomerProvider, demoAccountState, type CustomerState } from "../../customer/session";
import { generateDemoData } from "../../platform/demoData";
import { ProducerSessionProvider, useProducerSession, type ProducerSession } from "../../producer/session";
import HubRoutes from "./HubRoutes";

const ACCOUNT = { name: "Carla Teste", email: "carla@exemplo.test", document: "529.982.247-25" };
const SIGNED_IN: Partial<CustomerState> = { account: ACCOUNT, loggedIn: true };

function WalletProbe() {
  const { session } = useProducerSession();
  return <output data-testid="producer-orders">{session.platform?.orders.length ?? 0}</output>;
}

function renderHub(path = "/", customer: Partial<CustomerState> = {}, producer: Partial<ProducerSession> = {}) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ProducerSessionProvider initial={producer}>
        <CustomerProvider initial={customer}>
          <WalletProbe />
          <Routes>
            <Route path="/*" element={<HubRoutes />} />
          </Routes>
        </CustomerProvider>
      </ProducerSessionProvider>
    </MemoryRouter>,
  );
}

async function chooseTickets(user: ReturnType<typeof userEvent.setup>, label: string, times = 1) {
  const stepper = screen.getByRole("group", { name: label });
  for (let index = 0; index < times; index++) await user.click(within(stepper).getByRole("button", { name: `Adicionar um: ${label}` }));
}

async function goToCheckout(user: ReturnType<typeof userEvent.setup>) {
  await user.click(within(screen.getByRole("complementary", { name: "Resumo da compra" })).getByRole("button", { name: "Continuar" }));
}

async function fillCard(user: ReturnType<typeof userEvent.setup>, number: string) {
  await user.click(screen.getByRole("radio", { name: "Cartão de crédito" }));
  await user.type(screen.getByLabelText("Número do cartão"), number);
  await user.type(screen.getByLabelText("Nome impresso no cartão"), "CARLA TESTE");
  await user.type(screen.getByLabelText("Validade"), "1240");
  await user.type(screen.getByLabelText("Código de segurança"), "123");
}

beforeAll(() => {
  window.scrollTo = vi.fn();
  Element.prototype.scrollIntoView = vi.fn();
});

describe("vitrine", () => {
  it("mostra destaques e fileiras, sem eventos passados", () => {
    renderHub();

    expect(screen.getByRole("region", { name: "Eventos em destaque" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Em alta agora" })).toBeInTheDocument();
    expect(screen.getAllByText("Noites de Vinil").length).toBeGreaterThan(0);
    expect(screen.queryByText("Verão em Cena")).not.toBeInTheDocument();
  });

  it("filtra por categoria e permite limpar", async () => {
    const user = userEvent.setup();
    renderHub();

    await user.click(screen.getByRole("button", { name: "Infantil" }));

    expect(screen.getByText("1 evento encontrado", { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Pequenos Gigantes/ })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Em alta agora" })).not.toBeInTheDocument();
  });

  it("busca pelo cabeçalho e avisa quando não acha nada", async () => {
    const user = userEvent.setup();
    renderHub();

    await user.type(screen.getByLabelText("Buscar eventos"), "xyzzy{Enter}");

    expect(await screen.findByText("Nada por aqui com esses filtros")).toBeInTheDocument();
  });

  it("mostra na vitrine o evento publicado por um produtor da plataforma", () => {
    const platform = generateDemoData(Date.now(), false);
    renderHub("/", {}, { status: "ACTIVE", name: "Maré Alta", platform });

    expect(screen.getByRole("region", { name: "Acabaram de chegar" })).toBeInTheDocument();
  });
});

describe("página do evento", () => {
  it("mostra lotes à venda, esgotados e informações do evento", () => {
    renderHub("/eventos/festival-horizonte");

    expect(screen.getByRole("heading", { level: 1, name: "Festival Horizonte" })).toBeInTheDocument();
    expect(screen.getByText("Esgotado")).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Área VIP" })).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Pista · 1º lote" })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Programação" })).toBeInTheDocument();
  });

  it("não deixa passar do limite por pedido, somando os lotes", async () => {
    const user = userEvent.setup();
    renderHub("/eventos/festival-horizonte");

    await chooseTickets(user, "Pista · 2º lote", 4);
    await chooseTickets(user, "Área VIP", 2);

    expect(screen.getByRole("button", { name: "Adicionar um: Área VIP" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Adicionar um: Pista · 2º lote" })).toBeDisabled();
  });

  it("calcula o total e só libera continuar com algo escolhido", async () => {
    const user = userEvent.setup();
    renderHub("/eventos/festival-horizonte");
    const box = within(screen.getByRole("complementary", { name: "Resumo da compra" }));

    expect(box.getByRole("button", { name: "Continuar" })).toBeDisabled();
    await chooseTickets(user, "Pista · 2º lote", 2);

    expect(box.getByText(/R\$\s*360,00/, { selector: "strong" })).toBeInTheDocument();
    expect(box.getByRole("button", { name: "Continuar" })).toBeEnabled();
  });

  it("trata evento inexistente", () => {
    renderHub("/eventos/nao-existe");

    expect(screen.getByText("Este evento saiu de cartaz.")).toBeInTheDocument();
  });
});

describe("compra", () => {
  it("cria a conta na compra, paga com cartão e mostra o avatar com o ingresso na mão", async () => {
    const user = userEvent.setup();
    renderHub("/eventos/riso-solto");

    await chooseTickets(user, "Mesa compartilhada", 2);
    await goToCheckout(user);

    await user.click(screen.getByRole("button", { name: "Criar conta e continuar" }));
    expect(screen.getByText("Confira o CPF informado.")).toBeInTheDocument();

    await user.type(screen.getByLabelText("Nome completo"), "Carla Teste");
    await user.type(screen.getByLabelText("E-mail"), "carla@exemplo.test");
    await user.type(screen.getByLabelText("CPF"), "52998224725");
    await user.type(screen.getByLabelText("Senha"), "senha-de-teste");
    await user.click(screen.getByRole("button", { name: "Criar conta e continuar" }));

    await fillCard(user, "4111111111111111");
    await user.click(screen.getByRole("button", { name: /Pagar/ }));

    expect(await screen.findByRole("img", { name: /avatar segurando 2 ingressos de Riso Solto/ })).toBeInTheDocument();
    expect(screen.getByText("Ingresso garantido")).toBeInTheDocument();
    // Primeira compra sem avatar: já oferece montar.
    expect(screen.getByText(/Monte seu avatar/)).toBeInTheDocument();
  });

  it("recusa cartão inválido e mantém a pessoa na compra quando a operadora nega", async () => {
    const user = userEvent.setup();
    renderHub("/eventos/riso-solto", SIGNED_IN);

    await chooseTickets(user, "Mesa compartilhada");
    await goToCheckout(user);
    await fillCard(user, "4111111111111112");
    await user.click(screen.getByRole("button", { name: /Pagar/ }));
    expect(screen.getByText("Confira o número do cartão.")).toBeInTheDocument();

    await user.clear(screen.getByLabelText("Número do cartão"));
    await user.type(screen.getByLabelText("Número do cartão"), "4000000000000002");
    await user.click(screen.getByRole("button", { name: /Pagar/ }));

    expect(await screen.findByText(/recusado pela operadora/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Falta pouco." })).toBeInTheDocument();
  });

  it("Pix: fica pendente sem ingresso e só libera depois de pago", async () => {
    const user = userEvent.setup();
    renderHub("/eventos/riso-solto", SIGNED_IN);

    await chooseTickets(user, "Mesa compartilhada");
    await goToCheckout(user);
    await user.click(screen.getByRole("button", { name: /Gerar Pix/ }));

    expect(await screen.findByText("Aguardando pagamento", { selector: ".n-badge" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Ingressos deste pedido" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Simular pagamento do Pix" }));

    expect(await screen.findByRole("img", { name: /avatar segurando o ingresso de Riso Solto/ })).toBeInTheDocument();
  });

  it("volta ao evento quando a reserva expira", async () => {
    const user = userEvent.setup();
    renderHub("/eventos/riso-solto/checkout", {
      ...SIGNED_IN,
      cart: { eventId: "riso-solto", slug: "riso-solto", selection: { "riso-solto-1": 1 }, expiresAt: Date.now() + 200 },
    });
    void user;

    await waitFor(() => expect(screen.getByText(/Sua reserva expirou/)).toBeInTheDocument(), { timeout: 2500 });
  });

  it("não abre o pagamento sem nada no carrinho", () => {
    renderHub("/eventos/riso-solto/checkout", SIGNED_IN);

    expect(screen.queryByRole("heading", { name: "Falta pouco." })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Riso Solto" })).toBeInTheDocument();
  });

  it("venda de evento de produtor da plataforma chega nas vendas dele", async () => {
    const user = userEvent.setup();
    const platform = generateDemoData(Date.now(), false);
    renderHub("/eventos/event-aurora", SIGNED_IN, { status: "ACTIVE", name: "Maré Alta", platform });
    const before = Number(screen.getByTestId("producer-orders").textContent);

    await chooseTickets(user, "Lote único");
    await goToCheckout(user);
    await user.click(screen.getByRole("button", { name: /Gerar Pix/ }));
    await user.click(await screen.findByRole("button", { name: "Simular pagamento do Pix" }));

    await screen.findByText("Ingresso garantido");
    expect(Number(screen.getByTestId("producer-orders").textContent)).toBe(before + 1);
  });
});

describe("área do cliente", () => {
  const demo = () => demoAccountState(Date.now());

  it("exige login", () => {
    renderHub("/conta/pedidos");

    expect(screen.getByRole("heading", { level: 1, name: "Sua conta Ntix." })).toBeInTheDocument();
  });

  it("entra por e-mail ou CPF e recusa dados errados", async () => {
    const user = userEvent.setup();
    renderHub("/conta/entrar", { account: ACCOUNT, loggedIn: false });

    await user.click(screen.getByRole("radio", { name: "Já tenho conta" }));
    await user.type(screen.getByLabelText("E-mail ou CPF"), "errado@exemplo.test");
    await user.type(screen.getByLabelText("Senha"), "qualquer-senha");
    await user.click(screen.getByRole("button", { name: "Entrar" }));
    expect(screen.getByText("E-mail, CPF ou senha incorretos.")).toBeInTheDocument();

    await user.clear(screen.getByLabelText("E-mail ou CPF"));
    await user.type(screen.getByLabelText("E-mail ou CPF"), "529.982.247-25");
    await user.click(screen.getByRole("button", { name: "Entrar" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Meus ingressos" })).toBeInTheDocument();
  });

  it("lista pedidos com forma de pagamento e situação", () => {
    renderHub("/conta/pedidos", demo());

    expect(screen.getByRole("heading", { level: 1, name: "Pedidos" })).toBeInTheDocument();
    expect(screen.getAllByText("Pix").length).toBeGreaterThan(0);
    expect(screen.getByText("Visa final 1111 em 3x")).toBeInTheDocument();
    expect(screen.getByText("Aguardando pagamento")).toBeInTheDocument();
    expect(screen.getByText("Utilizado")).toBeInTheDocument();
    expect(screen.getByText("1 de 2 utilizados")).toBeInTheDocument();
  });

  it("separa ingressos próximos e passados e avisa do Pix pendente", async () => {
    const user = userEvent.setup();
    renderHub("/conta/ingressos", demo());

    expect(screen.getByText(/Falta pagar o Pix de Noites de Vinil/)).toBeInTheDocument();
    expect(screen.getAllByText("O Último Ato")).toHaveLength(2);
    expect(screen.queryByText("Verão em Cena")).not.toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: /Já foram/ }));

    expect(screen.getAllByText("Verão em Cena")).toHaveLength(2);
    expect(screen.getByText("Café com Comédia")).toBeInTheDocument();
    expect(screen.getAllByText("Utilizado").length).toBe(2);
    expect(screen.getByText("Não utilizado")).toBeInTheDocument();
  });

  it("o ingresso válido tem QR Code e, depois da entrada, vira utilizado", async () => {
    const user = userEvent.setup();
    const state = demo();
    const code = state.orders!.find((order) => order.event.slug === "o-ultimo-ato")!.tickets[0].code;
    renderHub(`/conta/ingressos/${code}`, state);

    expect(screen.getByTitle(`Código do ingresso ${code}`)).toBeInTheDocument();
    expect(screen.getByRole("img", { name: /avatar segurando o ingresso de O Último Ato/ })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Simular entrada no evento" }));

    expect(screen.getByText("Utilizado", { selector: ".n-stamp" })).toBeInTheDocument();
    expect(screen.queryByTitle(`Código do ingresso ${code}`)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Simular entrada no evento" })).not.toBeInTheDocument();
  });

  it("mostra só o guarda-roupa do estilo escolhido", async () => {
    const user = userEvent.setup();
    renderHub("/conta/avatar", SIGNED_IN);
    const tops = () => within(screen.getByRole("group", { name: "Parte de cima" }));

    await user.click(screen.getByRole("radio", { name: "Masculino" }));
    expect(tops().getByRole("button", { name: "Regata" })).toBeInTheDocument();
    expect(tops().queryByRole("button", { name: "Corset" })).not.toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Barba" })).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "Feminino" }));
    expect(tops().getByRole("button", { name: "Corset" })).toBeInTheDocument();
    expect(tops().queryByRole("button", { name: "Regata" })).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Barba" })).not.toBeInTheDocument();
    expect(within(screen.getByRole("group", { name: "Joias" })).getByRole("button", { name: "Argolas" })).toBeInTheDocument();

    // Vestido é peça inteira: não pede parte de baixo.
    await user.click(tops().getByRole("button", { name: "Vestido de franja" }));
    expect(screen.queryByRole("group", { name: "Parte de baixo" })).not.toBeInTheDocument();
  });
});
