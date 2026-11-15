import { storeSessionTokens } from "../../api/auth";
import type { ApiNotification } from "../../api/client";
import { createProducerSession, TOO_MANY_LOGIN_ATTEMPTS_CODE } from "../../api/sessions";
import { sessionFromLogin, type ProducerSession } from "../../producer/session";

export type SignInErrors = { email?: string; password?: string; general?: string };

export type SignInResult = { status: "signed-in"; session: Partial<ProducerSession> } | { status: "rejected"; errors: SignInErrors };

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Validação local, espelho da API: e-mail no formato e senha não vazia. A regra de força da senha é do cadastro, não
 * do login.
 */
export function validateSignIn(email: string, password: string): SignInErrors {
  const errors: SignInErrors = {};
  if (!EMAIL_PATTERN.test(email.trim())) errors.email = "Informe um e-mail válido.";
  if (!password) errors.password = "Informe a senha.";
  return errors;
}

function minutesText(seconds: number): string {
  const minutes = Math.max(1, Math.ceil(seconds / 60));
  return minutes === 1 ? "1 minuto" : `${minutes} minutos`;
}

/** `EMAIL_` vai para o e-mail, `PASSWORD_` para a senha; o resto (credenciais, bloqueio, limite) é mensagem geral. */
export function mapSignInNotifications(notifications: ApiNotification[], retryAfterSeconds?: number): SignInErrors {
  const errors: SignInErrors = {};
  const general: string[] = [];
  for (const notification of notifications) {
    if (notification.code.startsWith("EMAIL_")) errors.email ??= notification.message;
    else if (notification.code.startsWith("PASSWORD_")) errors.password ??= notification.message;
    else if (notification.code === TOO_MANY_LOGIN_ATTEMPTS_CODE && retryAfterSeconds)
      general.push(`Muitas tentativas de entrar com este e-mail. Tente de novo em ${minutesText(retryAfterSeconds)}.`);
    else general.push(notification.message);
  }
  if (general.length) errors.general = general.join(" ");
  return errors;
}

/** POST /api/v1/producers/sessions. A senha vai só no corpo da requisição; os tokens ficam em api/auth.ts. */
export async function signInProducer(email: string, password: string): Promise<SignInResult> {
  const errors = validateSignIn(email, password);
  if (errors.email || errors.password) return { status: "rejected", errors };

  const typedEmail = email.trim().toLowerCase();
  const result = await createProducerSession({ email: typedEmail, password });

  if (!result.ok) return { status: "rejected", errors: mapSignInNotifications(result.notifications, result.retryAfterSeconds) };

  storeSessionTokens(result.data);
  return { status: "signed-in", session: sessionFromLogin(result.data, typedEmail) };
}
