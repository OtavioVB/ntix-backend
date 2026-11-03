import type { ProducerType } from "./session";

function hasValidCheckDigits(digits: string, weights: number[][]): boolean {
  return weights.every((series) => {
    const sum = series.reduce((total, weight, index) => total + Number(digits[index]) * weight, 0);
    const remainder = sum % 11;
    const expected = remainder < 2 ? 0 : 11 - remainder;
    return Number(digits[series.length]) === expected;
  });
}

const CPF_WEIGHTS = [
  [10, 9, 8, 7, 6, 5, 4, 3, 2],
  [11, 10, 9, 8, 7, 6, 5, 4, 3, 2],
];

const CNPJ_WEIGHTS = [
  [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2],
  [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2],
];

export function isValidDocument(type: ProducerType, value: string): boolean {
  const digits = value.replace(/\D/g, "");
  const length = type === "COMPANY" ? 14 : 11;
  if (digits.length !== length || /^(\d)\1+$/.test(digits)) return false;
  return hasValidCheckDigits(digits, type === "COMPANY" ? CNPJ_WEIGHTS : CPF_WEIGHTS);
}

export function formatDocument(type: ProducerType, value: string): string {
  const digits = value.replace(/\D/g, "");
  if (type === "COMPANY") {
    return digits
      .slice(0, 14)
      .replace(/^(\d{2})(\d)/, "$1.$2")
      .replace(/^(\d{2})\.(\d{3})(\d)/, "$1.$2.$3")
      .replace(/\.(\d{3})(\d)/, ".$1/$2")
      .replace(/(\d{4})(\d)/, "$1-$2");
  }
  return digits
    .slice(0, 11)
    .replace(/^(\d{3})(\d)/, "$1.$2")
    .replace(/^(\d{3})\.(\d{3})(\d)/, "$1.$2.$3")
    .replace(/\.(\d{3})(\d)/, ".$1-$2");
}
