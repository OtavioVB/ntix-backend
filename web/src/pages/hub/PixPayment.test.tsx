import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { clearCustomerTokens, storeCustomerTokens } from "../../api/customerAuth";
import { PixPayment } from "./PixPayment";

const CODE = "NX7K2QH9MP";

function jsonResponse(status: number, body: unknown) {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function payment(overrides: Record<string, unknown> = {}) {
  return {
    id: "p1", method: "PIX", status: "PENDING", amountCents: 24000, expiresAt: new Date(Date.now() + 13 * 60_000).toISOString(),
    pix: { copyPasteCode: "00020101021226950014br.gov.bcb.pix", qrCodeImage: "data:image/png;base64,iVBORw0KGgo=" },
    ...overrides,
  };
}

function order(overrides: Record<string, unknown> = {}) {
  return { code: CODE, status: "PENDING_PAYMENT", payment: payment(), ...overrides };
}

type Answers = {
  methods?: () => Response;
  pay?: () => Response;
  order?: () => Response;
};

/** Os três endpoints do pagamento, respondidos pela rota. */
function stubApi(answers: Answers = {}) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith("/payment-methods")) return (answers.methods ?? (() => jsonResponse(200, { methods: [{ method: "PIX" }] })))();
    if (url.endsWith("/payments") && init?.method === "POST") return (answers.pay ?? (() => jsonResponse(201, payment())))();
    if (url.endsWith(`/api/v1/orders/${CODE}`)) return (answers.order ?? (() => jsonResponse(200, order())))();

    return jsonResponse(404, undefined);
  });

  vi.stubGlobal("fetch", fetchMock);

  return fetchMock;
}

const callsTo = (fetchMock: ReturnType<typeof stubApi>, suffix: string) => fetchMock.mock.calls.filter(([url]) => String(url).endsWith(suffix));

function renderPix(onOrderExpired = vi.fn(), pollMs = 20) {
  render(
    <MemoryRouter>
      <PixPayment orderCode={CODE} totalCents={24000} onOrderExpired={onOrderExpired} pollMs={pollMs} />
    </MemoryRouter>,
  );

  return onOrderExpired;
}

async function generate(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: /Gerar Pix de/ }));
}

beforeEach(() => {
  window.localStorage.clear();
  storeCustomerTokens({ accessToken: "access-1", refreshToken: "refresh-1" });
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearCustomerTokens();
});

