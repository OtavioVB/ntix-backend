import http from "k6/http";
import { check } from "k6";
import { Rate, Trend } from "k6/metrics";

// Vendas dos eventos do painel (REQ14), no perfil moderado: até 50 VUs, p95 abaixo de 500 ms e menos de 1% de erros.
// Lê a lista com o resumo de vendas (contadores) e as vendas por dia (orders.sale_entry, particionada por mês).
//
// Prepare a produtora com uns 50 eventos e muitas vendas pagas (o REQ14 propõe 100 mil linhas em sale_entry) antes de rodar.
// Os endpoints exigem o access token do usuário da produtora (REQ5), que vale 15 minutos, mais que a execução abaixo.
// k6 run -e BASE_URL=http://localhost:8080 -e SLUG=produtora-x -e ACCESS_TOKEN=eyJ... tsts/LoadTests/producers-events-sales-tests.js

const listFailRate = new Rate("list_events_with_sales_fail");
const dailyFailRate = new Rate("get_event_daily_sales_fail");
const listDuration = new Trend("list_events_with_sales_duration", true);
const dailyDuration = new Trend("get_event_daily_sales_duration", true);

const BASE_URL = __ENV.BASE_URL;
const SLUG = __ENV.SLUG;
const ACCESS_TOKEN = __ENV.ACCESS_TOKEN;

const STATUSES = ["", "DRAFT", "PUBLISHED", "ENDED"];
const PERIODS = [7, 30, 90];

export const options = {
    scenarios: {
        events_sales: {
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
        list_events_with_sales_fail: ["rate<0.01"],
        get_event_daily_sales_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildHeaders() {
    return {
        Authorization: `Bearer ${ACCESS_TOKEN}`,
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-load-tests",
        "X-Requested-At": new Date().toISOString(),
    };
}

function pick(values) {
    return values[Math.floor(Math.random() * values.length)];
}

function formatDate(date) {
    return date.toISOString().slice(0, 10);
}

// Os eventos vêm da própria lista, para a carga usar os que existem no ambiente.
export function setup() {
    if (!BASE_URL || !SLUG || !ACCESS_TOKEN)
        throw new Error("Informe BASE_URL, SLUG e ACCESS_TOKEN: veja o cabeçalho de tsts/LoadTests/producers-events-sales-tests.js");

    const response = http.get(`${BASE_URL}/api/v1/producers/${SLUG}/events`, { headers: buildHeaders() });

    if (response.status !== 200)
        throw new Error(`Não foi possível listar os eventos: ${response.status} ${response.body}`);

    return { eventIds: response.json().map((item) => item.id) };
}

export default function (data) {
    if (data.eventIds.length > 0 && Math.random() < 0.5) {
        const days = pick(PERIODS);
        const to = new Date();
        const from = new Date(to.getTime() - (days - 1) * 24 * 60 * 60 * 1000);
        const response = http.get(
            `${BASE_URL}/api/v1/producers/${SLUG}/events/${pick(data.eventIds)}/sales/daily?from=${formatDate(from)}&to=${formatDate(to)}`,
            { headers: buildHeaders() });

        const ok = check(response, {
            "[EventDailySales][Get] Status 200": (r) => r.status === 200,
            "[EventDailySales][Get] Todos os dias do período": (r) => r.status === 200 && r.json("days").length === days,
        });

        dailyFailRate.add(!ok);
        dailyDuration.add(response.timings.duration);
        return;
    }

    const status = pick(STATUSES);
    const query = status ? `?status=${status}` : "";
    const response = http.get(`${BASE_URL}/api/v1/producers/${SLUG}/events${query}`, { headers: buildHeaders() });

    const ok = check(response, {
        "[ProducerEvents][List] Status 200": (r) => r.status === 200,
        "[ProducerEvents][List] Resumo de vendas": (r) => r.status === 200 && r.json().every((item) => item.salesSummary !== undefined),
    });

    listFailRate.add(!ok);
    listDuration.add(response.timings.duration);
}
