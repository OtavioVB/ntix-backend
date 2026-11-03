import { render as baseRender, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { ProducerSessionProvider } from "../../producer/session";
import { ProducersPage } from "./ProducersPage";

function render(element: ReactElement) {
  return baseRender(
    <MemoryRouter>
      <ProducerSessionProvider>{element}</ProducerSessionProvider>
    </MemoryRouter>,
  );
}

beforeAll(() => {
  // jsdom não implementa canvas.
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
});

describe("cadastro de produtor", () => {
  it("não avança com campos inválidos, mostra os erros e foca o primeiro", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await user.click(screen.getByRole("button", { name: "Continuar" }));

    expect(screen.getByText("Informe o nome da produtora.")).toBeInTheDocument();
    expect(screen.getByText("Informe o telefone com DDD.")).toBeInTheDocument();
    expect(screen.getByLabelText("Nome da produtora")).toHaveFocus();
    expect(screen.getByLabelText("Nome da produtora")).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByLabelText("Nome do administrador")).not.toBeInTheDocument();
  });

  async function fillProducer(user: ReturnType<typeof userEvent.setup>) {
    await user.type(screen.getByLabelText("Nome da produtora"), "Maré Alta");
    await user.type(screen.getByLabelText("E-mail da produtora"), "contato@exemplo.test");
    await user.type(screen.getByLabelText("Telefone"), "11987654321");
    expect(screen.getByLabelText("Telefone")).toHaveValue("(11) 98765-4321");
    await user.click(screen.getByRole("button", { name: "Continuar" }));
  }

  it("pede o administrador, que acessa com o e-mail da produtora", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await fillProducer(user);

    expect(screen.getByLabelText("Nome do administrador")).toHaveFocus();
    expect(screen.queryByLabelText("E-mail de acesso")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Criar meu cadastro" }));
    expect(screen.getByText("Informe o nome do administrador.")).toBeInTheDocument();
    expect(screen.getByText(/A senha precisa ter pelo menos 8/)).toBeInTheDocument();
  });

  it("guarda o que foi digitado ao voltar um passo", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await fillProducer(user);
    await user.click(screen.getByRole("button", { name: /Voltar/ }));

    expect(screen.getByLabelText("Nome da produtora")).toHaveValue("Maré Alta");
  });

  it("mostra e oculta a senha", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await fillProducer(user);
    expect(screen.getByLabelText("Senha")).toHaveAttribute("type", "password");
    await user.click(screen.getByRole("button", { name: "Mostrar" }));
    expect(screen.getByLabelText("Senha")).toHaveAttribute("type", "text");
  });

  it("conclui o cadastro válido e avisa que é demonstração", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await fillProducer(user);
    await user.type(screen.getByLabelText("Nome do administrador"), "Pessoa Administradora");
    await user.type(screen.getByLabelText("Senha"), "Senha@123");
    await user.click(screen.getByRole("button", { name: "Criar meu cadastro" }));

    expect(await screen.findByText(/Tudo certo, Maré Alta/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Senha")).not.toBeInTheDocument();
    expect(screen.getByText(/nenhum dado foi enviado/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Entrar na plataforma" })).toHaveAttribute("href", "/painel");
  });

  async function fillAdministrator(user: ReturnType<typeof userEvent.setup>) {
    await user.type(screen.getByLabelText("Nome do administrador"), "Pessoa Administradora");
    await user.type(screen.getByLabelText("Senha"), "Senha@123");
    await user.click(screen.getByRole("button", { name: "Criar meu cadastro" }));
  }

  describe("com a API real (?api=real)", () => {
    beforeEach(() => window.sessionStorage.setItem("ntix:api-mode", "real"));
    afterEach(() => {
      window.sessionStorage.clear();
      vi.unstubAllGlobals();
    });

    it("cadastra na API e pede a verificação do e-mail, sem abrir o painel", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("[]", { status: 200 })));
      const user = userEvent.setup();
      render(<ProducersPage />);

      await fillProducer(user);
      await fillAdministrator(user);

      expect(await screen.findByText(/Falta só confirmar o seu e-mail/)).toBeInTheDocument();
      expect(screen.getByText(/contato@exemplo.test/)).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Entrar na plataforma" })).not.toBeInTheDocument();
    });

    it("volta ao passo da produtora quando o e-mail já existe", async () => {
      const body = JSON.stringify([{ code: "PRODUCER_EMAIL_ALREADY_EXISTS", message: "Já existe um cadastro de produtora com o e-mail informado." }]);
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, { status: 400 })));
      const user = userEvent.setup();
      render(<ProducersPage />);

      await fillProducer(user);
      await fillAdministrator(user);

      expect(await screen.findByText("Já existe um cadastro de produtora com o e-mail informado.")).toBeInTheDocument();
      expect(screen.getByLabelText("E-mail da produtora")).toHaveAttribute("aria-invalid", "true");
    });
  });

  it("muda o rótulo do nome para pessoa física", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await user.click(screen.getByRole("radio", { name: "Pessoa física" }));

    expect(screen.getByLabelText("Seu nome")).toBeInTheDocument();
  });
});

describe("laboratório de marca", () => {
  it("aplica o nome na loja e aproveita no cadastro", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await user.type(screen.getByLabelText("Nome da sua produtora"), "Maré Alta");

    const store = within(screen.getByRole("group", { name: "Pré-visualização da loja" }));
    expect(store.getByText("Maré Alta")).toBeInTheDocument();
    expect(store.getByText("mare-alta.com.br")).toBeInTheDocument();
    expect(screen.getByLabelText("Nome da produtora")).toHaveValue("Maré Alta");
  });

  it("não sobrescreve um nome que a pessoa digitou no cadastro", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await user.type(screen.getByLabelText("Nome da produtora"), "Outro Nome");
    await user.type(screen.getByLabelText("Nome da sua produtora"), "Maré Alta");

    expect(screen.getByLabelText("Nome da produtora")).toHaveValue("Outro Nome");
  });

  it("garante contraste na tinta da cor escolhida", async () => {
    const user = userEvent.setup();
    render(<ProducersPage />);

    await user.click(screen.getByRole("button", { name: "Teatro" }));

    const store = document.querySelector<HTMLElement>("[data-brand]")!;
    expect(store.style.getPropertyValue("--n-on-brand")).toBe("#ffffff");
    expect(screen.getByText("Temporada Aurora")).toBeInTheDocument();
  });
});
