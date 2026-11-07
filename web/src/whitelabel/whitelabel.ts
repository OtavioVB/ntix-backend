import { readableInk } from "../design-system/brand";
import { setThemeColors } from "../design-system/theme";
import { safeAssetUrl, toCssVariables, type WhitelabelConfig } from "./config";

/**
 * Carrega e aplica a identidade da implantação antes da primeira pintura.
 * Origem: `VITE_WHITELABEL_URL` quando definida (futuro endpoint da API), senão `/whitelabel.json`,
 * que cada implantação substitui sem rebuild (volume, ConfigMap, CDN).
 */
const DEFAULT_SOURCE = "/whitelabel.json";
const LOAD_TIMEOUT_MS = 3000;
const DEFAULT_NAME = "Ntix";
const HEX = /^#[0-9a-f]{6}$/i;

let active: WhitelabelConfig = {};

export function whitelabelSource(): string {
  return (import.meta.env.VITE_WHITELABEL_URL as string | undefined) || DEFAULT_SOURCE;
}

/** A configuração aplicada, para quem precisa dela fora do CSS (nome e logo da marca). */
export function getWhitelabel(): WhitelabelConfig {
  return active;
}

export function brandName(): string {
  const name = active.brand?.name;
  return typeof name === "string" && name.trim() ? name.trim() : DEFAULT_NAME;
}

function addStylesheet(href: string) {
  const loaded = [...document.head.querySelectorAll('link[rel="stylesheet"]')].some((link) => link.getAttribute("href") === href);
  if (loaded) return;
  const link = document.createElement("link");
  link.rel = "stylesheet";
  link.href = href;
  document.head.appendChild(link);
}

function setFavicon(href: string) {
  let link = document.head.querySelector<HTMLLinkElement>('link[rel="icon"]');
  if (!link) {
    link = document.createElement("link");
    link.rel = "icon";
    document.head.appendChild(link);
  }
  link.removeAttribute("type");
  link.href = href;
}

/** Aplica a configuração no documento: tokens no :root, fontes, título, favicon e cor da barra. */
export function applyWhitelabel(config: WhitelabelConfig, root: HTMLElement = document.documentElement) {
  active = config ?? {};
  const vars = toCssVariables(active);

  const action = vars["--n-brand"];
  if (action && !vars["--n-on-brand"] && HEX.test(action)) vars["--n-on-brand"] = readableInk(action);

  for (const [name, value] of Object.entries(vars)) root.style.setProperty(name, value);

  const stylesheets = active.typography?.stylesheets;
  if (Array.isArray(stylesheets)) stylesheets.map(safeAssetUrl).forEach((href) => href && addStylesheet(href));

  const favicon = safeAssetUrl(active.brand?.faviconUrl);
  if (favicon) setFavicon(favicon);

  document.title = brandName();

  const themeColor = active.brand?.themeColor;
  setThemeColors({
    light: typeof themeColor?.light === "string" && HEX.test(themeColor.light) ? themeColor.light : undefined,
    dark: typeof themeColor?.dark === "string" && HEX.test(themeColor.dark) ? themeColor.dark : undefined,
  });
}

/**
 * Busca a configuração e aplica. Falha, tempo esgotado ou resposta inválida mantêm o padrão Ntix:
 * a plataforma nunca deixa de abrir por causa da marca.
 */
export async function loadWhitelabel(source = whitelabelSource(), fetcher: typeof fetch = fetch): Promise<WhitelabelConfig> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), LOAD_TIMEOUT_MS);
  try {
    const response = await fetcher(source, { signal: controller.signal, headers: { Accept: "application/json" } });
    if (!response.ok) return {};
    const body: unknown = await response.json();
    const config = typeof body === "object" && body !== null && !Array.isArray(body) ? (body as WhitelabelConfig) : {};
    applyWhitelabel(config);
    return config;
  } catch {
    return {};
  } finally {
    clearTimeout(timer);
  }
}
