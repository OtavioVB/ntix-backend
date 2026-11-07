/**
 * Contrato da configuração whitelabel: a identidade visual de uma implantação da plataforma.
 * Hoje vem de `public/whitelabel.json`; amanhã, da API (mesmo formato). Todo campo é opcional:
 * o que faltar cai no padrão Ntix declarado em `design-system/tokens.css`.
 *
 * Cada campo vira um token CSS do design system (ver `toCssVariables`). As cenas e os
 * componentes só referenciam esses tokens, então trocar a configuração troca a plataforma inteira.
 */

/** Escala de cor por degrau ("50" a "950"), como nos primitivos do design system. */
export type ColorScale = Partial<Record<string, string>>;

export type WhitelabelConfig = {
  /** Versão do contrato, para a API evoluir o formato sem quebrar implantações antigas. */
  version?: 1;
  brand?: {
    /** Nome da plataforma: título da aba e rótulo acessível da marca. */
    name?: string;
    /** Logo no lugar do wordmark Ntix. URL https ou caminho do próprio front. */
    logoUrl?: string;
    faviconUrl?: string;
    /** Cor da barra do navegador no celular, por tema. */
    themeColor?: { light?: string; dark?: string };
  };
  colors?: {
    /** Cor da marca e de destaque (botão principal, foco, detalhes). Degraus 50–900. */
    primary?: ColorScale;
    /** Cor de ação nas cenas claras (casa, ouro). Degraus 50–900. */
    secondary?: ColorScale;
    /** Superfícies, textos e linhas. Degraus 0, 50–950. */
    neutral?: ColorScale;
    success?: ColorScale;
    danger?: ColorScale;
    /** Cor de ação. Sem ela, usa primary.500 / primary.400, e a tinta sobre ela é calculada pelo contraste. */
    action?: { base?: string; hover?: string; ink?: string };
  };
  typography?: {
    fontFamily?: { sans?: string; mono?: string };
    /** Folhas de estilo das fontes (ex.: Google Fonts). Só https ou caminho do próprio front. */
    stylesheets?: string[];
    /** Tamanhos de texto: xs, sm, md, lg, xl. */
    text?: Partial<Record<string, string>>;
    /** Tamanhos de título de destaque: sm, md, lg. */
    display?: Partial<Record<string, string>>;
  };
  spacing?: {
    /** Escala de espaço, degraus 1–10 (base 4 na Ntix). */
    scale?: Partial<Record<string, string>>;
    /** Respiro lateral da página. */
    gutter?: string;
    /** Largura máxima do conteúdo. */
    pageMax?: string;
  };
  /** Raios: sm, md, lg e notch (picotagem do ingresso). */
  radius?: Partial<Record<string, string>>;
  motion?: {
    /** Durações: fast, base, slow, scene. */
    duration?: Partial<Record<string, string>>;
    easing?: { out?: string; inOut?: string };
  };
};

const KEY = /^[a-z0-9-]+$/i;
/* Valor de token: sem caracteres que fechem a declaração ou abram outra regra. */
const SAFE_VALUE = /^[^;{}<>\\]{1,200}$/;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function put(target: Record<string, string>, name: string, value: unknown) {
  if (typeof value === "string" && SAFE_VALUE.test(value.trim())) target[name] = value.trim();
}

function putScale(target: Record<string, string>, prefix: string, scale: unknown) {
  if (!isRecord(scale)) return;
  for (const [step, value] of Object.entries(scale)) if (KEY.test(step)) put(target, `${prefix}-${step}`, value);
}

/**
 * Traduz a configuração para os tokens CSS do design system. Chaves desconhecidas e valores
 * inseguros são ignorados: a configuração vem de fora (arquivo ou API) e não é confiável.
 */
export function toCssVariables(config: unknown): Record<string, string> {
  const vars: Record<string, string> = {};
  if (!isRecord(config)) return vars;

  const { colors, typography, spacing, radius, motion } = config;

  if (isRecord(colors)) {
    for (const palette of ["primary", "secondary", "neutral", "success", "danger"]) putScale(vars, `--n-${palette}`, colors[palette]);
    if (isRecord(colors.action)) {
      put(vars, "--n-brand", colors.action.base);
      put(vars, "--n-brand-hover", colors.action.hover);
      put(vars, "--n-on-brand", colors.action.ink);
    }
  }

  if (isRecord(typography)) {
    if (isRecord(typography.fontFamily)) {
      put(vars, "--n-font-sans", typography.fontFamily.sans);
      put(vars, "--n-font-mono", typography.fontFamily.mono);
    }
    putScale(vars, "--n-text", typography.text);
    putScale(vars, "--n-display", typography.display);
  }

  if (isRecord(spacing)) {
    putScale(vars, "--n-space", spacing.scale);
    put(vars, "--n-gutter", spacing.gutter);
    put(vars, "--n-page-max", spacing.pageMax);
  }

  if (isRecord(radius)) {
    for (const [name, value] of Object.entries(radius)) {
      if (name === "notch") put(vars, "--n-notch", value);
      else if (KEY.test(name)) put(vars, `--n-radius-${name}`, value);
    }
  }

  if (isRecord(motion)) {
    putScale(vars, "--n-dur", motion.duration);
    if (isRecord(motion.easing)) {
      put(vars, "--n-ease-out", motion.easing.out);
      put(vars, "--n-ease-in-out", motion.easing.inOut);
    }
  }

  return vars;
}

/** URL de recurso externo (logo, favicon, fonte): só https ou caminho do próprio front. */
export function safeAssetUrl(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const url = value.trim();
  if (url.startsWith("/") && !url.startsWith("//")) return url;
  try {
    return new URL(url).protocol === "https:" ? url : undefined;
  } catch {
    return undefined;
  }
}
