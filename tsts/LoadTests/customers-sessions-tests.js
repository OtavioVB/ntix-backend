import http from "k6/http";
import { check, sleep } from "k6";
import exec from "k6/execution";
import { Rate, Trend } from "k6/metrics";

// Sessão do cliente (REQ12), mesmo perfil do login do produtor (REQ5):
// - Login: o hash da senha leva centenas de milissegundos de propósito, então o perfil é próprio: 50 VUs, p95 < 1 s e
//   menos de 1% de erros.
// - Renovação: perfil moderado (até 50 VUs, p95 < 500 ms, menos de 1% de erros).
//
// O limite de 10 logins por e-mail a cada 15 minutos (REQ12, RN9) também vale na carga: o setup cadastra ACCOUNTS clientes
// e cada login usa a próxima conta em rodízio, para nenhuma passar de 10 tentativas na janela. Com os estágios abaixo
// (até 50 VUs, uma iteração por segundo), 1000 contas bastam; aumente ACCOUNTS se alongar a execução.
//
// k6 run -e BASE_URL=http://localhost:8080 [-e ACCOUNTS=1000] tsts/LoadTests/customers-sessions-tests.js

const loginFailRate = new Rate("customer_login_fail");
const loginDuration = new Trend("customer_login_duration", true);
const refreshFailRate = new Rate("customer_session_refresh_fail");
const refreshDuration = new Trend("customer_session_refresh_duration", true);

const BASE_URL = __ENV.BASE_URL;
const ACCOUNTS = Number(__ENV.ACCOUNTS || 1000);
const PASSWORD = "Carga@123";
const SETUP_BATCH_SIZE = 20;

// Uma conta por VU da renovação, separada das do login, para o rodízio do login não somar tentativas nelas.
const REFRESH_ACCOUNTS = 50;

const stages = [
    { duration: "1m", target: 50 },
    { duration: "2m", target: 50 },
    { duration: "30s", target: 0 },
];

export const options = {
    setupTimeout: "15m",
    scenarios: {
        login: {
            executor: "ramping-vus",
            exec: "login",
            startVUs: 0,
            stages,
            gracefulRampDown: "30s",
        },
        refresh: {
            executor: "ramping-vus",
            exec: "refresh",
            startVUs: 0,
            stages,
            gracefulRampDown: "30s",
        },
    },
    thresholds: {
        customer_login_duration: ["p(95)<1000"],
        customer_login_fail: ["rate<0.01"],
        customer_session_refresh_duration: ["p(95)<500"],
        customer_session_refresh_fail: ["rate<0.01"],
    },
};

// CPF válido e aleatório: o documento é único por cliente.
function buildCpf() {
    const digits = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10));
    for (const length of [9, 10]) {
        const sum = digits.slice(0, length).reduce((total, digit, index) => total + digit * (length + 1 - index), 0);
        const rest = sum % 11;
        digits.push(rest < 2 ? 0 : 11 - rest);
    }
    return digits.join("");
}

function buildCorrelationId() {
    return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(/x/g, () => Math.floor(Math.random() * 16).toString(16));
}

function buildHeaders() {
    return {
        "Content-Type": "application/json",
        "X-Correlation-Id": buildCorrelationId(),
        "X-Origin-Service": "k6-load-tests",
        "X-Requested-At": new Date().toISOString(),
    };
}

function postLogin(email) {
    return http.post(`${BASE_URL}/api/v1/customers/sessions`, JSON.stringify({ login: email, password: PASSWORD }), { headers: buildHeaders() });
}

// Cadastra as contas da execução em lotes, com e-mails únicos por execução.
export function setup() {
    if (!BASE_URL)
        throw new Error("Informe BASE_URL: veja o cabeçalho de tsts/LoadTests/customers-sessions-tests.js");

    const run = Date.now();
    const emails = [];

    for (let start = 0; start < ACCOUNTS; start += SETUP_BATCH_SIZE) {
        const batch = [];

        for (let index = start; index < Math.min(start + SETUP_BATCH_SIZE, ACCOUNTS); index++) {
            const email = `carga-${run}-${index}@cliente.com.br`;

            emails.push(email);
            batch.push(["POST", `${BASE_URL}/api/v1/customers`, JSON.stringify({
                fullName: `Cliente de Carga ${index}`,
                email,
                document: buildCpf(),
                password: PASSWORD,
            }), { headers: buildHeaders() }]);
        }

        for (const response of http.batch(batch)) {
            if (response.status !== 200)
                throw new Error(`Não foi possível cadastrar a conta de carga: ${response.status} ${response.body}`);
        }
    }

    return { emails };
}

export function login(data) {
    // Rodízio pelas contas, sem as últimas, que ficam para a renovação: no máximo ceil(iterações / contas) logins por e-mail.
    const email = data.emails[exec.scenario.iterationInTest % (data.emails.length - REFRESH_ACCOUNTS)];
    const response = postLogin(email);

    loginDuration.add(response.timings.duration);
    loginFailRate.add(!check(response, {
        "[CustomerLogin][Request] Status 200": (r) => r.status === 200,
        "[CustomerLogin][Response] Par de tokens": (r) => r.status === 200 && Boolean(r.json("accessToken")) && Boolean(r.json("refreshToken")),
    }));

    sleep(1);
}

// Cada VU entra uma vez e depois só renova, guardando o token mais novo: reapresentar um token já usado seria reuso
// (RN7) e encerraria a sessão.
let refreshToken = null;

export function refresh(data) {
    if (!refreshToken) {
        const email = data.emails[data.emails.length - 1 - (exec.vu.idInTest % REFRESH_ACCOUNTS)];
        const loginResponse = postLogin(email);

        if (loginResponse.status !== 200) {
            refreshFailRate.add(true);
            sleep(1);
            return;
        }

        refreshToken = loginResponse.json("refreshToken");
    }

    const response = http.post(`${BASE_URL}/api/v1/customers/sessions/refresh`, JSON.stringify({ refreshToken }), { headers: buildHeaders() });

    refreshDuration.add(response.timings.duration);

    const ok = check(response, {
        "[CustomerSessionRefresh][Request] Status 200": (r) => r.status === 200,
        "[CustomerSessionRefresh][Response] Token novo": (r) => r.status === 200 && r.json("refreshToken") !== refreshToken,
    });

    refreshFailRate.add(!ok);
    refreshToken = ok ? response.json("refreshToken") : null;

    sleep(1);
}
