import { afterEach, describe, expect, it, vi } from "vitest";
import { applyWhitelabel, brandName, loadWhitelabel, safeAssetUrl, toCssVariables } from ".";

afterEach(() => {
  document.documentElement.removeAttribute("style");
  document.head.querySelectorAll('link[rel="stylesheet"]').forEach((link) => link.remove());
  applyWhitelabel({});
});

describe("toCssVariables", () => {
  it("traduz cada seção para os tokens do design system", () => {
    const vars = toCssVariables({
      colors: { primary: { "500": "#123456" }, secondary: { "800": "#222222" }, neutral: { "0": "#fff" }, action: { base: "#123456", hover: "#234567" } },
      typography: { fontFamily: { sans: "Inter, sans-serif" }, text: { md: "1rem" }, display: { lg: "5rem" } },
      spacing: { scale: { "4": "1rem" }, gutter: "2rem", pageMax: "80rem" },
      radius: { md: "6px", notch: "9px" },
      motion: { duration: { fast: "100ms" }, easing: { out: "ease-out" } },
    });

    expect(vars).toEqual({
      "--n-primary-500": "#123456",
      "--n-secondary-800": "#222222",
      "--n-neutral-0": "#fff",
      "--n-brand": "#123456",
      "--n-brand-hover": "#234567",
      "--n-font-sans": "Inter, sans-serif",
      "--n-text-md": "1rem",
      "--n-display-lg": "5rem",
      "--n-space-4": "1rem",
      "--n-gutter": "2rem",
      "--n-page-max": "80rem",
      "--n-radius-md": "6px",
      "--n-notch": "9px",
      "--n-dur-fast": "100ms",
      "--n-ease-out": "ease-out",
    });
  });

  it("ignora valores inseguros, chaves desconhecidas e entrada inválida", () => {
    expect(toCssVariables({ colors: { primary: { "500": "red; } body { display:none" }, unknown: { "1": "#fff" } } })).toEqual({});
    expect(toCssVariables({ colors: { primary: { "a b": "#fff", "500": 42 } } })).toEqual({});
    expect(toCssVariables(null)).toEqual({});
    expect(toCssVariables([1, 2])).toEqual({});
  });
});

describe("safeAssetUrl", () => {
  it("aceita https e caminho do próprio front", () => {
    expect(safeAssetUrl("https://cdn.exemplo.com/logo.svg")).toBe("https://cdn.exemplo.com/logo.svg");
    expect(safeAssetUrl("/logo.svg")).toBe("/logo.svg");
  });

  it("recusa http, protocolo relativo, javascript e vazio", () => {
    expect(safeAssetUrl("http://exemplo.com/logo.svg")).toBeUndefined();
    expect(safeAssetUrl("//exemplo.com/logo.svg")).toBeUndefined();
    expect(safeAssetUrl("javascript:alert(1)")).toBeUndefined();
    expect(safeAssetUrl("")).toBeUndefined();
  });
});

describe("applyWhitelabel", () => {
  it("aplica os tokens no :root e calcula a tinta sobre a cor de ação", () => {
    applyWhitelabel({ colors: { primary: { "500": "#0044ff" }, action: { base: "#0044ff" } } });
    const style = document.documentElement.style;

    expect(style.getPropertyValue("--n-primary-500")).toBe("#0044ff");
    expect(style.getPropertyValue("--n-on-brand")).toBe("#ffffff");
  });

  it("define nome, folhas de fonte seguras e mantém o padrão Ntix sem nome", () => {
    applyWhitelabel({ brand: { name: "Palco Sul" }, typography: { stylesheets: ["https://fonts.exemplo.com/a.css", "http://inseguro/b.css"] } });

    expect(document.title).toBe("Palco Sul");
    expect(brandName()).toBe("Palco Sul");
    const links = [...document.head.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].map((link) => link.getAttribute("href"));
    expect(links).toEqual(["https://fonts.exemplo.com/a.css"]);

    applyWhitelabel({});
    expect(brandName()).toBe("Ntix");
  });
});

describe("loadWhitelabel", () => {
  it("busca a configuração da origem e aplica", async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ colors: { primary: { "500": "#aa0000" } } }), { status: 200 }));

    await loadWhitelabel("/whitelabel.json", fetcher);

    expect(fetcher).toHaveBeenCalledWith("/whitelabel.json", expect.objectContaining({ headers: { Accept: "application/json" } }));
    expect(document.documentElement.style.getPropertyValue("--n-primary-500")).toBe("#aa0000");
  });

  it("mantém o padrão quando a origem falha ou responde erro", async () => {
    await expect(loadWhitelabel("/x", vi.fn().mockRejectedValue(new TypeError("rede")))).resolves.toEqual({});
    await expect(loadWhitelabel("/x", vi.fn().mockResolvedValue(new Response("", { status: 404 })))).resolves.toEqual({});
    expect(document.documentElement.style.getPropertyValue("--n-primary-500")).toBe("");
  });
});
