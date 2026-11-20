import http from "k6/http";
import { check, sleep } from "k6";
import exec from "k6/execution";
import { Counter, Rate, Trend } from "k6/metrics";

// Criação de pedidos (REQ6): a disputa por um mesmo lote, o pior caso da trava `FOR UPDATE` (RN8).
// Proposta a confirmar: 100 VUs disputando um lote de 1.000 ingressos, p95 abaixo de 800 ms e menos de 1% de erros.
// Recusa por estoque (409) é desfecho esperado da disputa e não conta como erro; erro é qualquer status que não seja 201 ou 409.
//
// Pré-requisito: um evento publicado com um lote em venda. A carga não cria produtora, evento nem lote: informe os do ambiente.
// O setup cadastra uma conta por VU e entra com cada uma, para o hash lento da senha não entrar na medição. O access token vale
// 15 minutos: a execução (4m30s) cabe nele.
//
// Ao fim, se PRODUCER_TOKEN, PRODUCER_SLUG e EVENT_ID forem informados, o teardown confere que vendidos + reservados do lote não
// passam da quantidade (a garantia de não vender acima do estoque).
//
// k6 run -e BASE_URL=http://localhost:8080 -e EVENT_SLUG=festival-horizonte -e BATCH_ID=<id do lote> \
//        [-e BATCH_QUANTITY=1000] [-e PRODUCER_SLUG=<slug> -e EVENT_ID=<id> -e PRODUCER_TOKEN=<access token>] tsts/LoadTests/orders-create-tests.js

const createOrderFailRate = new Rate("create_order_fail");
const createOrderDuration = new Trend("create_order_duration", true);
const ordersCreated = new Counter("orders_created");
const ordersRefusedForStock = new Counter("orders_refused_for_stock");

const BASE_URL = __ENV.BASE_URL;
const EVENT_SLUG = __ENV.EVENT_SLUG;
const BATCH_ID = __ENV.BATCH_ID;
const BATCH_QUANTITY = Number(__ENV.BATCH_QUANTITY || 1000);
const PRODUCER_SLUG = __ENV.PRODUCER_SLUG;
const EVENT_ID = __ENV.EVENT_ID;
const PRODUCER_TOKEN = __ENV.PRODUCER_TOKEN;

const VUS = 100;
const PASSWORD = "Carga@123";
const SETUP_BATCH_SIZE = 20;

export const options = {
    setupTimeout: "15m",
    scenarios: {
        create_order: {
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
        create_order_duration: ["p(95)<800"],
        create_order_fail: ["rate<0.01"],
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
        throw new Error("Informe BASE_URL, EVENT_SLUG e BATCH_ID: veja o cabeçalho de tsts/LoadTests/orders-create-tests.js");

    const run = Date.now();
    const accounts = Array.from({ length: VUS }, (_, index) => ({ email: `carga-pedidos-${run}-${index}@cliente.com.br` }));

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
    const body = JSON.stringify({ eventSlug: EVENT_SLUG, salesChannel: "HUB", items: [{ batchId: BATCH_ID, quantity: 1 }] });
    const response = http.post(`${BASE_URL}/api/v1/orders`, body, { headers: buildHeaders(token) });

    createOrderDuration.add(response.timings.duration);

    const ok = check(response, {
        "[CreateOrder][Request] Status 201 ou 409": (r) => r.status === 201 || r.status === 409,
        "[CreateOrder][Response] Pedido com código e prazo": (r) => r.status !== 201 || (Boolean(r.json("code")) && Boolean(r.json("expiresAt"))),
    });

    createOrderFailRate.add(!ok);

    if (response.status === 201)
        ordersCreated.add(1);
    else if (response.status === 409)
        ordersRefusedForStock.add(1);

    sleep(1);
}

// Garantia de não vender acima do estoque: vendidos + reservados nunca passam da quantidade do lote.
export function teardown() {
    if (!PRODUCER_TOKEN || !PRODUCER_SLUG || !EVENT_ID)
        return;

    const response = http.get(`${BASE_URL}/api/v1/producers/${PRODUCER_SLUG}/events/${EVENT_ID}/batches`, { headers: buildHeaders(PRODUCER_TOKEN) });

    if (response.status !== 200)
        throw new Error(`Não foi possível ler os lotes ao fim da carga: ${response.status} ${response.body}`);

    const batch = response.json().find((item) => item.id === BATCH_ID);

    if (!batch)
        throw new Error("O lote da carga não está na lista de lotes do evento.");

    if (batch.soldQuantity + batch.reservedQuantity > BATCH_QUANTITY)
        throw new Error(`Venda acima do estoque: ${batch.soldQuantity} vendidos + ${batch.reservedQuantity} reservados de ${BATCH_QUANTITY}.`);
}
