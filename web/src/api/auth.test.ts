import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { authorizedRequest, clearSessionTokens, getAccessToken, readRefreshToken, REFRESH_TOKEN_STORAGE_KEY, signOut, storeSessionTokens, subscribeAuth, type AuthEvent } from "./auth";
import { createProducerSession, type ProducerSessionResponse } from "./sessions";
import { mapSignInNotifications, signInProducer, validateSignIn } from "../pages/login/signIn";

const SESSION: ProducerSessionResponse = {
  accessToken: "access-novo",
  accessTokenExpiresIn: 900,
  refreshToken: "refresh-novo",
  refreshTokenExpiresIn: 604800,
  user: { id: "u1", name: "Ana Souza", userType: "ADMINISTRATOR", status: "PENDING" },
  producer: { id: "p1", slug: "produtora-x", name: "Produtora X", status: "PENDING" },
};

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });

function calls(fetchMock: ReturnType<typeof vi.fn>) {
  return (fetchMock.mock.calls as [string, RequestInit][]).map(([url, init]) => ({
    url,
    headers: init.headers as Record<string, string>,
    body: init.body ? JSON.parse(init.body as string) : undefined,
  }));
}

beforeEach(() => {
  window.localStorage.clear();
  clearSessionTokens();
});
afterEach(() => vi.unstubAllGlobals());

