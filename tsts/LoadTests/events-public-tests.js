import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

// Vitrine pública (REQ11): rota de maior tráfego em lançamento de evento. Proposta a confirmar: 200 VUs, p95 abaixo de
// 300 ms e menos de 1% de erros. Endpoints anônimos: não precisam de token.
// k6 run -e BASE_URL=http://localhost:8080 [-e PRODUCER=produtora-x] tsts/LoadTests/events-public-tests.js

const searchFailRate = new Rate("search_events_fail");
const detailFailRate = new Rate("get_public_event_fail");

const BASE_URL = __ENV.BASE_URL;
const PRODUCER = __ENV.PRODUCER;

const STATES = ["PE", "SP", "RJ", "MG", "BA"];
const CATEGORIES = ["SHOWS", "FESTIVALS", "THEATER", "STAND_UP"];

export const options = {
    scenarios: {
        showcase: {
            executor: "ramping-vus",
            startVUs: 0,
            stages: [
                { duration: "1m", target: 200 },
                { duration: "3m", target: 200 },
                { duration: "30s", target: 0 },
            ],
            gracefulRampDown: "30s",
        },
    },
    thresholds: {
        http_req_duration: ["p(95)<300"],
        search_events_fail: ["rate<0.01"],
        get_public_event_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildHeaders() {
    return {
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-load-tests",
        "X-Requested-At": new Date().toISOString(),
    };
}

function pick(values) {
    return values[Math.floor(Math.random() * values.length)];
}

// Os slugs da página do evento vêm da própria vitrine, para a carga usar eventos que existem no ambiente.
export function setup() {
    if (!BASE_URL)
        throw new Error("Informe BASE_URL: veja o cabeçalho de tsts/LoadTests/events-public-tests.js");

    const query = PRODUCER ? `?producer=${PRODUCER}&pageSize=50` : "?pageSize=50";
    const response = http.get(`${BASE_URL}/api/v1/events${query}`, { headers: buildHeaders() });

    if (response.status !== 200)
        throw new Error(`Não foi possível ler a vitrine: ${response.status} ${response.body}`);

    return { slugs: response.json("items").map((item) => item.slug) };
}

export default function (data) {
    if (data.slugs.length > 0 && Math.random() < 0.5) {
        const response = http.get(`${BASE_URL}/api/v1/events/${pick(data.slugs)}`, { headers: buildHeaders() });

        detailFailRate.add(!check(response, {
            "[GetPublicEvent][Request] Status 200": (r) => r.status === 200,
            "[GetPublicEvent][Response] Sem estoque exposto": (r) => r.status === 200 && !r.body.includes("availableQuantity"),
        }));

        return;
    }

    const filters = Math.random() < 0.5 ? `?state=${pick(STATES)}` : `?category=${pick(CATEGORIES)}`;
    const response = http.get(`${BASE_URL}/api/v1/events${filters}`, { headers: buildHeaders() });

    searchFailRate.add(!check(response, { "[SearchPublicEvents][Request] Status 200": (r) => r.status === 200 }));
}
