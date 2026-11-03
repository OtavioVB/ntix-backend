import http from "k6/http";
import { check } from "k6";
import { Counter } from "k6/metrics";

// Gera tráfego em cada ramo do funil de cadastro de produtora e imprime os totais para comparar com o dashboard
// "Ntix Funil de Cadastro de Produtora". O ramo de erro por exceção depende de parar o PostgreSQL durante a execução:
// veja docs/observabilidade.md.

const BASE_URL = __ENV.BASE_URL;
const DURATION = __ENV.DURATION || "5m";
const RATE = Number(__ENV.RATE || 150);
const MIX_WINDOW_SECONDS = Number(__ENV.MIX_WINDOW_SECONDS || 30);

const PRODUCERS_ROUTE = "/api/v1/producers";
const PASSWORD = "Senha@123";
const HOMONYM_NAME = "Produtora Homonima Funil";
const HOMONYM_PERCENTAGE = 0.1;
const SEED_PRODUCERS = 20;
const MAX_UNIQUE_NUMBER = 1000000000;
const MAX_ITERATIONS_PER_VU = 1000000;

const OUTCOME_CREATED = "created";
const OUTCOME_VALIDATION_FAILED = "validation_failed";
const OUTCOME_ALREADY_EXISTS = "already_exists";
const OUTCOME_ERROR = "error";
const OUTCOME_UNKNOWN = "unknown";

const funnelRequests = new Counter("funnel_requests");
const funnelCreated = new Counter("funnel_created");
const funnelValidationFailed = new Counter("funnel_validation_failed");
const funnelAlreadyExists = new Counter("funnel_already_exists");
const funnelError = new Counter("funnel_error");
const funnelUnknownResponse = new Counter("funnel_unknown_response");
const funnelUnexpectedOutcome = new Counter("funnel_unexpected_outcome");

const COUNTERS_BY_OUTCOME = {
    [OUTCOME_CREATED]: funnelCreated,
    [OUTCOME_VALIDATION_FAILED]: funnelValidationFailed,
    [OUTCOME_ALREADY_EXISTS]: funnelAlreadyExists,
    [OUTCOME_ERROR]: funnelError,
    [OUTCOME_UNKNOWN]: funnelUnknownResponse,
};

