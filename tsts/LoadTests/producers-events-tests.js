import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

// Perfil moderado (REQ9, Decisões em aberto 6, a confirmar): até 50 VUs, p95 abaixo de 500 ms e menos de 1% de erros
// nos endpoints da aba Eventos do painel.
//
// Os endpoints exigem o access token do usuário da produtora (REQ5): pegue o accessToken da resposta do login
// (POST /api/v1/producers/sessions). Ele vale 15 minutos, mais que a execução abaixo.
// k6 run -e BASE_URL=http://localhost:8080 -e SLUG=produtora-x -e ACCESS_TOKEN=eyJ... tsts/LoadTests/producers-events-tests.js

const createFailRate = new Rate("create_event_fail");
const listFailRate = new Rate("list_events_fail");
const getFailRate = new Rate("get_event_fail");
const updateFailRate = new Rate("update_event_fail");

const BASE_URL = __ENV.BASE_URL;
const SLUG = __ENV.SLUG;
const ACCESS_TOKEN = __ENV.ACCESS_TOKEN;

export const options = {
    setupTimeout: "1m",
    scenarios: {
        events_tab: {
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
        create_event_fail: ["rate<0.01"],
        list_events_fail: ["rate<0.01"],
        get_event_fail: ["rate<0.01"],
        update_event_fail: ["rate<0.01"],
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

function buildEvent(title) {
    const startsAt = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000);
    const endsAt = new Date(startsAt.getTime() + 6 * 60 * 60 * 1000);

    return JSON.stringify({
        title,
        description: "Evento criado pelo teste de carga.",
        category: "SHOWS",
        startsAt: startsAt.toISOString(),
        endsAt: endsAt.toISOString(),
        venueName: "Arena de carga",
        address: "Rua do Teste, 100",
        city: "São Paulo",
        state: "SP",
        ageRating: "FREE",
    });
}

// Um evento fixo por execução, para as leituras e as edições concorrentes.
export function setup() {
    if (!BASE_URL || !SLUG || !ACCESS_TOKEN)
        throw new Error("Informe BASE_URL, SLUG e ACCESS_TOKEN: veja o cabeçalho de tsts/LoadTests/producers-events-tests.js");

    const response = http.post(`${BASE_URL}/api/v1/producers/${SLUG}/events`, buildEvent(`Carga ${Date.now()}`), { headers: buildHeaders() });

    if (response.status !== 201)
        throw new Error(`Não foi possível criar o evento de carga: ${response.status} ${response.body}`);

    return { eventId: response.json("id") };
}

function createEvent() {
    // Título repetido de propósito em parte das criações: exercita a versão aleatória do slug sob concorrência.
    const title = Math.random() < 0.3 ? "Show de carga" : `Show de carga ${__VU}-${__ITER}`;
    const response = http.post(`${BASE_URL}/api/v1/producers/${SLUG}/events`, buildEvent(title), { headers: buildHeaders() });

    createFailRate.add(!check(response, {
        "[CreateEvent][Request] Status 201": (r) => r.status === 201,
        "[CreateEvent][Response] Rascunho": (r) => r.status === 201 && r.json("status") === "DRAFT",
    }));
}

function listEvents() {
    const response = http.get(`${BASE_URL}/api/v1/producers/${SLUG}/events?status=DRAFT`, { headers: buildHeaders() });

    listFailRate.add(!check(response, { "[GetProducerEvents][Request] Status 200": (r) => r.status === 200 }));
}

function getEvent(data) {
    const response = http.get(`${BASE_URL}/api/v1/producers/${SLUG}/events/${data.eventId}`, { headers: buildHeaders() });

    getFailRate.add(!check(response, { "[GetProducerEvent][Request] Status 200": (r) => r.status === 200 }));
}

function updateEvent(data) {
    const response = http.put(`${BASE_URL}/api/v1/producers/${SLUG}/events/${data.eventId}`, buildEvent(`Carga editada ${__VU}`), { headers: buildHeaders() });

    updateFailRate.add(!check(response, { "[UpdateEvent][Request] Status 200": (r) => r.status === 200 }));
}

// A aba lê muito mais do que escreve.
export default function (data) {
    const draw = Math.random();

    if (draw < 0.5) listEvents();
    else if (draw < 0.8) getEvent(data);
    else if (draw < 0.9) updateEvent(data);
    else createEvent();
}
