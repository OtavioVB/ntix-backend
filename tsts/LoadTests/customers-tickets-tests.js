import http from "k6/http";
import { check } from "k6";
import { Rate, Trend } from "k6/metrics";

// Carga das listas do comprador (REQ13). Escrita, não executada: precisa da API no ar e de clientes com pedidos e ingressos.
// Perfil moderado proposto no requisito (50 VUs, p95 < 500 ms), "a confirmar". Variáveis: BASE_URL e CUSTOMER_TOKENS (JSON: ["token", ...]).

const BASE_URL = __ENV.BASE_URL;
const CUSTOMER_TOKENS = JSON.parse(__ENV.CUSTOMER_TOKENS || "[]");

const listFailRate = new Rate("customers_tickets_list_fail");
const listDuration = new Trend("customers_tickets_list_duration", true);

export const options = {
    scenarios: {
        customer_tickets: {
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
        customers_tickets_list_duration: ["p(95)<500"], // a confirmar
        customers_tickets_list_fail: ["rate<0.01"], // a confirmar
    },
};

export default function () {
    const token = CUSTOMER_TOKENS[Math.floor(Math.random() * CUSTOMER_TOKENS.length)];
    const headers = {
        Authorization: `Bearer ${token}`,
        "X-Correlation-Id": `${__VU}-${__ITER}-${Date.now()}`,
        "X-Origin-Service": "load-tests",
        "X-Requested-At": new Date().toISOString(),
    };

    const orders = http.get(`${BASE_URL}/api/v1/customers/me/orders?page=1&pageSize=20`, { headers });
    const tickets = http.get(`${BASE_URL}/api/v1/customers/me/tickets?when=UPCOMING&page=1&pageSize=20`, { headers });

    listDuration.add(orders.timings.duration);
    listDuration.add(tickets.timings.duration);

    const ok = check(orders, {
        "[CustomersTickets][Orders] Status 200": (r) => r.status === 200,
    }) && check(tickets, {
        "[CustomersTickets][Tickets] Status 200": (r) => r.status === 200,
    });

    listFailRate.add(!ok);
}
