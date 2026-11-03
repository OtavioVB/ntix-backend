import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

// Perfil moderado: até 50 VUs, p95 abaixo de 500 ms e menos de 1% de erros.
// k6 run -e BASE_URL=http://localhost:5001 tsts/LoadTests/producers-create-tests.js

const createProducerFailRate = new Rate("create_producer_fail");

const BASE_URL = __ENV.BASE_URL;
const PRODUCERS_ROUTE = "/api/v1/producers";

const PASSWORD = "Senha@123";
const HOMONYM_NAME = "Produtora Homonima Carga";
const HOMONYM_PERCENTAGE = 0.05;
const MAX_UNIQUE_NUMBER = 1000000000;
const MAX_ITERATIONS_PER_VU = 1000000;

export const options = {
    scenarios: {
        create_producer: {
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
        create_producer_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

// Cada execução começa em um ponto diferente para não repetir os dados da execução anterior no mesmo banco.
export function setup() {
    if (!BASE_URL)
        throw new Error("Informe BASE_URL: k6 run -e BASE_URL=http://localhost:5001 tsts/LoadTests/producers-create-tests.js");

    return { runOffset: Math.floor(Math.random() * MAX_UNIQUE_NUMBER) };
}

export default function (data) {
    const uniqueNumber = (data.runOffset + __VU * MAX_ITERATIONS_PER_VU + __ITER) % MAX_UNIQUE_NUMBER;

    const payload = JSON.stringify({
        name: Math.random() < HOMONYM_PERCENTAGE ? HOMONYM_NAME : `Produtora Carga ${uniqueNumber}`,
        email: `produtora.carga.${uniqueNumber}@ntix.com`,
        phone: "(11) 98765-4321",
        administratorName: `Administrador Carga ${uniqueNumber}`,
        administratorPassword: PASSWORD,
    });

    // Cadastro público: sem X-Execution-User.
    const response = http.post(`${BASE_URL}${PRODUCERS_ROUTE}`, payload, {
        headers: {
            "Content-Type": "application/json",
            "X-Correlation-Id": buildCorrelationId(),
            "X-Origin-Service": "k6-load-tests",
            "X-Requested-At": new Date().toISOString(),
        },
    });

    const created = check(response, {
        "[CreateProducer][Request] Status 200": (r) => r.status === 200,
    });

    createProducerFailRate.add(!created);
}
