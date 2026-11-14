import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import type { ProducerIntegration } from "../../api/payments";
import { ProducerSessionProvider, type ProducerSession } from "../../producer/session";
import PanelRoutes from "./PanelRoutes";
import { credentialHint, DEMO_PROVIDERS, integrationsForMethod, move, validateIntegration } from "./integrations";

const ADMIN = { id: "admin", name: "Pessoa Administradora", email: "admin@exemplo.test", isAdmin: true, invited: false };

const LOGGED_IN: Partial<ProducerSession> = { name: "Maré Alta", slug: "mare-alta", loggedIn: true, adminEmail: ADMIN.email, team: [ADMIN] };

function integration(name: string, status: ProducerIntegration["status"] = "ACTIVE"): ProducerIntegration {
  return { id: `id-${name}`, providerCode: "ABACATEPAY", name, status, credentials: [{ key: "API_KEY", isSecret: true, valueHint: "a91f" }] };
}

function renderIntegrations(session: Partial<ProducerSession> = {}) {
  return render(
    <MemoryRouter initialEntries={["/painel/integracoes"]}>
      <ProducerSessionProvider initial={{ ...LOGGED_IN, ...session }}>
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

beforeEach(() => window.sessionStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe("regras da aba Integrações", () => {
  it("mostra do segredo só o final, nunca mais da metade", () => {
    expect(credentialHint("abc_prod_7f3e9a1b2c7c2e")).toBe("7c2e");
    expect(credentialHint("abcd")).toBe("cd");
    expect(credentialHint("a")).toBe("");
  });

  it("valida como a API: nome, credencial obrigatória e espaço nas pontas", () => {
    const provider = DEMO_PROVIDERS[0];

    expect(validateIntegration({ providerCode: "ABACATEPAY", name: " ", credentials: {} }, provider, []).name).toBeDefined();
    expect(validateIntegration({ providerCode: "ABACATEPAY", name: "Conta", credentials: {} }, provider, []).credentials.API_KEY).toBeDefined();
    expect(validateIntegration({ providerCode: "ABACATEPAY", name: "Conta", credentials: { API_KEY: " abc" } }, provider, []).credentials.API_KEY).toBeDefined();
    expect(validateIntegration({ providerCode: "ABACATEPAY", name: "Conta", credentials: { API_KEY: "abc" } }, provider, [integration("Conta")]).name).toBe("Já existe uma integração com este nome.");
    expect(validateIntegration({ providerCode: "ABACATEPAY", name: "Conta", credentials: { API_KEY: "abc" } }, provider, []).credentials).toEqual({});
  });

  it("só oferece no método as integrações de fornecedor que o aceita", () => {
    const pixOnly = { ...DEMO_PROVIDERS[0], code: "SO_PIX", paymentMethods: ["PIX" as const] };
    const pixIntegration = { ...integration("Só Pix"), providerCode: "SO_PIX" };

    expect(integrationsForMethod("BOLETO", [integration("AbacatePay"), pixIntegration], [DEMO_PROVIDERS[0], pixOnly]).map((item) => item.name)).toEqual(["AbacatePay"]);
  });

  it("move a prioridade sem sair da lista", () => {
    expect(move(["a", "b", "c"], 2, -1)).toEqual(["a", "c", "b"]);
    expect(move(["a", "b"], 0, -1)).toEqual(["a", "b"]);
  });
});

describe("aba Integrações em demonstração", () => {
  it("aparece no menu e fica liberada no ensaio", () => {
    renderIntegrations();

    const nav = screen.getByRole("navigation", { name: "Áreas da plataforma" });
    expect(within(nav).getByRole("link", { name: "Integrações" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Integrações");
    expect(screen.getByText("Não configurado: o pix não aparece no checkout.")).toBeInTheDocument();
  });

  it("conecta o AbacatePay mostrando só o final da chave e com o campo de segredo escondido", async () => {
    const user = userEvent.setup();
    renderIntegrations();

    const apiKey = screen.getByLabelText("Chave de API");
    expect(apiKey).toHaveAttribute("type", "password");

    await user.type(screen.getByLabelText("Nome da integração"), "Conta principal");
    await user.type(apiKey, "abc_dev_chave-secreta-a91f");
    await user.click(screen.getByRole("button", { name: "Conectar fornecedor" }));

    const list = screen.getByRole("region", { name: "1 integração" });
    expect(list).toHaveTextContent("Conta principal");
    expect(list).toHaveTextContent("••••a91f");
    expect(document.body).not.toHaveTextContent("abc_dev_chave-secreta");
    expect(window.localStorage.getItem("ntix.demo.session.v1") ?? "").not.toContain("abc_dev_chave-secreta");
  });

  it("recusa conectar sem a chave de API", async () => {
    const user = userEvent.setup();
    renderIntegrations();

    await user.type(screen.getByLabelText("Nome da integração"), "Conta principal");
    await user.click(screen.getByRole("button", { name: "Conectar fornecedor" }));

    expect(screen.getByText("Esta credencial é obrigatória para este fornecedor.")).toBeInTheDocument();
  });

  it("define o Pix com principal e reserva e reordena", async () => {
    const user = userEvent.setup();
    renderIntegrations({ paymentIntegrations: [integration("Principal"), integration("Reserva")] });

    const pix = screen.getByRole("region", { name: "Pix" });
    await user.selectOptions(within(pix).getByLabelText("Adicionar ao pix"), "id-Principal");
    await user.click(within(pix).getByRole("button", { name: "Adicionar" }));
    await user.selectOptions(within(pix).getByLabelText("Adicionar ao pix"), "id-Reserva");
    await user.click(within(pix).getByRole("button", { name: "Adicionar" }));

    let items = within(within(pix).getByRole("list", { name: "Prioridades do pix" })).getAllByRole("listitem");
    expect(items.map((item) => within(item).getByText(/Principal|Reserva/, { selector: "strong" }).textContent)).toEqual(["Principal", "Reserva"]);

    await user.click(within(pix).getByRole("button", { name: "Subir Reserva no pix" }));

    items = within(within(pix).getByRole("list", { name: "Prioridades do pix" })).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Reserva");
    expect(items[1]).toHaveTextContent("Principal");
  });

  it("inativa a integração e a mantém na rota sinalizada", async () => {
    const user = userEvent.setup();
    renderIntegrations({ paymentIntegrations: [integration("Principal")], paymentRoutes: { PIX: ["id-Principal"], BOLETO: [], CREDIT_CARD: [] } });

    await user.click(screen.getByRole("button", { name: "Inativar Principal" }));

    const pix = screen.getByRole("region", { name: "Pix" });
    expect(within(pix).getByText("Inativa")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reativar Principal" })).toBeInTheDocument();
  });
});

describe("aba Integrações com a API real", () => {
  it("carrega catálogo, integrações e rotas com o access token", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    const fetchMock = vi.fn((url: string) => {
      const body = url.endsWith("/payment-providers")
        ? DEMO_PROVIDERS
        : url.endsWith("/payment-integrations")
          ? [integration("Conta da API")]
          : { PIX: [{ priority: 1, integrationId: "id-Conta da API", name: "Conta da API", status: "ACTIVE" }], BOLETO: [], CREDIT_CARD: [] };
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }));
    });
    vi.stubGlobal("fetch", fetchMock);

    renderIntegrations({ accessToken: "token-123" });

    await waitFor(() => expect(screen.getByRole("region", { name: "1 integração" })).toHaveTextContent("Conta da API"));
    expect(within(screen.getByRole("region", { name: "Pix" })).getByText("Conta da API")).toBeInTheDocument();

    const authenticated = fetchMock.mock.calls.filter(([url]) => String(url).includes("/producers/mare-alta/"));
    expect(authenticated).toHaveLength(2);
    for (const [, init] of authenticated as unknown as [string, RequestInit][]) expect((init.headers as Record<string, string>).Authorization).toBe("Bearer token-123");
  });

  it("pede para entrar de novo quando a sessão não é aceita", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 401 })));

    renderIntegrations();

    expect(await screen.findByRole("alert")).toHaveTextContent("Sua sessão expirou. Entre de novo para continuar.");
  });
});