describe("pagamento com Pix na API real", () => {
  it("confere os métodos do pedido antes de oferecer o Pix", async () => {
    const fetchMock = stubApi();

    renderPix();

    expect(await screen.findByRole("button", { name: /Gerar Pix de .*240,00/ })).toBeInTheDocument();
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/api\/v1\/orders\/NX7K2QH9MP\/payment-methods$/);
  });

  it("avisa quando o evento não tem forma de pagamento disponível", async () => {
    stubApi({ methods: () => jsonResponse(200, { methods: [] }) });

    renderPix();

    expect(await screen.findByRole("alert")).toHaveTextContent("não tem forma de pagamento disponível");
    expect(screen.queryByRole("button", { name: /Gerar Pix de/ })).not.toBeInTheDocument();
  });

  it("segue para gerar o Pix, com o aviso da API, quando não consegue consultar os métodos", async () => {
    stubApi({ methods: () => jsonResponse(500, undefined) });

    renderPix();

    expect(await screen.findByRole("button", { name: /Gerar Pix de/ })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Não foi possível falar com a Ntix");
  });

  it("gera o Pix e mostra o QR Code, o copia e cola, o contador e o aviso de espera", async () => {
    const fetchMock = stubApi();
    const user = userEvent.setup();

    renderPix();
    await generate(user);

    expect(await screen.findByRole("img", { name: "QR Code do Pix" })).toHaveAttribute("src", "data:image/png;base64,iVBORw0KGgo=");
    expect(screen.getByLabelText("Pix copia e cola")).toHaveValue("00020101021226950014br.gov.bcb.pix");
    expect(screen.getByRole("timer").textContent).toMatch(/^1[23]:/);
    expect(screen.getByText(/Aguardando o pagamento de .*240,00/)).toBeInTheDocument();

    const [, init] = callsTo(fetchMock, "/payments")[0] as [string, RequestInit];

    expect(JSON.parse(init.body as string)).toEqual({ method: "PIX" });
  });

  it("gera o QR Code do copia e cola quando o servidor não manda uma imagem em data URL", async () => {
    stubApi({ pay: () => jsonResponse(201, payment({ pix: { copyPasteCode: "00020101-sem-imagem", qrCodeImage: "https://exemplo.com/qr.png" } })) });
    const user = userEvent.setup();

    renderPix();
    await generate(user);

    expect(await screen.findByLabelText("Pix copia e cola")).toHaveValue("00020101-sem-imagem");
    expect(document.querySelector("img.pix-qr")).toBeNull();
    expect(document.querySelector("svg title")?.textContent).toBe("QR Code do Pix");
  });

  it("copia o código para a área de transferência e avisa", async () => {
    stubApi();
    const user = userEvent.setup();

    renderPix();
    await generate(user);
    await user.click(await screen.findByRole("button", { name: "Copiar código Pix" }));

    expect(await navigator.clipboard.readText()).toBe("00020101021226950014br.gov.bcb.pix");
    expect(await screen.findByRole("button", { name: "Código copiado!" })).toBeInTheDocument();
  });

  it("orienta a copiar à mão quando o navegador nega a área de transferência", async () => {
    stubApi();
    const user = userEvent.setup();

    vi.spyOn(navigator.clipboard, "writeText").mockRejectedValue(new Error("negado"));

    renderPix();
    await generate(user);
    await user.click(await screen.findByRole("button", { name: "Copiar código Pix" }));

    expect(await screen.findByText(/copie manualmente/)).toBeInTheDocument();
  });

  it.each([
    [409, "ORDER_PAYMENT_WINDOW_TOO_SHORT", "A reserva está perto do fim e não comporta um novo Pix."],
    [503, "PAYMENT_PROVIDERS_UNAVAILABLE", "Não foi possível gerar o Pix agora."],
    [400, "PAYMENT_METHOD_UNAVAILABLE", "O método de pagamento não está disponível."],
  ])("mostra a mensagem da API e deixa tentar de novo no %i (%s)", async (status, code, message) => {
    stubApi({ pay: () => jsonResponse(status, [{ code, message }]) });
    const user = userEvent.setup();

    renderPix();
    await generate(user);

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(screen.getByRole("button", { name: /Gerar Pix de/ })).toBeEnabled();
  });

  it("volta ao evento quando a reserva venceu", async () => {
    stubApi({ pay: () => jsonResponse(409, [{ code: "ORDER_EXPIRED", message: "A reserva deste pedido venceu." }]) });
    const user = userEvent.setup();
    const onOrderExpired = renderPix();

    await generate(user);

    await waitFor(() => expect(onOrderExpired).toHaveBeenCalledTimes(1));
  });

  it("mostra a confirmação quando o pedido já estava pago", async () => {
    stubApi({ pay: () => jsonResponse(409, [{ code: "ORDER_ALREADY_PAID", message: "Este pedido já foi pago." }]) });
    const user = userEvent.setup();

    renderPix();
    await generate(user);

    expect(await screen.findByText(/Pagamento confirmado/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ver meus ingressos" })).toHaveAttribute("href", "/conta/pedidos");
  });

  it("volta a oferecer o botão se a API responder sem o Pix", async () => {
    stubApi({ pay: () => jsonResponse(200, payment({ pix: undefined })) });
    const user = userEvent.setup();

    renderPix();
    await generate(user);

    expect(await screen.findByRole("button", { name: /Gerar Pix de/ })).toBeInTheDocument();
  });

  it("acompanha o pedido enquanto espera e confirma quando ele é pago", async () => {
    let paid = false;
    const fetchMock = stubApi({ order: () => jsonResponse(200, order(paid ? { status: "PAID" } : {})) });
    const user = userEvent.setup();

    renderPix();
    await generate(user);
    await screen.findByLabelText("Pix copia e cola");
    await waitFor(() => expect(callsTo(fetchMock, `/api/v1/orders/${CODE}`).length).toBeGreaterThan(1));

    paid = true;

    expect(await screen.findByText(/Pagamento confirmado/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Pix copia e cola")).not.toBeInTheDocument();
  });

  it("volta ao evento quando a reserva do pedido acaba durante a espera", async () => {
    stubApi({ order: () => jsonResponse(200, order({ status: "EXPIRED" })) });
    const user = userEvent.setup();
    const onOrderExpired = renderPix();

    await generate(user);

    await waitFor(() => expect(onOrderExpired).toHaveBeenCalled());
  });

  it("oferece gerar outro Pix quando o Pix venceu e o pedido ainda vale", async () => {
    stubApi({ order: () => jsonResponse(200, order({ payment: payment({ status: "EXPIRED", pix: undefined }) })) });
    const user = userEvent.setup();

    renderPix();
    await generate(user);

    expect(await screen.findByText("O Pix venceu. Gere um novo para pagar.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Gerar Pix de/ })).toBeEnabled();
  });

  it("ignora uma consulta que falhou e continua esperando", async () => {
    let calls = 0;
    const fetchMock = stubApi({ order: () => (++calls === 1 ? jsonResponse(500, undefined) : jsonResponse(200, order())) });
    const user = userEvent.setup();

    renderPix();
    await generate(user);

    await waitFor(() => expect(callsTo(fetchMock, `/api/v1/orders/${CODE}`).length).toBeGreaterThan(2));
    expect(screen.getByLabelText("Pix copia e cola")).toBeInTheDocument();
  });

  it("para de consultar o pedido quando a tela é fechada", async () => {
    const fetchMock = stubApi();
    const user = userEvent.setup();

    const { unmount } = render(
      <MemoryRouter>
        <PixPayment orderCode={CODE} totalCents={24000} onOrderExpired={vi.fn()} pollMs={20} />
      </MemoryRouter>,
    );

    await generate(user);
    await waitFor(() => expect(callsTo(fetchMock, `/api/v1/orders/${CODE}`).length).toBeGreaterThan(0));

    unmount();

    const before = callsTo(fetchMock, `/api/v1/orders/${CODE}`).length;

    await new Promise((resolve) => setTimeout(resolve, 100));

    expect(callsTo(fetchMock, `/api/v1/orders/${CODE}`).length).toBe(before);
  });
});
