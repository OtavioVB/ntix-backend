import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiBaseUrl, auditableHeaders, postJson } from "./client";
import { getApiMode, isRealApi } from "./mode";
import { mapNotifications, submitProducerSignup, type SignupValues } from "../pages/producers/signup";

const VALUES: SignupValues = {
  producerType: "COMPANY",
  name: " Maré Alta ",
  email: "contato@exemplo.test",
  phone: "(11) 98765-4321",
  adminName: "Pessoa Administradora",
  adminPassword: "Senha@123",
};

function mockFetch(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

beforeEach(() => window.sessionStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe("modo da API", () => {
  it("começa em demonstração", () => {
    expect(getApiMode("")).toBe("demo");
  });

  it("liga a API real pela query e lembra na aba", () => {
    expect(isRealApi("?api=real")).toBe(true);
    expect(isRealApi("")).toBe(true);
  });

  it("volta para a demonstração pela query", () => {
    getApiMode("?api=real");
    expect(getApiMode("?api=demo")).toBe("demo");
    expect(getApiMode("")).toBe("demo");
  });

  it("ignora valores desconhecidos", () => {
    expect(getApiMode("?api=producao")).toBe("demo");
  });
});

describe("cliente da API", () => {
  it("aponta para a API local em localhost e para a de produção nos domínios", () => {
    expect(apiBaseUrl("localhost")).toBe("https://localhost:5000");
    expect(apiBaseUrl("produtores.ntix.com.br")).toBe("https://api.ntix.com.br");
  });

  it("envia os headers auditáveis no formato da API", () => {
    const headers = auditableHeaders();
    expect(headers["X-Correlation-Id"]).toMatch(/^[0-9a-f-]{36}$/);
    expect(headers["X-Origin-Service"]).toBe("ntix-web");
    expect(headers["X-Requested-At"]).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
    expect(headers).not.toHaveProperty("X-Execution-User");
  });

  it("transforma falha de rede em notificação", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
    const result = await postJson("/api/v1/producers", {});
    expect(result.ok).toBe(false);
    expect(result.notifications[0].code).toBe("NETWORK_ERROR");
  });

  it("transforma erro 500 sem corpo em notificação", async () => {
    mockFetch(500, null);
    const result = await postJson("/api/v1/producers", {});
    expect(result.notifications[0].code).toBe("NETWORK_ERROR");
  });
});

describe("cadastro de produtor na API", () => {
  it("não chama a API em demonstração", async () => {
    const fetchMock = mockFetch(200, []);
    vi.useFakeTimers();
    const pending = submitProducerSignup(VALUES);
    await vi.runAllTimersAsync();
    vi.useRealTimers();
    expect(await pending).toEqual({ status: "created", mode: "demo" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("faz o POST com o contrato do OpenAPI quando a API real está ligada", async () => {
    getApiMode("?api=real");
    const fetchMock = mockFetch(200, [{ code: "CREATE_PRODUCER_SUCCESS", message: "ok", type: 1 }]);

    const result = await submitProducerSignup(VALUES);

    expect(result).toEqual({ status: "created", mode: "real" });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("https://localhost:5000/api/v1/producers");
    expect(JSON.parse(init.body)).toEqual({
      name: "Maré Alta",
      email: "contato@exemplo.test",
      phone: "(11) 98765-4321",
      administratorName: "Pessoa Administradora",
      administratorPassword: "Senha@123",
    });
  });

  it("leva as recusas da API para os campos", async () => {
    getApiMode("?api=real");
    mockFetch(400, [{ code: "PRODUCER_EMAIL_ALREADY_EXISTS", message: "Já existe um cadastro de produtora com o e-mail informado." }]);

    const result = await submitProducerSignup(VALUES);

    expect(result).toEqual({ status: "rejected", errors: { email: "Já existe um cadastro de produtora com o e-mail informado." }, message: undefined });
  });

  it("mapeia cada código para o campo dele e o resto para a mensagem geral", () => {
    const result = mapNotifications([
      { code: "PRODUCER_NAME_COULD_NOT_BE_EMPTY", message: "nome" },
      { code: "SLUG_COULD_NOT_BE_GENERATED", message: "slug" },
      { code: "PHONE_FORMAT_INVALID", message: "telefone" },
      { code: "FULL_NAME_COULD_NOT_BE_EMPTY", message: "admin" },
      { code: "PASSWORD_WITHOUT_SYMBOL", message: "senha" },
      { code: "AUDITABLE_CORRELATION_ID_REQUIRED", message: "header" },
    ]);
    expect(result.errors).toEqual({ name: "nome", phone: "telefone", adminName: "admin", adminPassword: "senha" });
    expect(result.message).toBe("header");
  });
});
