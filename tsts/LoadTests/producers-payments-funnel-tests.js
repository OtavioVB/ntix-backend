import http from "k6/http";
import { check } from "k6";
import { Counter } from "k6/metrics";

// Gera tráfego em cada ramo dos funis de conexão de fornecedor e de substituição de rota (REQ4) e imprime os totais
// para comparar com o dashboard "Ntix Funil de Integrações de Pagamento". O ramo de erro por exceção depende de parar o
// PostgreSQL durante a execução: veja docs/observabilidade.md.
//
// k6 run -e BASE_URL=http://localhost:8080 -e SLUG=produtora-x -e ACCESS_TOKEN=eyJ... tsts/LoadTests/producers-payments-funnel-tests.js
// O token é do usuário da produtora (REQ5); veja tsts/LoadTests/producers-payment-integrations-tests.js.

const BASE_URL = __ENV.BASE_URL;
const SLUG = __ENV.SLUG;
const ACCESS_TOKEN = __ENV.ACCESS_TOKEN;
const DURATION = __ENV.DURATION || "5m";
const RATE = Number(__ENV.RATE || 50);
const MIX_WINDOW_SECONDS = Number(__ENV.MIX_WINDOW_SECONDS || 30);

const MAX_ITERATIONS_PER_VU = 1000000;

const OUTCOME_CREATED = "created";
const OUTCOME_REPLACED = "replaced";
const OUTCOME_VALIDATION_FAILED = "validation_failed";
const OUTCOME_NAME_ALREADY_EXISTS = "name_already_exists";
const OUTCOME_ERROR = "error";
const OUTCOME_UNKNOWN = "unknown";

const funnelRequests = new Counter("funnel_requests");
const funnelCreated = new Counter("funnel_created");
const funnelReplaced = new Counter("funnel_replaced");
const funnelValidationFailed = new Counter("funnel_validation_failed");
const funnelNameAlreadyExists = new Counter("funnel_name_already_exists");
const funnelError = new Counter("funnel_error");
const funnelUnknownResponse = new Counter("funnel_unknown_response");
const funnelUnexpectedOutcome = new Counter("funnel_unexpected_outcome");

const COUNTERS_BY_OUTCOME = {
    [OUTCOME_CREATED]: funnelCreated,
    [OUTCOME_REPLACED]: funnelReplaced,
    [OUTCOME_VALIDATION_FAILED]: funnelValidationFailed,
    [OUTCOME_NAME_ALREADY_EXISTS]: funnelNameAlreadyExists,
    [OUTCOME_ERROR]: funnelError,
    [OUTCOME_UNKNOWN]: funnelUnknownResponse,
};

