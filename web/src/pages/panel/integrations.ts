import type { ApiNotification } from "../../api/client";
import type { PaymentMethod, PaymentProvider, ProducerIntegration } from "../../api/payments";

/** Nome de cada método na tela. */
export const METHOD_LABELS: Record<PaymentMethod, string> = {
  PIX: "Pix",
  BOLETO: "Boleto",
  CREDIT_CARD: "Cartão de crédito",
};

/** Catálogo da demonstração: o mesmo do seed da API (data/payments/003-seed-payment-providers.sql). */
export const DEMO_PROVIDERS: PaymentProvider[] = [
  {
    code: "ABACATEPAY",
    name: "AbacatePay",
    paymentMethods: ["PIX", "BOLETO", "CREDIT_CARD"],
    credentialKeys: [
      { key: "API_KEY", isSecret: true, isRequired: true },
      { key: "WEBHOOK_SECRET", isSecret: true, isRequired: false },
    ],
  },
];

/** Rótulo de cada chave de credencial no formulário; chave sem rótulo aparece como vem do catálogo. */
export const CREDENTIAL_LABELS: Record<string, string> = {
  API_KEY: "Chave de API",
  WEBHOOK_SECRET: "Segredo do webhook",
  ACCOUNT_ID: "Id da conta",
};

export const INTEGRATION_NAME_MAX_LENGTH = 64;
export const CREDENTIAL_MAX_LENGTH = 2048;
const HINT_LENGTH = 4;

export type IntegrationForm = { providerCode: string; name: string; credentials: Record<string, string> };

export type IntegrationFormErrors = { name?: string; credentials: Record<string, string>; general?: string };

export function normalizeName(name: string): string {
  return name.trim().split(/\s+/).filter(Boolean).join(" ");
}

/** Mesmo final que a API mostra (`CredentialValueObject.GetObfuscatedValue`): até 4 caracteres, nunca mais da metade. */
export function credentialHint(value: string): string {
  const length = Math.min(HINT_LENGTH, Math.floor(value.length / 2));
  return length === 0 ? "" : value.slice(-length);
}

/** Validação local espelhando a API (RN4, RN5), para a pessoa não descobrir o erro só no servidor. */
export function validateIntegration(form: IntegrationForm, provider: PaymentProvider | undefined, existing: ProducerIntegration[]): IntegrationFormErrors {
  const errors: IntegrationFormErrors = { credentials: {} };
  const name = normalizeName(form.name);

  if (!provider) errors.general = "Escolha um fornecedor do catálogo.";

  if (name.length === 0 || name.length > INTEGRATION_NAME_MAX_LENGTH) errors.name = "O nome da integração deve ter de 1 a 64 caracteres.";
  else if (existing.some((integration) => integration.name === name)) errors.name = "Já existe uma integração com este nome.";

  for (const key of provider?.credentialKeys ?? []) {
    const value = form.credentials[key.key] ?? "";

    if (value.length === 0) {
      if (key.isRequired) errors.credentials[key.key] = "Esta credencial é obrigatória para este fornecedor.";
      continue;
    }

    if (value.length > CREDENTIAL_MAX_LENGTH || value.trim() !== value)
      errors.credentials[key.key] = "Cole a credencial sem espaço no início ou no fim, com até 2048 caracteres.";
  }

  return errors;
}

export function hasErrors(errors: IntegrationFormErrors): boolean {
  return Boolean(errors.name || errors.general || Object.keys(errors.credentials).length);
}

/** Credenciais preenchidas, só das chaves do catálogo: campo vazio de chave opcional não vai para a API. */
export function filledCredentials(form: IntegrationForm, provider: PaymentProvider): Record<string, string> {
  return Object.fromEntries(provider.credentialKeys.map(({ key }) => [key, form.credentials[key] ?? ""]).filter(([, value]) => value.length > 0));
}

/** Notificações da API para os campos do formulário: nome, credenciais (geral, porque a API não diz a chave) e o resto. */
export function mapIntegrationNotifications(notifications: ApiNotification[]): IntegrationFormErrors {
  const errors: IntegrationFormErrors = { credentials: {} };

  for (const notification of notifications) {
    if (notification.code.startsWith("INTEGRATION_NAME_")) errors.name ??= notification.message;
    else errors.general ??= notification.message;
  }

  return errors;
}

/** Integração da demonstração: a credencial secreta nunca é guardada, só o final dela, como a API devolve. */
export function buildDemoIntegration(form: IntegrationForm, provider: PaymentProvider): ProducerIntegration {
  return {
    id: crypto.randomUUID(),
    providerCode: provider.code,
    name: normalizeName(form.name),
    status: "ACTIVE",
    credentials: Object.entries(filledCredentials(form, provider)).map(([key, value]) => {
      const isSecret = provider.credentialKeys.find((credentialKey) => credentialKey.key === key)?.isSecret ?? true;
      return isSecret ? { key, isSecret, valueHint: credentialHint(value) } : { key, isSecret, value };
    }),
  };
}

/** Integrações que podem entrar na rota do método (RN7): o fornecedor precisa aceitá-lo. */
export function integrationsForMethod(method: PaymentMethod, integrations: ProducerIntegration[], providers: PaymentProvider[]): ProducerIntegration[] {
  return integrations.filter((integration) => providers.find((provider) => provider.code === integration.providerCode)?.paymentMethods.includes(method));
}

/** Move uma posição da lista de prioridades para cima (-1) ou para baixo (+1). */
export function move<T>(items: T[], index: number, direction: -1 | 1): T[] {
  const target = index + direction;
  if (target < 0 || target >= items.length) return items;
  const moved = [...items];
  [moved[index], moved[target]] = [moved[target], moved[index]];
  return moved;
}
