import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

// Cada cadastro válido dispara um email. Rode com uma ResendConfiguration__ApiKey inválida:
// o envio falha sem custo e o cadastro continua, e o desfecho fica na métrica de emails de verificação.
// k6 run -e BASE_URL=... tsts/LoadTests/producers-create-tests.js

const createProducerFailRate = new Rate("create_producer_fail");

const BASE_URL = __ENV.BASE_URL;
const PRODUCERS_ROUTE = "/api/v1/producers";

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
        http_req_duration: ["p(95)<1000"],
        create_producer_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

export default function () {
    const uniqueId = buildCorrelationId();

    const response = http.post(
        `${BASE_URL}${PRODUCERS_ROUTE}`,
        JSON.stringify({
            name: `Produtora ${uniqueId}`,
            email: `${uniqueId}@carga.ntix.com`,
            phone: "+5511999999999",
            userName: "Administrador Carga",
            password: "Senha@123",
        }),
        {
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
