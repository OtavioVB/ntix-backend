import http from "k6/http";
import { check } from "k6";
import { Counter } from "k6/metrics";

// Gera tráfego em cada ramo do funil de cadastro de cliente e imprime os totais para comparar com o dashboard.
// O ramo de erro por exceção depende de parar o SQL Server durante a execução: veja docs/observabilidade.md.

const BASE_URL = __ENV.BASE_URL;
const DURATION = __ENV.DURATION || "5m";
const RATE = Number(__ENV.RATE || 150);
const MIX_WINDOW_SECONDS = Number(__ENV.MIX_WINDOW_SECONDS || 30);

const CUSTOMERS_ROUTE = "/api/v1/customers";
const HOMONYM_FULL_NAME = "Cliente Homonimo Funil";
const HOMONYM_PERCENTAGE = 0.1;
const SEED_CUSTOMERS = 20;
const MAX_CPF_BASE = 1000000000;
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

function calculateCheckDigit(digits) {
    const sum = digits
        .split("")
        .reduce((total, digit, index) => total + Number(digit) * (digits.length + 1 - index), 0);

    const remainder = sum % 11;

    return remainder < 2 ? 0 : 11 - remainder;
}

function buildCpf(base) {
    let cpf = String(base).padStart(9, "0");

    cpf += calculateCheckDigit(cpf);
    cpf += calculateCheckDigit(cpf);

    return cpf;
}

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildUniqueCustomer(uniqueNumber) {
    return {
        fullName: `Cliente Funil ${uniqueNumber}`,
        email: `cliente.funil.${uniqueNumber}@ntix.com`,
        document: buildCpf(uniqueNumber),
    };
}

function nextUniqueNumber(data) {
    return (data.runOffset + __VU * MAX_ITERATIONS_PER_VU + __ITER) % MAX_CPF_BASE;
}

function pickRandom(items) {
    return items[Math.floor(Math.random() * items.length)];
}

function classifyResponse(response) {
    if (response.status === 200)
        return OUTCOME_CREATED;

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

    if (codes.includes("CUSTOMER_ALREADY_EXISTS"))
        return OUTCOME_ALREADY_EXISTS;

    if (codes.includes("CREATE_CUSTOMER_ERROR"))
        return OUTCOME_ERROR;

    return OUTCOME_VALIDATION_FAILED;
}

function register(customer, expectedOutcome, omittedHeader) {
    const headers = {
        "Content-Type": "application/json",
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-funnel-tests",
        "X-Execution-User": `k6-vu-${__VU}`,
        "X-Requested-At": new Date().toISOString(),
    };

    if (omittedHeader)
        delete headers[omittedHeader];

    const response = http.post(`${BASE_URL}${CUSTOMERS_ROUTE}`, JSON.stringify(customer), { headers });
    const outcome = classifyResponse(response);

    funnelRequests.add(1);
    COUNTERS_BY_OUTCOME[outcome].add(1);

    // Erro é aceito em qualquer cenário, porque aparece de propósito quando o banco é parado durante o teste.
    const isExpected = outcome === expectedOutcome || outcome === OUTCOME_ERROR;

    if (!isExpected && outcome !== OUTCOME_UNKNOWN)
        funnelUnexpectedOutcome.add(1);

    check(response, {
        [`[CustomerFunnel][${expectedOutcome}] Desfecho esperado`]: () => isExpected,
    });
}

export function setup() {
    if (!BASE_URL)
        throw new Error("Informe BASE_URL: k6 run -e BASE_URL=http://localhost:5001 tsts/LoadTests/customers-funnel-tests.js");

    const runOffset = Math.floor(Math.random() * MAX_CPF_BASE);
    const seedCustomers = [];

    // Os números abaixo de MAX_ITERATIONS_PER_VU a partir do offset não são usados pelos VUs, que começam em 1.
    for (let i = 0; i < SEED_CUSTOMERS; i++) {
        const customer = buildUniqueCustomer((runOffset + i) % MAX_CPF_BASE);

        register(customer, OUTCOME_CREATED);
        seedCustomers.push(customer);
    }

    return { runOffset, seedCustomers, startedAt: Date.now() };
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
        createsCustomer(data);
    else if (draw < mix.created + mix.validation)
        stopsAtValidation(data);
    else
        customerAlreadyExists(data);
}

function createsCustomer(data) {
    const customer = buildUniqueCustomer(nextUniqueNumber(data));

    if (Math.random() < HOMONYM_PERCENTAGE)
        customer.fullName = HOMONYM_FULL_NAME;

    register(customer, OUTCOME_CREATED);
}

function stopsAtValidation(data) {
    const customer = buildUniqueCustomer(nextUniqueNumber(data));

    const invalidCases = [
        () => register({ ...customer, fullName: "" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...customer, fullName: "!!!" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...customer, email: "email-invalido" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...customer, document: "11111111111" }, OUTCOME_VALIDATION_FAILED),
        () => register({ ...customer, document: "" }, OUTCOME_VALIDATION_FAILED),
        () => register(customer, OUTCOME_VALIDATION_FAILED, "X-Correlation-Id"),
        () => register(customer, OUTCOME_VALIDATION_FAILED, "X-Requested-At"),
    ];

    pickRandom(invalidCases)();
}

function customerAlreadyExists(data) {
    const seed = pickRandom(data.seedCustomers);
    const newCustomer = buildUniqueCustomer(nextUniqueNumber(data));

    const duplicateCases = [
        () => register({ ...newCustomer, email: seed.email }, OUTCOME_ALREADY_EXISTS),
        () => register({ ...newCustomer, email: seed.email.toUpperCase() }, OUTCOME_ALREADY_EXISTS),
        () => register({ ...newCustomer, document: seed.document }, OUTCOME_ALREADY_EXISTS),
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
        ["Contas criadas", count(data, "funnel_created")],
        ["Respostas não reconhecidas", count(data, "funnel_unknown_response")],
        ["Desfechos fora do esperado", count(data, "funnel_unexpected_outcome")],
    ];

    const lines = rows.map(([name, value]) => `  ${name.padEnd(32)} ${value}`);

    return {
        stdout: `\nFunil de cadastro: totais do k6, para comparar com o dashboard\n${lines.join("\n")}\n\n`,
    };
}
