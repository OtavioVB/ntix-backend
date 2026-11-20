import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

// Perfil moderado (REQ10, a confirmar): até 50 VUs, p95 abaixo de 500 ms e menos de 1% de erros nos lotes do evento.
// A publicação não entra na carga: acontece uma vez por evento.
//
// Os endpoints exigem o access token do usuário da produtora (REQ5), do login em POST /api/v1/producers/sessions.
// k6 run -e BASE_URL=http://localhost:8080 -e SLUG=produtora-x -e ACCESS_TOKEN=eyJ... tsts/LoadTests/producers-batches-tests.js

const createFailRate = new Rate("create_batch_fail");
const listFailRate = new Rate("list_batches_fail");
const updateFailRate = new Rate("update_batch_fail");

const BASE_URL = __ENV.BASE_URL;
const SLUG = __ENV.SLUG;
const ACCESS_TOKEN = __ENV.ACCESS_TOKEN;

const DAY_MS = 24 * 60 * 60 * 1000;

export const options = {
    setupTimeout: "1m",
    scenarios: {
        batches: {
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
        create_batch_fail: ["rate<0.01"],
        list_batches_fail: ["rate<0.01"],
        update_batch_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildHeaders() {
    return {
        "Content-Type": "application/json",
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-load-tests",
        "X-Requested-At": new Date().toISOString(),
        Authorization: `Bearer ${ACCESS_TOKEN}`,
    };
}

function buildBatch(name, status = "ACTIVE") {
    return JSON.stringify({
        name,
        priceCents: 5000 + Math.floor(Math.random() * 10000),
        quantity: 1000,
        salesStartsAt: new Date(Date.now() - DAY_MS).toISOString(),
        salesEndsAt: new Date(Date.now() + 50 * DAY_MS).toISOString(),
        status,
    });
}

// Um evento e um lote por execução, para as leituras e as edições concorrentes.
export function setup() {
    if (!BASE_URL || !SLUG || !ACCESS_TOKEN)
        throw new Error("Informe BASE_URL, SLUG e ACCESS_TOKEN: veja o cabeçalho de tsts/LoadTests/producers-batches-tests.js");

    const startsAt = new Date(Date.now() + 60 * DAY_MS);
    const event = http.post(`${BASE_URL}/api/v1/producers/${SLUG}/events`, JSON.stringify({
        title: `Carga de lotes ${Date.now()}`,
        category: "SHOWS",
        startsAt: startsAt.toISOString(),
        endsAt: new Date(startsAt.getTime() + 6 * 60 * 60 * 1000).toISOString(),
        venueName: "Arena de carga",
        address: "Rua do Teste, 100",
        city: "São Paulo",
        state: "SP",
        ageRating: "FREE",
    }), { headers: buildHeaders() });

    if (event.status !== 201)
        throw new Error(`Não foi possível criar o evento de carga: ${event.status} ${event.body}`);

    const eventId = event.json("id");
    const batch = http.post(`${BASE_URL}/api/v1/producers/${SLUG}/events/${eventId}/batches`, buildBatch("Lote fixo"), { headers: buildHeaders() });

    if (batch.status !== 201)
        throw new Error(`Não foi possível criar o lote de carga: ${batch.status} ${batch.body}`);

    return { eventId, batchId: batch.json("id") };
}

function batchesUrl(data) {
    return `${BASE_URL}/api/v1/producers/${SLUG}/events/${data.eventId}/batches`;
}

export default function (data) {
    const draw = Math.random();

    if (draw < 0.7) {
        const response = http.get(batchesUrl(data), { headers: buildHeaders() });
        listFailRate.add(!check(response, { "[GetTicketBatches][Request] Status 200": (r) => r.status === 200 }));
    } else if (draw < 0.9) {
        const status = Math.random() < 0.5 ? "ACTIVE" : "INACTIVE";
        const response = http.put(`${batchesUrl(data)}/${data.batchId}`, buildBatch("Lote fixo", status), { headers: buildHeaders() });
        updateFailRate.add(!check(response, { "[UpdateTicketBatch][Request] Status 200": (r) => r.status === 200 }));
    } else {
        const response = http.post(batchesUrl(data), buildBatch(`Lote ${__VU}-${__ITER}`), { headers: buildHeaders() });
        createFailRate.add(!check(response, { "[CreateTicketBatch][Request] Status 201": (r) => r.status === 201 }));
    }
}
