import http from "k6/http";
import { Counter } from "k6/metrics";

// Valida o funil de cadastro de produtor, não a performance. O ramo "error" é provocado parando o banco durante a execução.
// Use uma ResendConfiguration__ApiKey inválida, para não enviar emails reais.
// k6 run -e BASE_URL=... tsts/LoadTests/producers-funnel-tests.js

const BASE_URL = __ENV.BASE_URL;
const PRODUCERS_ROUTE = "/api/v1/producers";
const WEIGHTS_WINDOW_MS = 30000;
const EXISTING_EMAIL = "funil-repetido@carga.ntix.com";

const outcomes = {
    created: new Counter("producer_funnel_created"),
    validation_failed: new Counter("producer_funnel_validation_failed"),
    already_exists: new Counter("producer_funnel_already_exists"),
    error: new Counter("producer_funnel_error"),
};

export const options = {
    scenarios: {
        producer_funnel: {
            executor: "constant-arrival-rate",
            rate: 50,
            timeUnit: "1s",
            duration: "5m",
            preAllocatedVUs: 100,
        },
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function seededRandom(seed) {
    const value = Math.sin(seed) * 10000;

    return value - Math.floor(value);
}

// Os pesos mudam a cada janela, nunca em proporções fixas.
function pickBranch() {
    const windowIndex = Math.floor(Date.now() / WEIGHTS_WINDOW_MS);
    const weights = {
        created: 50 + seededRandom(windowIndex + 1) * 30,
        validation_failed: 5 + seededRandom(windowIndex + 2) * 25,
        already_exists: 5 + seededRandom(windowIndex + 3) * 25,
    };
    const total = weights.created + weights.validation_failed + weights.already_exists;
    const drawn = Math.random() * total;

    if (drawn < weights.created)
        return "created";

    return drawn < weights.created + weights.validation_failed ? "validation_failed" : "already_exists";
}

function buildPayload(branch) {
    const uniqueId = buildCorrelationId();
    const payload = {
        name: `Produtora ${uniqueId}`,
        email: `${uniqueId}@carga.ntix.com`,
        phone: "+5511999999999",
        userName: "Administrador Carga",
        password: "Senha@123",
    };

    if (branch === "validation_failed")
        payload.phone = "11999999999";

    if (branch === "already_exists")
        payload.email = EXISTING_EMAIL;

    return payload;
}

export default function () {
    const response = http.post(`${BASE_URL}${PRODUCERS_ROUTE}`, JSON.stringify(buildPayload(pickBranch())), {
        headers: {
            "Content-Type": "application/json",
            "X-Correlation-Id": buildCorrelationId(),
            "X-Origin-Service": "k6-load-tests",
            "X-Requested-At": new Date().toISOString(),
        },
    });

    if (response.status === 200)
        outcomes.created.add(1);
    else if (response.status >= 500 || response.status === 0)
        outcomes.error.add(1);
    else if (response.status === 400 && response.body.includes("PRODUCER_ALREADY_EXISTS"))
        outcomes.already_exists.add(1);
    else
        outcomes.validation_failed.add(1);
}

export function handleSummary(data) {
    const lines = Object.keys(outcomes).map((outcome) => {
        const metric = data.metrics[`producer_funnel_${outcome}`];

        return `${outcome}: ${metric ? metric.values.count : 0}`;
    });

    return { stdout: `\nFunil de cadastro de produtor\n${lines.join("\n")}\n` };
}
