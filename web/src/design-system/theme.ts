import { useSyncExternalStore } from "react";

export type Theme = "light" | "dark";

/* Sem escolha da pessoa, segue o sistema. O script em index.html aplica a mesma regra antes da
   primeira pintura, para a tela não piscar no tema errado: mudou aqui, muda lá. */
const STORAGE_KEY = "ntix.theme";
const DEFAULT_THEME_COLORS: Record<Theme, string> = { light: "#ffffff", dark: "#1b1d20" };
let themeColors: Record<Theme, string> = { ...DEFAULT_THEME_COLORS };

const listeners = new Set<() => void>();

function storedTheme(): Theme | null {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return value === "light" || value === "dark" ? value : null;
  } catch {
    return null;
  }
}

function systemTheme(): Theme {
  return typeof window.matchMedia === "function" && window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
}

function applyTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", themeColors[theme]);
  listeners.forEach((listener) => listener());
}

export function currentTheme(): Theme {
  return document.documentElement.dataset.theme === "light" ? "light" : "dark";
}

export function setTheme(theme: Theme) {
  try {
    window.localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // Sem armazenamento, o tema vale até recarregar a página.
  }
  applyTheme(theme);
}

/** Aplica o tema inicial e passa a acompanhar o sistema enquanto a pessoa não escolher. */
export function initTheme() {
  applyTheme(storedTheme() ?? systemTheme());
  if (typeof window.matchMedia !== "function") return;
  window.matchMedia("(prefers-color-scheme: light)").addEventListener("change", (event) => {
    if (!storedTheme()) applyTheme(event.matches ? "light" : "dark");
  });
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useTheme(): Theme {
  return useSyncExternalStore(subscribe, currentTheme);
}

/** Cor da barra do navegador por tema, definida pelo whitelabel. Sem valor, fica o padrão Ntix. */
export function setThemeColors(colors: Partial<Record<Theme, string | undefined>>) {
  themeColors = {
    light: colors.light ?? DEFAULT_THEME_COLORS.light,
    dark: colors.dark ?? DEFAULT_THEME_COLORS.dark,
  };
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", themeColors[currentTheme()]);
}
