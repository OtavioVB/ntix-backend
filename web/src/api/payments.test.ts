import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { FORBIDDEN_CODE, NETWORK_ERROR_CODE, UNAUTHORIZED_CODE } from "./client";
import { createPaymentIntegration, getPaymentProviders, getPaymentRoutes, replacePaymentRoute, updatePaymentIntegration } from "./payments";

function mockFetch(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function sentRequest(fetchMock: ReturnType<typeof vi.fn>) {
  const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
  return { url, init, headers: init.headers as Record<string, string>, body: init.body ? JSON.parse(init.body as string) : undefined };
}

beforeEach(() => window.sessionStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe("integrações de pagamento na API", () => {
  it("consulta o catálogo sem token", async () => {
    const fetchMock = mockFetch(200, [{ code: "ABACATEPAY", name: "AbacatePay", paymentMethods: ["PIX"], credentialKeys: [] }]);

    const result = await getPaymentProviders();

    const { url, init, headers } = sentRequest(fetchMock);
    expect(url).toMatch(/\/api\/v1\/payment-providers$/);
    expect(init.method).toBe("GET");
    expect(headers).not.toHaveProperty("Authorization");
    expect(headers["X-Origin-Service"]).toBe("ntix-web");
    expect(result.ok && result.data[0].code).toBe("ABACATEPAY");
  });

  it("conecta o fornecedor com o access token e sem X-Execution-User", async () => {
    const fetchMock = mockFetch(201, { id: "6b0f", providerCode: "ABACATEPAY", name: "Conta principal", status: "ACTIVE", credentials: [{ key: "API_KEY", isSecret: true, valueHint: "a91f" }] });

    const result = await createPaymentIntegration("produtora-x", { providerCode: "ABACATEPAY", name: "Conta principal", credentials: { API_KEY: "abc_dev_a91f" } }, "token-123");

    const { url, init, headers, body } = sentRequest(fetchMock);
    expect(url).toMatch(/\/api\/v1\/producers\/produtora-x\/payment-integrations$/);
    expect(init.method).toBe("POST");
    expect(headers.Authorization).toBe("Bearer token-123");
    expect(headers).not.toHaveProperty("X-Execution-User");
    expect(body).toEqual({ providerCode: "ABACATEPAY", name: "Conta principal", credentials: { API_KEY: "abc_dev_a91f" } });
    expect(result.ok && result.data.credentials[0].valueHint).toBe("a91f");
  });

  it("inativa pela alteração parcial", async () => {
    const fetchMock = mockFetch(200, { id: "6b0f", providerCode: "ABACATEPAY", name: "Conta principal", status: "INACTIVE", credentials: [] });

    await updatePaymentIntegration("produtora-x", "6b0f", { status: "INACTIVE" }, "token-123");

    const { url, init, body } = sentRequest(fetchMock);
    expect(url).toMatch(/\/payment-integrations\/6b0f$/);
    expect(init.method).toBe("PUT");
    expect(body).toEqual({ status: "INACTIVE" });
  });

  it("substitui a rota do método com a ordem como prioridade", async () => {
    const fetchMock = mockFetch(200, [{ priority: 1, integrationId: "b", name: "B", status: "ACTIVE" }, { priority: 2, integrationId: "a", name: "A", status: "ACTIVE" }]);

    const result = await replacePaymentRoute("produtora-x", "PIX", { integrationIds: ["b", "a"] }, "token-123");

    const { url, body } = sentRequest(fetchMock);
    expect(url).toMatch(/\/payment-routes\/PIX$/);
    expect(body).toEqual({ integrationIds: ["b", "a"] });
    expect(result.ok && result.data.map((item) => item.priority)).toEqual([1, 2]);
  });

  it("devolve as notificações da recusa", async () => {
    mockFetch(400, [{ code: "INTEGRATION_DUPLICATED_IN_ROUTE", message: "Uma integração só pode aparecer uma vez no mesmo método de pagamento." }]);

    const result = await replacePaymentRoute("produtora-x", "PIX", { integrationIds: ["a", "a"] }, "token-123");

    expect(result.ok).toBe(false);
    expect(!result.ok && result.notifications[0].code).toBe("INTEGRATION_DUPLICATED_IN_ROUTE");
  });

  it("transforma 401 e 403, que vêm sem corpo, em notificações próprias", async () => {
    mockFetch(401, undefined);
    const unauthorized = await getPaymentRoutes("produtora-x");
    expect(!unauthorized.ok && unauthorized.notifications[0].code).toBe(UNAUTHORIZED_CODE);

    mockFetch(403, undefined);
    const forbidden = await getPaymentRoutes("produtora-y", "token-de-outra");
    expect(!forbidden.ok && forbidden.notifications[0].code).toBe(FORBIDDEN_CODE);
  });

  it("transforma falha de rede em notificação", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    const result = await getPaymentProviders();

    expect(!result.ok && result.notifications[0].code).toBe(NETWORK_ERROR_CODE);
  });
});