export const options = {
    setupTimeout: "1m",
    scenarios: {
        funnel_traffic: {
            executor: "constant-arrival-rate",
            exec: "funnelTraffic",
            rate: RATE,
            timeUnit: "1s",
            duration: DURATION,
            preAllocatedVUs: 20,
            maxVUs: 100,
        },
    },
    thresholds: {
        funnel_unknown_response: ["count==0"],
        funnel_unexpected_outcome: ["count==0"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildHeaders() {
    return {
        "Content-Type": "application/json",
        Authorization: `Bearer ${ACCESS_TOKEN}`,
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-funnel-tests",
        "X-Requested-At": new Date().toISOString(),
    };
}

function pickRandom(items) {
    return items[Math.floor(Math.random() * items.length)];
}

function classifyResponse(response, successStatus, successOutcome) {
    if (response.status === successStatus)
        return successOutcome;

    if (response.status === 500)
        return OUTCOME_ERROR;

    if (response.status === 409)
        return OUTCOME_NAME_ALREADY_EXISTS;

    return response.status === 400 ? OUTCOME_VALIDATION_FAILED : OUTCOME_UNKNOWN;
}

function record(response, outcome, expectedOutcome, funnel) {
    funnelRequests.add(1);
    COUNTERS_BY_OUTCOME[outcome].add(1);

    // Erro é aceito em qualquer ramo, porque aparece de propósito quando o banco é parado durante o teste.
    const isExpected = outcome === expectedOutcome || outcome === OUTCOME_ERROR;

    if (!isExpected && outcome !== OUTCOME_UNKNOWN)
        funnelUnexpectedOutcome.add(1);

    check(response, {
        [`[${funnel}][${expectedOutcome}] Desfecho esperado`]: () => isExpected,
    });
}

function connect(body, expectedOutcome) {
    const response = http.post(`${BASE_URL}/api/v1/producers/${SLUG}/payment-integrations`, JSON.stringify(body), { headers: buildHeaders() });

    record(response, classifyResponse(response, 201, OUTCOME_CREATED), expectedOutcome, "IntegrationFunnel");

    return response;
}

function replaceRoute(paymentMethod, integrationIds, expectedOutcome) {
    const response = http.put(`${BASE_URL}/api/v1/producers/${SLUG}/payment-routes/${paymentMethod}`, JSON.stringify({ integrationIds }), { headers: buildHeaders() });

    record(response, classifyResponse(response, 200, OUTCOME_REPLACED), expectedOutcome, "RouteFunnel");
}

function uniqueName(data) {
    return `Funil ${data.run} ${__VU * MAX_ITERATIONS_PER_VU + __ITER}`;
}

export function setup() {
    if (!BASE_URL || !SLUG || !ACCESS_TOKEN)
        throw new Error("Informe BASE_URL, SLUG e ACCESS_TOKEN: veja o cabeçalho de tsts/LoadTests/producers-payments-funnel-tests.js");

    const run = Date.now();
    const seedName = `Funil ${run} semente`;
    const seed = connect({ providerCode: "ABACATEPAY", name: seedName, credentials: { API_KEY: `abc_dev_funil_${run}` } }, OUTCOME_CREATED);

    if (seed.status !== 201)
        throw new Error(`Não foi possível criar a integração semente: ${seed.status} ${seed.body}`);

    return { run, seedName, seedId: seed.json("id"), startedAt: Date.now() };
}

// Mulberry32: os VUs não compartilham estado, então calculam os mesmos pesos a partir da execução e da janela.
function seededRandom(seed) {
    let state = seed | 0;

    return () => {
        state = (state + 0x6d2b79f5) | 0;

        let mixed = Math.imul(state ^ (state >>> 15), 1 | state);

        mixed = (mixed + Math.imul(mixed ^ (mixed >>> 7), 61 | mixed)) ^ mixed;

        return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
    };
}

function currentMix(data) {
    const window = Math.floor((Date.now() - data.startedAt) / (MIX_WINDOW_SECONDS * 1000));
    const random = seededRandom(data.run + window * 7919);

    return {
        created: 0.2 + random() * 0.5,
        integrationValidation: random() * 0.4,
        nameAlreadyExists: random() * 0.3,
        replaced: 0.2 + random() * 0.5,
        routeValidation: random() * 0.4,
    };
}

export function funnelTraffic(data) {
    const mix = currentMix(data);
    const branches = [
        [mix.created, () => connect({ providerCode: "ABACATEPAY", name: uniqueName(data), credentials: { API_KEY: `abc_dev_${data.run}` } }, OUTCOME_CREATED)],
        [mix.integrationValidation, () => stopsAtIntegrationValidation(data)],
        [mix.nameAlreadyExists, () => connect({ providerCode: "ABACATEPAY", name: data.seedName, credentials: { API_KEY: `abc_dev_${data.run}` } }, OUTCOME_NAME_ALREADY_EXISTS)],
        [mix.replaced, () => replaceRoute(pickRandom(["PIX", "BOLETO", "CREDIT_CARD"]), [data.seedId], OUTCOME_REPLACED)],
        [mix.routeValidation, () => stopsAtRouteValidation(data)],
    ];

    let draw = Math.random() * branches.reduce((total, [weight]) => total + weight, 0);

    for (const [weight, branch] of branches) {
        if (draw < weight)
            return branch();

        draw -= weight;
    }

    return branches[branches.length - 1][1]();
}

function stopsAtIntegrationValidation(data) {
    const name = uniqueName(data);

    const invalidCases = [
        () => connect({ providerCode: "ABACATEPAY", name, credentials: {} }, OUTCOME_VALIDATION_FAILED),
        () => connect({ providerCode: "ABACATEPAY", name, credentials: { API_KEY: "abc", SENHA: "123" } }, OUTCOME_VALIDATION_FAILED),
        () => connect({ providerCode: "ABACATEPAY", name, credentials: { API_KEY: " abc_dev_com_espaco" } }, OUTCOME_VALIDATION_FAILED),
        () => connect({ providerCode: "INEXISTENTE", name, credentials: { API_KEY: "abc_dev_123" } }, OUTCOME_VALIDATION_FAILED),
        () => connect({ providerCode: "ABACATEPAY", name: "", credentials: { API_KEY: "abc_dev_123" } }, OUTCOME_VALIDATION_FAILED),
    ];

    pickRandom(invalidCases)();
}

function stopsAtRouteValidation(data) {
    const invalidCases = [
        () => replaceRoute("PIX", [data.seedId, data.seedId], OUTCOME_VALIDATION_FAILED),
        () => replaceRoute("PIX", ["6b0f0000-0000-4000-8000-000000000000"], OUTCOME_VALIDATION_FAILED),
        () => replaceRoute("CARD", [data.seedId], OUTCOME_VALIDATION_FAILED),
    ];

    pickRandom(invalidCases)();
}

function count(data, metric) {
    return data.metrics[metric] ? data.metrics[metric].values.count : 0;
}

export function handleSummary(data) {
    const rows = [
        ["Solicitações", count(data, "funnel_requests")],
        ["Integrações criadas", count(data, "funnel_created")],
        ["Rotas substituídas", count(data, "funnel_replaced")],
        ["Pararam na validação", count(data, "funnel_validation_failed")],
        ["Nome de integração repetido", count(data, "funnel_name_already_exists")],
        ["Erros por exceção", count(data, "funnel_error")],
        ["Respostas não reconhecidas", count(data, "funnel_unknown_response")],
        ["Desfechos fora do esperado", count(data, "funnel_unexpected_outcome")],
    ];

    const lines = rows.map(([name, value]) => `  ${name.padEnd(32)} ${value}`);

    return {
        stdout: `\nFunil de integrações de pagamento: totais do k6, para comparar com o dashboard\n${lines.join("\n")}\n\n`,
    };
}
