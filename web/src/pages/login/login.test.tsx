import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { clearSessionTokens, readRefreshToken } from "../../api/auth";
import { ProducerSessionProvider, useProducerSession, type ProducerSession } from "../../producer/session";
import { LoginPage } from "./LoginPage";

function PanelProbe() {
  const { session } = useProducerSession();
  return <h1>Painel de {session.name} ({session.slug})</h1>;
}

function renderLogin(session: Partial<ProducerSession> = {}) {
  return render(
    <MemoryRouter initialEntries={["/entrar"]}>
      <ProducerSessionProvider initial={session}>
        <Routes>
          <Route path="/entrar" element={<LoginPage />} />
          <Route path="/painel/*" element={<PanelProbe />} />
        </Routes>
      </ProducerSessionProvider>
    </MemoryRouter>,
  );
}

async function fillAndSubmit(email: string, password: string) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("E-mail"), email);
  await user.type(screen.getByLabelText("Senha"), password);
  await user.click(screen.getByRole("button", { name: /Entrar/ }));
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
});
afterEach(() => {
  vi.unstubAllGlobals();
  clearSessionTokens();
});

describe("login em demonstração", () => {
  it("continua conferindo só a equipe deste navegador", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    renderLogin({ name: "Maré Alta", team: [{ id: "a", name: "Ana", email: "ana@exemplo.test", isAdmin: true, invited: false }] });

    await fillAndSubmit("ana@exemplo.test", "qualquer");

    expect(await screen.findByRole("heading", { name: /Painel de Maré Alta/ })).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByText(/Demonstração/)).not.toBeInTheDocument();
  });
});

describe("login com a API real", () => {
  beforeEach(() => window.sessionStorage.setItem("ntix:api-mode", "real"));

  it("entra pela API e abre o painel da produtora do usuário", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            accessToken: "access",
            accessTokenExpiresIn: 900,
            refreshToken: "refresh",
            refreshTokenExpiresIn: 604800,
            user: { id: "u1", name: "Ana Souza", userType: "ADMINISTRATOR", status: "PENDING" },
            producer: { id: "p1", slug: "produtora-x", name: "Produtora X", status: "PENDING" },
          }),
          { status: 200 },
        ),
      ),
    );
    renderLogin();

    expect(screen.queryByText(/Demonstração/)).not.toBeInTheDocument();
    await fillAndSubmit("ana@produtora.com.br", "Senha@123");

    expect(await screen.findByRole("heading", { name: "Painel de Produtora X (produtora-x)" })).toBeInTheDocument();
    expect(readRefreshToken()).toBe("refresh");
  });

  it("mostra a recusa da API sem abrir o painel", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify([{ code: "INVALID_CREDENTIALS", message: "E-mail ou senha incorretos." }]), { status: 401 })));
    renderLogin();

    await fillAndSubmit("ana@produtora.com.br", "Errada@123");

    expect(await screen.findByRole("alert")).toHaveTextContent("E-mail ou senha incorretos.");
    expect(screen.queryByRole("heading", { name: /Painel/ })).not.toBeInTheDocument();
  });

  it("não chama a API com o e-mail fora do formato", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    renderLogin();

    await fillAndSubmit("ana", "Senha@123");

    expect(screen.getByText("Informe um e-mail válido.")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
