import http from "k6/http";
import { check } from "k6";
import { Rate, Trend } from "k6/metrics";

// Perfil moderado: até 50 VUs, p95 abaixo de 500 ms e menos de 1% de erros.
// A consulta lê e valida o arquivo a cada requisição (REQ3), então este teste mede o custo de disco e validação.
// k6 run -e BASE_URL=http://localhost:5001 [-e SLUG=ntix] tsts/LoadTests/producers-whitelabel-tests.js

const getWhitelabelFailRate = new Rate("get_whitelabel_fail");
const getWhitelabelDuration = new Trend("get_whitelabel_duration", true);

const BASE_URL = __ENV.BASE_URL;
const SLUG = __ENV.SLUG || "ntix";

export const options = {
    scenarios: {
        get_whitelabel: {
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
        get_whitelabel_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

export function setup() {
    if (!BASE_URL)
        throw new Error("Informe BASE_URL: k6 run -e BASE_URL=http://localhost:5001 tsts/LoadTests/producers-whitelabel-tests.js");
}

export default function () {
    // Consulta pública: sem X-Execution-User.
    const response = http.get(`${BASE_URL}/api/v1/producers/${SLUG}/whitelabel`, {
        headers: {
            "X-Correlation-Id": buildCorrelationId(),
            "X-Origin-Service": "k6-load-tests",
            "X-Requested-At": new Date().toISOString(),
        },
    });

    getWhitelabelDuration.add(response.timings.duration);

    const served = check(response, {
        "[GetWhitelabel][Request] Status 200": (r) => r.status === 200,
        "[GetWhitelabel][Response] Cache-Control público": (r) => (r.headers["Cache-Control"] || "").includes("max-age=300"),
        "[GetWhitelabel][Response] Cor primária presente": (r) => r.status === 200 && !!r.json("colors.primary.500"),
    });

    getWhitelabelFailRate.add(!served);
}
