import http from "k6/http";
import { check } from "k6";
import { Rate, Trend } from "k6/metrics";

// Perfil moderado (REQ4, Decisões em aberto 5): até 50 VUs, p95 abaixo de 500 ms e menos de 1% de erros nos endpoints
// da aba Integrações. O ranqueamento não tem endpoint: o p95 < 50 ms dele é medido no REQ7, no caminho da cobrança.
//
// Os endpoints exigem o access token do usuário da produtora (REQ5): pegue o accessToken da resposta do login
// (POST /api/v1/producers/sessions). Ele vale 15 minutos, mais que a execução abaixo.
// k6 run -e BASE_URL=http://localhost:8080 -e SLUG=produtora-x -e ACCESS_TOKEN=eyJ... tsts/LoadTests/producers-payment-integrations-tests.js

const getProvidersFailRate = new Rate("get_payment_providers_fail");
const getIntegrationsFailRate = new Rate("get_payment_integrations_fail");
const getRoutesFailRate = new Rate("get_payment_routes_fail");
const replaceRouteFailRate = new Rate("replace_payment_route_fail");
const replaceRouteDuration = new Trend("replace_payment_route_duration", true);

const BASE_URL = __ENV.BASE_URL;
const SLUG = __ENV.SLUG;
const ACCESS_TOKEN = __ENV.ACCESS_TOKEN;

// Peso de cada requisição: a aba lê muito mais do que altera.
const READ_PERCENTAGE = 0.85;

export const options = {
    setupTimeout: "1m",
    scenarios: {
        payment_integrations_tab: {
            executor: "ramping-vus",
            startVUs: 0,
            stages: [
                { duration: "1m", target: 50 },
                { duration: "3m", target: 50 },
                { duration: "30s", target: 0 },
            ],
            gracefulRampDown: "30s",
        },
    },
    thresholds: {
        http_req_duration: ["p(95)<500"],
        get_payment_providers_fail: ["rate<0.01"],
        get_payment_integrations_fail: ["rate<0.01"],
        get_payment_routes_fail: ["rate<0.01"],
        replace_payment_route_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildHeaders(authenticated = true) {
    const headers = {
        "Content-Type": "application/json",
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-load-tests",
        "X-Requested-At": new Date().toISOString(),
    };

    if (authenticated)
        headers.Authorization = `Bearer ${ACCESS_TOKEN}`;

    return headers;
}

// Garante duas integrações ativas do AbacatePay para as rotas (nome por execução, para não colidir com execuções anteriores).
export function setup() {
    if (!BASE_URL || !SLUG || !ACCESS_TOKEN)
        throw new Error("Informe BASE_URL, SLUG e ACCESS_TOKEN: veja o cabeçalho de tsts/LoadTests/producers-payment-integrations-tests.js");

    const run = Date.now();
    const integrationIds = [];

    for (const name of [`Carga ${run} - principal`, `Carga ${run} - reserva`]) {
        const response = http.post(
            `${BASE_URL}/api/v1/producers/${SLUG}/payment-integrations`,
            JSON.stringify({ providerCode: "ABACATEPAY", name, credentials: { API_KEY: `abc_dev_carga_${run}` } }),
            { headers: buildHeaders() });

        if (response.status !== 201)
            throw new Error(`Não foi possível criar a integração de carga: ${response.status} ${response.body}`);

        integrationIds.push(response.json("id"));
    }

    return { integrationIds };
}

function getProviders() {
    const response = http.get(`${BASE_URL}/api/v1/payment-providers`, { headers: buildHeaders(false) });

    getProvidersFailRate.add(!check(response, {
        "[GetPaymentProviders][Request] Status 200": (r) => r.status === 200,
        "[GetPaymentProviders][Response] AbacatePay no catálogo": (r) => r.status === 200 && r.json().some((provider) => provider.code === "ABACATEPAY"),
    }));
}

function getIntegrations() {
    const response = http.get(`${BASE_URL}/api/v1/producers/${SLUG}/payment-integrations`, { headers: buildHeaders() });

    getIntegrationsFailRate.add(!check(response, {
        "[GetPaymentIntegrations][Request] Status 200": (r) => r.status === 200,
        "[GetPaymentIntegrations][Response] Sem chave secreta em claro": (r) => r.status === 200 && !r.body.includes("abc_dev_carga_"),
    }));
}

function getRoutes() {
    const response = http.get(`${BASE_URL}/api/v1/producers/${SLUG}/payment-routes`, { headers: buildHeaders() });

    getRoutesFailRate.add(!check(response, {
        "[GetPaymentRoutes][Request] Status 200": (r) => r.status === 200,
        "[GetPaymentRoutes][Response] Três métodos": (r) => r.status === 200 && ["PIX", "BOLETO", "CREDIT_CARD"].every((method) => Array.isArray(r.json(method))),
    }));
}

// Alterna a ordem das duas integrações no Pix: exercita a substituição atômica e a trava por método sob concorrência.
function replaceRoute(data) {
    const ids = Math.random() < 0.5 ? data.integrationIds : [...data.integrationIds].reverse();
    const response = http.put(
        `${BASE_URL}/api/v1/producers/${SLUG}/payment-routes/PIX`,
        JSON.stringify({ integrationIds: ids }),
        { headers: buildHeaders() });

    replaceRouteDuration.add(response.timings.duration);

    replaceRouteFailRate.add(!check(response, {
        "[ReplacePaymentRoute][Request] Status 200": (r) => r.status === 200,
        "[ReplacePaymentRoute][Response] Prioridades 1 e 2": (r) => r.status === 200 && r.json().map((item) => item.priority).join(",") === "1,2",
    }));
}

export default function (data) {
    if (Math.random() >= READ_PERCENTAGE) {
        replaceRoute(data);
        return;
    }

    const reads = [getProviders, getIntegrations, getRoutes];

    reads[Math.floor(Math.random() * reads.length)]();
}
