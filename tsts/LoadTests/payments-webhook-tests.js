import http from "k6/http";
import { check } from "k6";
import { Rate, Trend } from "k6/metrics";

// Carga do webhook de pagamento (REQ8). Escrita, não executada: precisa da API no ar, de pedidos preparados com Pix pendente
// e do perfil definitivo do usuário. O perfil abaixo é o proposto no requisito e está "a confirmar".
// Variáveis: BASE_URL, WEBHOOK_SIGNATURE_KEY (chave pública de teste) e PREPARED_PAYMENTS (JSON: [{ "id": "pix_char_...", "eventId": "log_..." }]).

const BASE_URL = __ENV.BASE_URL;
const PREPARED_PAYMENTS = JSON.parse(__ENV.PREPARED_PAYMENTS || "[]");

const webhookFailRate = new Rate("payments_webhook_fail");
const webhookDuration = new Trend("payments_webhook_duration", true);

export const options = {
    scenarios: {
        payments_webhook: {
            executor: "constant-arrival-rate",
            rate: 50,
            timeUnit: "1s",
            duration: "2m",
            preAllocatedVUs: 60,
            maxVUs: 120,
        },
    },
    thresholds: {
        payments_webhook_duration: ["p(95)<500"], // a confirmar
        payments_webhook_fail: ["rate<0.01"], // a confirmar
    },
};

export default function () {
    const payment = PREPARED_PAYMENTS[Math.floor(Math.random() * PREPARED_PAYMENTS.length)];

    const body = JSON.stringify({
        id: payment.eventId,
        event: "transparent.completed",
        apiVersion: 2,
        devMode: true,
        data: { transparent: { id: payment.id, amount: payment.amountCents, status: "PAID" } },
    });

    const response = http.post(`${BASE_URL}/api/v1/payment-webhooks/abacatepay`, body, {
        headers: {
            "Content-Type": "application/json",
            "X-Webhook-Signature": payment.signature,
            "X-Webhook-Secret": payment.secret,
        },
    });

    webhookDuration.add(response.timings.duration);

    const ok = check(response, {
        "[PaymentsWebhook][Confirm] Status 200 ou 503": (r) => r.status === 200 || r.status === 503,
    });

    webhookFailRate.add(!ok);
}
