import { createCustomerSession, getCurrentCustomer, registerCustomer, storeCustomerTokens } from "../api/customerAuth";
import type { ApiNotification } from "../api/client";
import type { CustomerAccount } from "./session";

export type CustomerAuthResult = { ok: true; account: CustomerAccount } | { ok: false; message: string; field?: "name" | "email" | "document" | "password" };

const GENERIC_ERROR = "Não foi possível entrar agora. Tente de novo em instantes.";

function firstMessage(notifications: ApiNotification[]): string {
  return notifications[0]?.message ?? GENERIC_ERROR;
}

/** Campo do cadastro pelo prefixo do código da API (EMAIL_, CPF_, PASSWORD_, FULL_NAME_). */
function fieldOf(code: string): "name" | "email" | "document" | "password" | undefined {
  if (code.startsWith("EMAIL_")) return "email";
  if (code.startsWith("CPF_") || code.startsWith("DOCUMENT_")) return "document";
  if (code.startsWith("PASSWORD_")) return "password";
  if (code.startsWith("FULL_NAME_") || code.startsWith("FIRST_NAME_")) return "name";
  return undefined;
}

/**
 * Login real (REQ12): abre a sessão e lê "meus dados" para a conta da tela. O CPF fica ofuscado, como a API devolve.
 * A senha não fica guardada.
 */
export async function signInCustomer(login: string, password: string): Promise<CustomerAuthResult> {
  const session = await createCustomerSession(login.trim(), password);
  // 401 tem mensagem fixa (não diz se a conta existe); limite de tentativas e falha de rede usam a mensagem da API.
  if (!session.ok) return { ok: false, message: session.status === 401 ? "E-mail, CPF ou senha incorretos." : firstMessage(session.notifications) };

  storeCustomerTokens(session.data);

  const me = await getCurrentCustomer();
  if (!me.ok) return { ok: false, message: GENERIC_ERROR };

  return { ok: true, account: { name: me.data.fullName, email: me.data.email, document: me.data.document } };
}

/** Cadastro real (já existente) seguido do login automático com os mesmos dados (REQ12, Decisões, item 4). */
export async function signUpCustomer(fullName: string, email: string, document: string, password: string): Promise<CustomerAuthResult> {
  const created = await registerCustomer({ fullName: fullName.trim(), email: email.trim(), document: document.replace(/\D/g, ""), password });
  if (!created.ok) {
    const notification = created.notifications.find((item) => item.code !== "SUCCESS") ?? created.notifications[0];
    return { ok: false, message: notification?.message ?? GENERIC_ERROR, field: notification ? fieldOf(notification.code) : undefined };
  }

  return signInCustomer(email, password);
}
