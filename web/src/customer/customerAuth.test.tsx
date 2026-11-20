import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CUSTOMER_REFRESH_TOKEN_STORAGE_KEY, clearCustomerTokens, customerAuthorizedRequest, getCustomerAccessToken, readCustomerRefreshToken, signOutCustomer, storeCustomerTokens } from "../api/customerAuth";
import { AuthForm } from "../pages/hub/AuthForm";
import { CustomerProvider, useCustomer } from "./session";
import { signInCustomer, signUpCustomer } from "./signIn";

const SESSION = { accessToken: "access-1", accessTokenExpiresIn: 900, refreshToken: "refresh-1", refreshTokenExpiresIn: 2592000, customer: { id: "c1", firstName: "Marina" } };
const ME = { id: "c1", fullName: "Marina Souza", email: "marina@exemplo.com", document: "***.982.247-**" };

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function sent(fetchMock: ReturnType<typeof vi.fn>, call: number) {
  const [url, init] = fetchMock.mock.calls[call] as [string, RequestInit];
  return { url, init, headers: init.headers as Record<string, string>, body: init.body ? JSON.parse(init.body as string) : undefined };
}

beforeEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearCustomerTokens();
});

describe("sessão do comprador na API", () => {
  it("entra, guarda o refresh token separado do produtor e lê meus dados com o access token", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(200, SESSION)).mockResolvedValueOnce(jsonResponse(200, ME));
    vi.stubGlobal("fetch", fetchMock);

    const result = await signInCustomer(" 529.982.247-25 ", "Senha@123");

    expect(result).toEqual({ ok: true, account: { name: "Marina Souza", email: "marina@exemplo.com", document: "***.982.247-**" } });
    expect(sent(fetchMock, 0).url).toMatch(/\/api\/v1\/customers\/sessions$/);
    expect(sent(fetchMock, 0).body).toEqual({ login: "529.982.247-25", password: "Senha@123" });
    expect(sent(fetchMock, 1).headers.Authorization).toBe("Bearer access-1");
    expect(window.localStorage.getItem(CUSTOMER_REFRESH_TOKEN_STORAGE_KEY)).toBe("refresh-1");
    expect(window.localStorage.getItem("ntix.producer.refresh-token.v1")).toBeNull();
  });

  it("recusa com mensagem fixa no 401, sem revelar se a conta existe", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(401, [{ code: "INVALID_CREDENTIALS", message: "qualquer" }])));

    expect(await signInCustomer("ninguem@exemplo.com", "Senha@123")).toEqual({ ok: false, message: "E-mail, CPF ou senha incorretos." });
  });

  it("mostra a mensagem da API no limite de tentativas", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(429, [{ code: "TOO_MANY_LOGIN_ATTEMPTS", message: "Muitas tentativas." }])));

    expect(await signInCustomer("marina@exemplo.com", "x")).toEqual({ ok: false, message: "Muitas tentativas." });
  });

  it("cadastra e entra em seguida com os mesmos dados", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(200, [{ code: "CREATE_CUSTOMER_SUCCESS", message: "ok" }]))
      .mockResolvedValueOnce(jsonResponse(200, SESSION))
      .mockResolvedValueOnce(jsonResponse(200, ME));
    vi.stubGlobal("fetch", fetchMock);

    const result = await signUpCustomer("Marina Souza", "marina@exemplo.com", "529.982.247-25", "Senha@123");

    expect(result.ok).toBe(true);
    expect(sent(fetchMock, 0).body).toMatchObject({ fullName: "Marina Souza", document: "52998224725" });
    expect(sent(fetchMock, 1).body).toEqual({ login: "marina@exemplo.com", password: "Senha@123" });
  });

  it("aponta o campo da recusa do cadastro", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(400, [{ code: "EMAIL_ALREADY_EXISTS", message: "E-mail já cadastrado." }])));

    expect(await signUpCustomer("Marina", "marina@exemplo.com", "52998224725", "Senha@123")).toEqual({ ok: false, message: "E-mail já cadastrado.", field: "email" });
  });

  it("renova uma vez no 401 e repete a chamada", async () => {
    storeCustomerTokens({ accessToken: "velho", refreshToken: "refresh-1" });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, undefined))
      .mockResolvedValueOnce(jsonResponse(200, { ...SESSION, accessToken: "novo", refreshToken: "refresh-2" }))
      .mockResolvedValueOnce(jsonResponse(200, ME));
    vi.stubGlobal("fetch", fetchMock);

    const result = await customerAuthorizedRequest("GET", "/api/v1/customers/me");

    expect(result.ok).toBe(true);
    expect(sent(fetchMock, 1).url).toMatch(/\/sessions\/refresh$/);
    expect(sent(fetchMock, 2).headers.Authorization).toBe("Bearer novo");
    expect(readCustomerRefreshToken()).toBe("refresh-2");
  });

  it("sai apagando os tokens e revogando na API", async () => {
    storeCustomerTokens({ accessToken: "a", refreshToken: "refresh-1" });
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(204, undefined));
    vi.stubGlobal("fetch", fetchMock);

    await signOutCustomer();

    expect(getCustomerAccessToken()).toBeUndefined();
    expect(readCustomerRefreshToken()).toBeNull();
    expect(sent(fetchMock, 0).body).toEqual({ refreshToken: "refresh-1" });
  });
});

function LoggedInName() {
  const { customer } = useCustomer();
  return <p>{customer.loggedIn ? `Conectada: ${customer.account?.name}` : "Desconectada"}</p>;
}

describe("formulário do comprador com a API real", () => {
  it("entra com CPF e senha conferidos pela API", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(jsonResponse(200, SESSION)).mockResolvedValueOnce(jsonResponse(200, ME)));
    const user = userEvent.setup();

    render(
      <CustomerProvider initial={{}}>
        <AuthForm />
        <LoggedInName />
      </CustomerProvider>,
    );

    await user.click(screen.getByLabelText("Já tenho conta"));
    await user.type(screen.getByLabelText("E-mail ou CPF"), "529.982.247-25");
    await user.type(screen.getByLabelText("Senha"), "Senha@123");
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    expect(await screen.findByText("Conectada: Marina Souza")).toBeInTheDocument();
  });

  it("mostra a recusa da API", async () => {
    window.sessionStorage.setItem("ntix:api-mode", "real");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(401, [{ code: "INVALID_CREDENTIALS", message: "x" }])));
    const user = userEvent.setup();

    render(
      <CustomerProvider initial={{}}>
        <AuthForm />
        <LoggedInName />
      </CustomerProvider>,
    );

    await user.click(screen.getByLabelText("Já tenho conta"));
    await user.type(screen.getByLabelText("E-mail ou CPF"), "marina@exemplo.com");
    await user.type(screen.getByLabelText("Senha"), "Senha@errada1");
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("E-mail, CPF ou senha incorretos.");
    expect(screen.getByText("Desconectada")).toBeInTheDocument();
  });
});
