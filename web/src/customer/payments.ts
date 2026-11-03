/* Validações locais de cartão. Número e código de segurança nunca são guardados: sem provedor de
   pagamento ligado, a compra é simulada e o pedido só registra a bandeira e os quatro últimos dígitos. */

/** Cartão de teste que simula recusa da operadora, para exercitar o caminho de erro. */
export const DECLINED_TEST_CARD = "4000000000000002";

export function onlyDigits(value: string): string {
  return value.replace(/\D/g, "");
}

export function isValidCardNumber(value: string): boolean {
  const digits = onlyDigits(value);
  if (digits.length < 13 || digits.length > 19) return false;
  let sum = 0;
  let double = false;
  for (let index = digits.length - 1; index >= 0; index--) {
    let digit = Number(digits[index]);
    if (double) {
      digit *= 2;
      if (digit > 9) digit -= 9;
    }
    sum += digit;
    double = !double;
  }
  return sum % 10 === 0;
}

export function cardBrand(value: string): string {
  const digits = onlyDigits(value);
  if (/^4/.test(digits)) return "Visa";
  if (/^(5[1-5]|2[2-7])/.test(digits)) return "Mastercard";
  if (/^3[47]/.test(digits)) return "American Express";
  if (/^(4011|4312|4389|5041|6277|6362|650)/.test(digits)) return "Elo";
  return "Cartão";
}

export function formatCardNumber(value: string): string {
  return onlyDigits(value).slice(0, 19).replace(/(\d{4})(?=\d)/g, "$1 ");
}

export function formatExpiry(value: string): string {
  const digits = onlyDigits(value).slice(0, 4);
  return digits.length > 2 ? `${digits.slice(0, 2)}/${digits.slice(2)}` : digits;
}

export function isValidExpiry(value: string, now: number): boolean {
  const match = /^(\d{2})\/(\d{2})$/.exec(value);
  if (!match) return false;
  const month = Number(match[1]);
  if (month < 1 || month > 12) return false;
  const lastDay = new Date(2000 + Number(match[2]), month, 1).getTime();
  return lastDay > now;
}

export function isValidCvv(value: string): boolean {
  return /^\d{3,4}$/.test(value);
}

/** Parcelas sem juros: valor de exemplo, enquanto a regra do provedor não existe. */
export const MAX_INSTALLMENTS = 3;
