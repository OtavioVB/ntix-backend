import http from "k6/http";
import { check, sleep } from "k6";
import exec from "k6/execution";
import { Counter, Rate, Trend } from "k6/metrics";

// Pagamento de pedidos com Pix (REQ7). Proposta a confirmar: 50 VUs, p95 do POST /payments abaixo de 1,5 s e menos de 1% de erros.
//
// A carga NÃO deve chamar o AbacatePay de verdade: aponte `AbacatePayConfiguration__BaseUrl` da API sob teste para um fornecedor
// falso que responda ao `POST /transparents/create` (o p95 mede a API, não a latência do fornecedor). Pré-requisito: um evento
// publicado, com lote em venda de estoque grande, de um produtor com integração AbacatePay na rota de PIX.
//
// Cada iteração cria um pedido de 1 ingresso (o pedido vence em 15 minutos e devolve o estoque), gera o Pix e pede de novo, o que
// devolve o mesmo Pix (200). O setup cadastra uma conta por VU e entra com cada uma, para o hash lento da senha não entrar na medição.
// O access token vale 15 minutos: a execução (4m30s) cabe nele.
//
// k6 run -e BASE_URL=http://localhost:8080 -e EVENT_SLUG=festival-horizonte -e BATCH_ID=<id do lote> tsts/LoadTests/orders-payments-tests.js

const createPixFailRate = new Rate("create_pix_fail");
const createPixDuration = new Trend("create_pix_duration", true);
const reusedPixFailRate = new Rate("reused_pix_fail");
const reusedPixDuration = new Trend("reused_pix_duration", true);
const orderSetupFailRate = new Rate("payment_order_setup_fail");
const pixCreated = new Counter("pix_created");

const BASE_URL = __ENV.BASE_URL;
const EVENT_SLUG = __ENV.EVENT_SLUG;
const BATCH_ID = __ENV.BATCH_ID;

const VUS = 50;
const PASSWORD = "Carga@123";
const SETUP_BATCH_SIZE = 20;

export const options = {
    setupTimeout: "15m",
    scenarios: {
        create_pix: {
            executor: "ramping-vus",
            startVUs: 0,
            stages: [
                { duration: "30s", target: VUS },
                { duration: "3m", target: VUS },
                { duration: "1m", target: 0 },
            ],
            gracefulRampDown: "30s",
        },
    },
    thresholds: {
        create_pix_duration: ["p(95)<1500"],
        create_pix_fail: ["rate<0.01"],
        reused_pix_duration: ["p(95)<500"],
        reused_pix_fail: ["rate<0.01"],
        payment_order_setup_fail: ["rate<0.01"],
    },
};

// CPF válido e aleatório: o documento é único por cliente.
function buildCpf() {
    const digits = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10));
    for (const length of [9, 10]) {
        const sum = digits.slice(0, length).reduce((total, digit, index) => total + digit * (length + 1 - index), 0);
        const rest = sum % 11;
        digits.push(rest < 2 ? 0 : 11 - rest);
    }
    return digits.join("");
}

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildHeaders(token) {
    const headers = {
        "Content-Type": "application/json",
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-load-tests",
        "X-Requested-At": new Date().toISOString(),
    };

    if (token)
        headers.Authorization = `Bearer ${token}`;

    return headers;
}

// Cadastra uma conta por VU, com e-mails únicos por execução, e entra com cada uma.
export function setup() {
    if (!BASE_URL || !EVENT_SLUG || !BATCH_ID)
        throw new Error("Informe BASE_URL, EVENT_SLUG e BATCH_ID: veja o cabeçalho de tsts/LoadTests/orders-payments-tests.js");

    const run = Date.now();
    const accounts = Array.from({ length: VUS }, (_, index) => ({ email: `carga-pagamentos-${run}-${index}@cliente.com.br` }));

    for (let start = 0; start < accounts.length; start += SETUP_BATCH_SIZE) {
        const batch = accounts.slice(start, start + SETUP_BATCH_SIZE).map((account, offset) => ["POST", `${BASE_URL}/api/v1/customers`, JSON.stringify({
            fullName: `Cliente de Carga ${start + offset}`,
            email: account.email,
            document: buildCpf(),
            password: PASSWORD,
        }), { headers: buildHeaders() }]);

        for (const response of http.batch(batch)) {
            if (response.status !== 200)
                throw new Error(`Não foi possível cadastrar a conta de carga: ${response.status} ${response.body}`);
        }
    }

    const tokens = accounts.map((account) => {
        const response = http.post(`${BASE_URL}/api/v1/customers/sessions`, JSON.stringify({ login: account.email, password: PASSWORD }), { headers: buildHeaders() });

        if (response.status !== 200)
            throw new Error(`Não foi possível entrar com a conta de carga: ${response.status} ${response.body}`);

        return response.json("accessToken");
    });

    return { tokens };
}

export default function (data) {
    const token = data.tokens[(exec.vu.idInTest - 1) % data.tokens.length];
    const orderResponse = http.post(
        `${BASE_URL}/api/v1/orders`,
        JSON.stringify({ eventSlug: EVENT_SLUG, salesChannel: "HUB", items: [{ batchId: BATCH_ID, quantity: 1 }] }),
        { headers: buildHeaders(token) },
    );

    const orderOk = check(orderResponse, { "[CreateOrder][Request] Status 201": (r) => r.status === 201 });

    orderSetupFailRate.add(!orderOk);

    if (!orderOk) {
        sleep(1);
        return;
    }

    const code = orderResponse.json("code");
    const body = JSON.stringify({ method: "PIX" });

    const created = http.post(`${BASE_URL}/api/v1/orders/${code}/payments`, body, { headers: buildHeaders(token) });

    createPixDuration.add(created.timings.duration);
    createPixFailRate.add(!check(created, {
        "[CreatePix][Request] Status 201": (r) => r.status === 201,
        "[CreatePix][Response] Pix com copia e cola e QR Code": (r) => r.status === 201 && Boolean(r.json("pix.copyPasteCode")) && Boolean(r.json("pix.qrCodeImage")),
    }));

    if (created.status === 201)
        pixCreated.add(1);

    // O segundo clique devolve o mesmo Pix, sem nova cobrança no fornecedor (RN4).
    const reused = http.post(`${BASE_URL}/api/v1/orders/${code}/payments`, body, { headers: buildHeaders(token) });

    reusedPixDuration.add(reused.timings.duration);
    reusedPixFailRate.add(!check(reused, {
        "[ReusePix][Request] Status 200": (r) => r.status === 200,
        "[ReusePix][Response] Mesmo Pix": (r) => r.status === 200 && created.status === 201 && r.json("id") === created.json("id"),
    }));

    sleep(1);
}
