import type { ProducerType } from "../../producer/session";

export type SignupValues = {
  producerType: ProducerType;
  name: string;
  email: string;
  phone: string;
  adminName: string;
  adminEmail: string;
  adminPassword: string;
};

export type SignupField = "name" | "email" | "phone" | "adminName" | "adminEmail" | "adminPassword";
export type SignupErrors = Partial<Record<SignupField, string>>;

export const PRODUCER_FIELDS: readonly SignupField[] = ["name", "email", "phone"];
export const ADMIN_FIELDS: readonly SignupField[] = ["adminName", "adminEmail", "adminPassword"];

export const MIN_PASSWORD_LENGTH = 8;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
const EMAIL_MESSAGE = "Informe um e-mail válido, como voce@suaprodutora.com.";

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
    case "adminEmail":
      return EMAIL_PATTERN.test(values.adminEmail.trim()) ? undefined : EMAIL_MESSAGE;
    case "adminPassword":
      return values.adminPassword.length >= MIN_PASSWORD_LENGTH ? undefined : `A senha precisa ter pelo menos ${MIN_PASSWORD_LENGTH} caracteres.`;
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

/** Integração pendente: o endpoint de cadastro de produtor ainda não existe.
    Quando existir, esta função faz o POST com os headers de docs/rules/auditable-info.md
    (X-Request-Id, X-Correlation-Id, X-Source, X-Request-Timestamp) e o aviso de
    demonstração sai do SignupTicket. É aqui que a senha do administrador segue para o
    servidor: ela não entra na sessão nem fica guardada no navegador. */
export function submitProducerSignup(values: SignupValues): Promise<void> {
  void values;
  return new Promise((resolve) => setTimeout(resolve, 650));
}

/** Quem chega ao formulário por um link começa digitando. */
export function focusSignup() {
  document.getElementById("signup-name")?.focus({ preventScroll: true });
}
