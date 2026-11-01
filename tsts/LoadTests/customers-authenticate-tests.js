import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

// Exige um cliente já cadastrado e vinculado ao produtor informado:
// k6 run -e BASE_URL=... -e CUSTOMER_EMAIL=... -e CUSTOMER_DOCUMENT=... -e CUSTOMER_PASSWORD=... -e PRODUCER_ID=... tsts/LoadTests/customers-authenticate-tests.js

const authenticateCustomerFailRate = new Rate("authenticate_customer_fail");

const BASE_URL = __ENV.BASE_URL;
const CUSTOMER_EMAIL = __ENV.CUSTOMER_EMAIL;
const CUSTOMER_DOCUMENT = __ENV.CUSTOMER_DOCUMENT;
const CUSTOMER_PASSWORD = __ENV.CUSTOMER_PASSWORD;
const PRODUCER_ID = __ENV.PRODUCER_ID;

const AUTHENTICATION_ROUTE = "/api/v1/customers/authentication";

export const options = {
    scenarios: {
        authenticate_customer: {
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
        authenticate_customer_fail: ["rate<0.01"],
    },
};

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

export default function () {
    const login = __ITER % 2 === 0 ? { email: CUSTOMER_EMAIL } : { document: CUSTOMER_DOCUMENT };

    const response = http.post(
        `${BASE_URL}${AUTHENTICATION_ROUTE}`,
        JSON.stringify({ ...login, password: CUSTOMER_PASSWORD, producerId: PRODUCER_ID }),
        {
            headers: {
                "Content-Type": "application/json",
                "X-Correlation-Id": buildCorrelationId(),
                "X-Origin-Service": "k6-load-tests",
                "X-Execution-User": `k6-vu-${__VU}`,
                "X-Requested-At": new Date().toISOString(),
            },
        });

    const authenticated = check(response, {
        "[AuthenticateCustomer][Request] Status 200": (r) => r.status === 200,
        "[AuthenticateCustomer][Request] Token recebido": (r) => r.status === 200 && Boolean(r.json("accessToken")),
    });

    authenticateCustomerFailRate.add(!authenticated);
}
