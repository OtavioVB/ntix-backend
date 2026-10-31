import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

const createCustomerFailRate = new Rate("create_customer_fail");

const BASE_URL = __ENV.BASE_URL;
const CUSTOMERS_ROUTE = "/api/v1/customers";

const HOMONYM_FULL_NAME = "Cliente Homonimo Carga";
const HOMONYM_PERCENTAGE = 0.05;
const MAX_CPF_BASE = 1000000000;
const MAX_ITERATIONS_PER_VU = 1000000;

export const options = {
    scenarios: {
        create_customer: {
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
        create_customer_fail: ["rate<0.01"],
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

// Cada execução começa em um ponto diferente para não repetir os dados da execução anterior no mesmo banco.
export function setup() {
    return { runOffset: Math.floor(Math.random() * MAX_CPF_BASE) };
}

export default function (data) {
    const uniqueNumber = (data.runOffset + __VU * MAX_ITERATIONS_PER_VU + __ITER) % MAX_CPF_BASE;

    const payload = JSON.stringify({
        fullName: Math.random() < HOMONYM_PERCENTAGE ? HOMONYM_FULL_NAME : `Cliente Carga ${uniqueNumber}`,
        email: `cliente.carga.${uniqueNumber}@ntix.com`,
        document: buildCpf(uniqueNumber),
    });

    const response = http.post(`${BASE_URL}${CUSTOMERS_ROUTE}`, payload, {
        headers: {
            "Content-Type": "application/json",
            "X-Correlation-Id": buildCorrelationId(),
            "X-Origin-Service": "k6-load-tests",
            "X-Execution-User": `k6-vu-${__VU}`,
            "X-Requested-At": new Date().toISOString(),
        },
    });

    const created = check(response, {
        "[CreateCustomer][Request] Status 200": (r) => r.status === 200,
    });

    createCustomerFailRate.add(!created);
}
