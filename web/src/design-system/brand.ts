import type { CSSProperties } from "react";

const DARK_INK = "#1b1d20";
const LIGHT_INK = "#ffffff";

function relativeLuminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((start) => {
    const value = parseInt(hex.slice(start, start + 2), 16) / 255;
    return value <= 0.03928 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** Escolhe a tinta com maior contraste: qualquer cor do tenant continua legível. */
export function readableInk(hex: string): string {
  const luminance = relativeLuminance(hex);
  const withLight = 1.05 / (luminance + 0.05);
  const withDark = (luminance + 0.05) / (relativeLuminance(DARK_INK) + 0.05);
  return withLight >= withDark ? LIGHT_INK : DARK_INK;
}

/** Variáveis de marca de um tenant, para usar em um elemento com data-brand. */
export function brandVars(brand: string, hover: string = brand): CSSProperties {
  return {
    "--n-brand": brand,
    "--n-brand-hover": hover,
    "--n-on-brand": readableInk(brand),
  } as CSSProperties;
}