export const options = {
    setupTimeout: "2m",
    scenarios: {
        funnel_traffic: {
            executor: "constant-arrival-rate",
            exec: "funnelTraffic",
            rate: RATE,
            timeUnit: "1s",
            duration: DURATION,
            preAllocatedVUs: 50,
            maxVUs: 200,
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

function buildUniqueProducer(uniqueNumber) {
    return {
        name: `Produtora Funil ${uniqueNumber}`,
        email: `produtora.funil.${uniqueNumber}@ntix.com`,
        phone: "(11) 98765-4321",
        administratorName: `Administrador Funil ${uniqueNumber}`,
        administratorPassword: PASSWORD,
    };
}

function nextUniqueNumber(data) {
    return (data.runOffset + __VU * MAX_ITERATIONS_PER_VU + __ITER) % MAX_UNIQUE_NUMBER;
}

function pickRandom(items) {
    return items[Math.floor(Math.random() * items.length)];
}

function classifyResponse(response) {
    if (response.status === 200)
        return OUTCOME_CREATED;

    if (response.status === 500)
        return OUTCOME_ERROR;

    if (response.status !== 400)
        return OUTCOME_UNKNOWN;

    let notifications;

    try {
        notifications = response.json();
    } catch {
        return OUTCOME_UNKNOWN;
    }

    if (!Array.isArray(notifications) || notifications.length === 0)
        return OUTCOME_UNKNOWN;

    const codes = notifications.map((notification) => notification.code);

    if (codes.includes("PRODUCER_EMAIL_ALREADY_EXISTS"))
        return OUTCOME_ALREADY_EXISTS;

    return OUTCOME_VALIDATION_FAILED;
}

function register(producer, expectedOutcome, omittedHeader) {
    const headers = {
        "Content-Type": "application/json",
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-funnel-tests",
        "X-Requested-At": new Date().toISOString(),
    };

    if (omittedHeader)
        delete headers[omittedHeader];

    const response = http.post(`${BASE_URL}${PRODUCERS_ROUTE}`, JSON.stringify(producer), { headers });
    const outcome = classifyResponse(response);

    funnelRequests.add(1);
    COUNTERS_BY_OUTCOME[outcome].add(1);

    // Erro é aceito em qualquer cenário, porque aparece de propósito quando o banco é parado durante o teste.
    const isExpected = outcome === expectedOutcome || outcome === OUTCOME_ERROR;

    if (!isExpected && outcome !== OUTCOME_UNKNOWN)
        funnelUnexpectedOutcome.add(1);

    check(response, {
        [`[ProducerFunnel][${expectedOutcome}] Desfecho esperado`]: () => isExpected,
    });
}

export function setup() {
    if (!BASE_URL)
        throw new Error("Informe BASE_URL: k6 run -e BASE_URL=http://localhost:5001 tsts/LoadTests/producers-funnel-tests.js");

    const runOffset = Math.floor(Math.random() * MAX_UNIQUE_NUMBER);
    const seedProducers = [];

    // Os números abaixo de MAX_ITERATIONS_PER_VU a partir do offset não são usados pelos VUs, que começam em 1.
    for (let i = 0; i < SEED_PRODUCERS; i++) {
        const producer = buildUniqueProducer((runOffset + i) % MAX_UNIQUE_NUMBER);

        register(producer, OUTCOME_CREATED);
        seedProducers.push(producer);
    }

    return { runOffset, seedProducers, startedAt: Date.now() };
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
    const random = seededRandom(data.runOffset + window * 7919);

    return {
        created: 0.3 + random() * 0.7,
        validation: random() * 0.5,
        alreadyExists: random() * 0.5,
    };
}

export function funnelTraffic(data) {
    const mix = currentMix(data);
    const draw = Math.random() * (mix.created + mix.validation + mix.alreadyExists);

    if (draw < mix.created)
        createsProducer(data);
    else if (draw < mix.created + mix.validation)
        stopsAtValidation(data);
    else
        producerAlreadyExists(data);
}

function createsProducer(data) {
    const producer = buildUniqueProducer(nextUniqueNumber(data));

    if (Math.random() < HOMONYM_PERCENTAGE)
        producer.name = HOMONYM_NAME;

    register(producer, OUTCOME_CREATED);
}

function stopsAtValidation(data) {
    const producer = buildUniqueProducer(nextUniqueNumber(data));

    const invalidCases = [
        () => register({ ...producer, name: "" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...producer, name: "!!!" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...producer, email: "email-invalido" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...producer, phone: "" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...producer, phone: "98765-4321" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...producer, administratorName: "" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...producer, administratorPassword: "senha" }, OUTCOME_VALIDATION_FAILED),
        () => register(producer, OUTCOME_VALIDATION_FAILED, "X-Correlation-Id"),
        () => register(producer, OUTCOME_VALIDATION_FAILED, "X-Requested-At"),
    ];

    pickRandom(invalidCases)();
}

function producerAlreadyExists(data) {
    const seed = pickRandom(data.seedProducers);
    const newProducer = buildUniqueProducer(nextUniqueNumber(data));

    const duplicateCases = [
        () => register({ ...newProducer, email: seed.email }, OUTCOME_ALREADY_EXISTS),
        () => register({ ...newProducer, email: seed.email.toUpperCase() }, OUTCOME_ALREADY_EXISTS),
    ];

    pickRandom(duplicateCases)();
}

function count(data, metric) {
    return data.metrics[metric] ? data.metrics[metric].values.count : 0;
}

export function handleSummary(data) {
    const rows = [
        ["Solicitações de cadastro", count(data, "funnel_requests")],
        ["Pararam na validação inicial", count(data, "funnel_validation_failed")],
        ["Já existiam na base", count(data, "funnel_already_exists")],
        ["Erros por exceção", count(data, "funnel_error")],
        ["Produtoras criadas", count(data, "funnel_created")],
        ["Respostas não reconhecidas", count(data, "funnel_unknown_response")],
        ["Desfechos fora do esperado", count(data, "funnel_unexpected_outcome")],
    ];

    const lines = rows.map(([name, value]) => `  ${name.padEnd(32)} ${value}`);

    return {
        stdout: `\nFunil de cadastro de produtora: totais do k6, para comparar com o dashboard\n${lines.join("\n")}\n\n`,
    };
}
