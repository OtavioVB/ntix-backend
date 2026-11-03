import { createProducer } from "../../api/producers";
import { isRealApi, type ApiMode } from "../../api/mode";
import type { ApiNotification } from "../../api/client";
import type { ProducerType } from "../../producer/session";

export type SignupValues = {
  producerType: ProducerType;
  name: string;
  /** E-mail da produtora e, também, o de acesso do administrador. */
  email: string;
  phone: string;
  adminName: string;
  adminPassword: string;
};

export type SignupField = "name" | "email" | "phone" | "adminName" | "adminPassword";
export type SignupErrors = Partial<Record<SignupField, string>>;

export const PRODUCER_FIELDS: readonly SignupField[] = ["name", "email", "phone"];
export const ADMIN_FIELDS: readonly SignupField[] = ["adminName", "adminPassword"];

export const MIN_PASSWORD_LENGTH = 8;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
const EMAIL_MESSAGE = "Informe um e-mail válido, como voce@suaprodutora.com.";

/** Mesma política de senha da API (PasswordValueObject): o servidor recusaria o que passasse daqui. */
function validatePassword(password: string): string | undefined {
  if (password.length < MIN_PASSWORD_LENGTH) return `A senha precisa ter pelo menos ${MIN_PASSWORD_LENGTH} caracteres.`;
  if (!/[A-Z]/.test(password)) return "A senha precisa ter uma letra maiúscula.";
  if (!/[a-z]/.test(password)) return "A senha precisa ter uma letra minúscula.";
  if (!/\d/.test(password)) return "A senha precisa ter um número.";
  if (!/[^\p{L}\p{N}\s]/u.test(password)) return "A senha precisa ter um símbolo, como @ ou #.";
  return undefined;
}

export function validateField(field: SignupField, values: SignupValues): string | undefined {
  switch (field) {
    case "name":
      if (values.name.trim().length >= 2) return undefined;
      return values.producerType === "COMPANY" ? "Informe o nome da produtora." : "Informe o seu nome.";
    case "email":
      return EMAIL_PATTERN.test(values.email.trim()) ? undefined : EMAIL_MESSAGE;
    case "phone": {
      const digits = values.phone.replace(/\D/g, "").length;
      return digits === 10 || digits === 11 ? undefined : "Informe o telefone com DDD.";
    }
    case "adminName":
      return values.adminName.trim().length >= 2 ? undefined : "Informe o nome do administrador.";
    case "adminPassword":
      return validatePassword(values.adminPassword);
  }
}

export function validateFields(fields: readonly SignupField[], values: SignupValues): SignupErrors {
  const errors: SignupErrors = {};
  for (const field of fields) {
    const message = validateField(field, values);
    if (message) errors[field] = message;
  }
  return errors;
}

export function formatPhone(value: string): string {
  const digits = value.replace(/\D/g, "").slice(0, 11);
  if (digits.length <= 2) return digits.length ? `(${digits}` : "";
  const area = `(${digits.slice(0, 2)}) `;
  const rest = digits.slice(2);
  const split = rest.length > 8 ? 5 : 4;
  return rest.length > 4 ? `${area}${rest.slice(0, split)}-${rest.slice(split)}` : area + rest;
}

export type SignupResult =
  | { status: "created"; mode: ApiMode }
  | { status: "rejected"; errors: SignupErrors; message?: string };

/** Prefixos dos códigos de notificação da API e o campo do formulário a que cada um pertence. */
const FIELD_BY_CODE_PREFIX: ReadonlyArray<readonly [string, SignupField]> = [
  ["PRODUCER_NAME_", "name"],
  ["SLUG_", "name"],
  ["PRODUCER_EMAIL_", "email"],
  ["EMAIL_", "email"],
  ["PHONE_", "phone"],
  ["FULL_NAME_", "adminName"],
  ["PASSWORD_", "adminPassword"],
];

export function mapNotifications(notifications: ApiNotification[]): Extract<SignupResult, { status: "rejected" }> {
  const errors: SignupErrors = {};
  const general: string[] = [];
  for (const notification of notifications) {
    const field = FIELD_BY_CODE_PREFIX.find(([prefix]) => notification.code.startsWith(prefix))?.[1];
    if (field) errors[field] ??= notification.message;
    else general.push(notification.message);
  }
  return { status: "rejected", errors, message: general.length ? general.join(" ") : undefined };
}

const DEMO_DELAY_MS = 650;

/**
 * Em demonstração (padrão), só simula a espera. Com `?api=real`, faz o POST /api/v1/producers.
 * A senha do administrador segue só para o servidor: não entra na sessão nem fica guardada no navegador.
 */
export async function submitProducerSignup(values: SignupValues): Promise<SignupResult> {
  if (!isRealApi()) {
    await new Promise((resolve) => setTimeout(resolve, DEMO_DELAY_MS));
    return { status: "created", mode: "demo" };
  }

  const result = await createProducer({
    name: values.name.trim(),
    email: values.email.trim(),
    phone: values.phone,
    administratorName: values.adminName.trim(),
    administratorPassword: values.adminPassword,
  });

  return result.ok ? { status: "created", mode: "real" } : mapNotifications(result.notifications);
}

/** Quem chega ao formulário por um link começa digitando. */
export function focusSignup() {
  document.getElementById("signup-name")?.focus({ preventScroll: true });
}
