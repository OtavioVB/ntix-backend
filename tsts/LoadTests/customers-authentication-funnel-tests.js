import http from "k6/http";
import { check } from "k6";
import { Counter } from "k6/metrics";

// Gera tráfego em cada ramo do funil de autenticação e imprime os totais para comparar com o dashboard.
// Exige um cliente já cadastrado e vinculado a PRODUCER_ID, e OTHER_PRODUCER_ID sem vínculo com ele.
// O ramo de erro por exceção depende de parar o SQL Server durante a execução: veja docs/observabilidade.md.

const BASE_URL = __ENV.BASE_URL;
const CUSTOMER_EMAIL = __ENV.CUSTOMER_EMAIL;
const CUSTOMER_PASSWORD = __ENV.CUSTOMER_PASSWORD;
const PRODUCER_ID = __ENV.PRODUCER_ID;
const OTHER_PRODUCER_ID = __ENV.OTHER_PRODUCER_ID;
const DURATION = __ENV.DURATION || "5m";
const RATE = Number(__ENV.RATE || 20);
const MIX_WINDOW_SECONDS = Number(__ENV.MIX_WINDOW_SECONDS || 30);

const AUTHENTICATION_ROUTE = "/api/v1/customers/authentication";

const funnelRequests = new Counter("funnel_requests");
const funnelAuthenticated = new Counter("funnel_authenticated");
const funnelRefused = new Counter("funnel_refused");
const funnelValidationFailed = new Counter("funnel_validation_failed");
const funnelError = new Counter("funnel_error");
const funnelUnknownResponse = new Counter("funnel_unknown_response");

export const options = {
    scenarios: {
        funnel_traffic: {
            executor: "constant-arrival-rate",
            rate: RATE,
            timeUnit: "1s",
            duration: DURATION,
            preAllocatedVUs: 50,
            maxVUs: 200,
        },
    },
    thresholds: {
        funnel_unknown_response: ["count==0"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function pickRandom(items) {
    return items[Math.floor(Math.random() * items.length)];
}

function authenticate(payload) {
    const response = http.post(`${BASE_URL}${AUTHENTICATION_ROUTE}`, JSON.stringify(payload), {
        headers: {
            "Content-Type": "application/json",
            "X-Correlation-Id": buildCorrelationId(),
            "X-Origin-Service": "k6-funnel-tests",
            "X-Execution-User": `k6-vu-${__VU}`,
            "X-Requested-At": new Date().toISOString(),
        },
    });

    funnelRequests.add(1);

    if (response.status === 200)
        funnelAuthenticated.add(1);
    else if (response.status === 401)
        funnelRefused.add(1);
    else if (response.status === 400)
        funnelValidationFailed.add(1);
    else if (response.status === 500)
        funnelError.add(1);
    else
        funnelUnknownResponse.add(1);

    check(response, {
        "[AuthenticationFunnel][Request] Resposta reconhecida": (r) => [200, 400, 401, 500].includes(r.status),
    });
}

export function setup() {
    return { runOffset: Math.floor(Math.random() * 1000000), startedAt: Date.now() };
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
        authenticated: 0.3 + random() * 0.7,
        refused: random() * 0.5,
        validation: random() * 0.5,
    };
}

export default function (data) {
    const mix = currentMix(data);
    const draw = Math.random() * (mix.authenticated + mix.refused + mix.validation);
    const valid = { email: CUSTOMER_EMAIL, password: CUSTOMER_PASSWORD, producerId: PRODUCER_ID };

    if (draw < mix.authenticated) {
        authenticate(valid);
    } else if (draw < mix.authenticated + mix.refused) {
        authenticate(pickRandom([
            { ...valid, email: `sem.cadastro.${data.runOffset}@ntix.com` },
            { ...valid, password: "SenhaErrada@123" },
            { ...valid, producerId: OTHER_PRODUCER_ID },
        ]));
    } else {
        authenticate(pickRandom([
            { ...valid, email: "email-invalido" },
            { ...valid, email: null },
            { ...valid, document: "52998224725" },
            { ...valid, password: "" },
            { ...valid, producerId: null },
        ]));
    }
}

function count(data, metric) {
    return data.metrics[metric] ? data.metrics[metric].values.count : 0;
}

export function handleSummary(data) {
    const rows = [
        ["Solicitações de autenticação", count(data, "funnel_requests")],
        ["Clientes autenticados", count(data, "funnel_authenticated")],
        ["Recusadas (dados não reconhecidos)", count(data, "funnel_refused")],
        ["Pararam na validação inicial", count(data, "funnel_validation_failed")],
        ["Erros por exceção", count(data, "funnel_error")],
        ["Respostas não reconhecidas", count(data, "funnel_unknown_response")],
    ];

    const lines = rows.map(([name, value]) => `  ${name.padEnd(36)} ${value}`);

    return {
        stdout: `\nFunil de autenticação: totais do k6, para comparar com o dashboard\n${lines.join("\n")}\n\n`,
    };
}
