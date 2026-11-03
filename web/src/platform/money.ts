import type { FeeTier } from "./types";

export function formatCents(cents: number): string {
  return (cents / 100).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

/** Com sinal explícito, para lançamentos. */
export function formatSigned(cents: number): string {
  const value = (Math.abs(cents) / 100).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return `${cents < 0 ? "−" : "+"} ${value}`;
}

/** Aceita "1.234,50", "1234,5" ou "1234". Devolve null se não for um valor. */
export function parseCents(text: string): number | null {
  const normalized = text.trim().replace(/\s|R\$/g, "").replace(/\./g, "").replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  return Math.round(Number(normalized) * 100);
}

export function describeTier(tier: FeeTier, previous?: FeeTier): string {
  const value = tier.type === "FIXED" ? `fixa de ${formatCents(tier.value)}` : `${tier.value.toLocaleString("pt-BR")}%`;
  if (tier.upToCents !== null) return `Até ${formatCents(tier.upToCents)}: ${value}`;
  return previous?.upToCents ? `Acima de ${formatCents(previous.upToCents)}: ${value}` : `${value} sobre o ingresso`;
}

/** Premissa P-46: a faixa em que o valor se enquadra vale para o valor inteiro. */
export function applyTiers(tiers: FeeTier[], baseCents: number): { amountCents: number; rule: string } {
  const index = tiers.findIndex((candidate) => candidate.upToCents === null || baseCents <= candidate.upToCents);
  const position = index < 0 ? tiers.length - 1 : index;
  const tier = tiers[position];
  const amountCents = tier.type === "FIXED" ? tier.value : Math.round((baseCents * tier.value) / 100);
  return { amountCents, rule: describeTier(tier, tiers[position - 1]) };
}