describe("sessão do produtor na API", () => {
  it("faz o login com o contrato do OpenAPI, sem guardar a senha", async () => {
    const fetchMock = vi.fn().mockResolvedValue(json(200, SESSION));
    vi.stubGlobal("fetch", fetchMock);

    const result = await signInProducer(" ANA@Produtora.com.br ", "Senha@123");

    const [call] = calls(fetchMock);
    expect(call.url).toMatch(/\/api\/v1\/producers\/sessions$/);
    expect(call.body).toEqual({ email: "ana@produtora.com.br", password: "Senha@123" });
    expect(call.headers).not.toHaveProperty("Authorization");
    expect(call.headers).not.toHaveProperty("X-Execution-User");
    expect(result.status).toBe("signed-in");
    expect(result.status === "signed-in" && result.session).toMatchObject({ loggedIn: true, slug: "produtora-x", name: "Produtora X", adminEmail: "ana@produtora.com.br" });
    expect(getAccessToken()).toBe("access-novo");
    expect(readRefreshToken()).toBe("refresh-novo");
    expect(JSON.stringify({ ...window.localStorage })).not.toContain("Senha@123");
  });

  it("valida como a API antes de enviar: e-mail no formato e senha não vazia", () => {
    expect(validateSignIn("ana", "x").email).toBeDefined();
    expect(validateSignIn("ana@produtora.com.br", "").password).toBeDefined();
    expect(validateSignIn("ana@produtora.com.br", "senha")).toEqual({});
  });

  it("mostra a recusa da API: credenciais, bloqueio e campos", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(401, [{ code: "INVALID_CREDENTIALS", message: "E-mail ou senha incorretos." }])));

    const result = await signInProducer("ana@produtora.com.br", "Errada@123");

    expect(result).toEqual({ status: "rejected", errors: { general: "E-mail ou senha incorretos." } });
    expect(readRefreshToken()).toBeNull();
    expect(mapSignInNotifications([{ code: "EMAIL_FORMAT_INVALID", message: "O email informado não possui um formato válido." }]).email).toBeDefined();
    expect(mapSignInNotifications([{ code: "PRODUCER_USER_BLOCKED", message: "Este acesso está bloqueado." }]).general).toBe("Este acesso está bloqueado.");
  });

  it("diz em quanto tempo tentar de novo depois do limite de tentativas", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(429, [{ code: "TOO_MANY_LOGIN_ATTEMPTS", message: "Muitas tentativas." }], { "Retry-After": "540" })));

    const result = await createProducerSession({ email: "ana@produtora.com.br", password: "Senha@123" });

    expect(!result.ok && result.retryAfterSeconds).toBe(540);
    expect(!result.ok && mapSignInNotifications(result.notifications, result.retryAfterSeconds).general).toBe("Muitas tentativas de entrar com este e-mail. Tente de novo em 9 minutos.");
  });

  it("renova uma vez no 401 e repete a chamada com o token novo", async () => {
    storeSessionTokens({ accessToken: "access-vencido", refreshToken: "refresh-atual" });
    const events: AuthEvent[] = [];
    const unsubscribe = subscribeAuth((event) => events.push(event));
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(json(401, undefined))
      .mockResolvedValueOnce(json(200, SESSION))
      .mockResolvedValueOnce(json(200, [{ id: "i1" }]));
    vi.stubGlobal("fetch", fetchMock);

    const result = await authorizedRequest<{ id: string }[]>("GET", "/api/v1/producers/produtora-x/payment-integrations");
    unsubscribe();

    const [first, refresh, retry] = calls(fetchMock);
    expect(first.headers.Authorization).toBe("Bearer access-vencido");
    expect(refresh.url).toMatch(/\/sessions\/refresh$/);
    expect(refresh.body).toEqual({ refreshToken: "refresh-atual" });
    expect(retry.headers.Authorization).toBe("Bearer access-novo");
    expect(result.ok && result.data[0].id).toBe("i1");
    expect(readRefreshToken()).toBe("refresh-novo");
    expect(events).toEqual([{ type: "refreshed", session: SESSION }]);
  });

  it("renova uma única vez quando várias chamadas recebem 401 juntas", async () => {
    storeSessionTokens({ accessToken: "access-vencido", refreshToken: "refresh-atual" });
    const fetchMock = vi.fn((url: string, init: RequestInit) => {
      if (url.endsWith("/sessions/refresh")) return Promise.resolve(json(200, SESSION));
      const authorization = (init.headers as Record<string, string>).Authorization;
      return Promise.resolve(authorization === "Bearer access-novo" ? json(200, []) : json(401, undefined));
    });
    vi.stubGlobal("fetch", fetchMock);

    const results = await Promise.all([authorizedRequest("GET", "/a"), authorizedRequest("GET", "/b"), authorizedRequest("GET", "/c")]);

    expect(results.every((result) => result.ok)).toBe(true);
    expect(calls(fetchMock).filter((call) => call.url.endsWith("/sessions/refresh"))).toHaveLength(1);
  });

  it("encerra a sessão quando a renovação é recusada", async () => {
    storeSessionTokens({ accessToken: "access-vencido", refreshToken: "refresh-reusado" });
    const events: AuthEvent[] = [];
    const unsubscribe = subscribeAuth((event) => events.push(event));
    vi.stubGlobal(
      "fetch",
      vi.fn((url: string) => Promise.resolve(url.endsWith("/sessions/refresh") ? json(401, [{ code: "INVALID_REFRESH_TOKEN", message: "Sua sessão expirou." }]) : json(401, undefined))),
    );

    const result = await authorizedRequest("GET", "/api/v1/producers/produtora-x/payment-routes");
    unsubscribe();

    expect(result.ok).toBe(false);
    expect(events).toEqual([{ type: "expired" }]);
    expect(readRefreshToken()).toBeNull();
    expect(getAccessToken()).toBeUndefined();
  });

  it("mantém a sessão quando a renovação falha por rede", async () => {
    storeSessionTokens({ accessToken: "access-vencido", refreshToken: "refresh-atual" });
    vi.stubGlobal("fetch", vi.fn((url: string) => (url.endsWith("/sessions/refresh") ? Promise.reject(new TypeError("Failed to fetch")) : Promise.resolve(json(401, undefined)))));

    await authorizedRequest("GET", "/api/v1/producers/produtora-x/payment-routes");

    expect(readRefreshToken()).toBe("refresh-atual");
  });

  it("revoga a sessão na saída e apaga os tokens deste navegador", async () => {
    storeSessionTokens({ accessToken: "access", refreshToken: "refresh-atual" });
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await signOut();

    const [call] = calls(fetchMock);
    expect(call.url).toMatch(/\/api\/v1\/producers\/sessions\/revoke$/);
    expect(call.body).toEqual({ refreshToken: "refresh-atual" });
    expect(window.localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)).toBeNull();
    expect(getAccessToken()).toBeUndefined();
  });
});
