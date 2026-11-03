import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ThemeToggle } from "./components/ThemeToggle";
import { initTheme, setTheme } from "./theme";

function stubSystem(prefersLight: boolean) {
  vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: prefersLight, addEventListener: vi.fn() })));
}

beforeEach(() => {
  window.localStorage.clear();
  delete document.documentElement.dataset.theme;
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("tema", () => {
  it("sem escolha, segue o sistema", () => {
    stubSystem(true);
    initTheme();
    expect(document.documentElement.dataset.theme).toBe("light");

    stubSystem(false);
    initTheme();
    expect(document.documentElement.dataset.theme).toBe("dark");
  });

  it("a escolha da pessoa vence o sistema e sobrevive ao recarregar", () => {
    stubSystem(true);
    setTheme("dark");
    initTheme();
    expect(document.documentElement.dataset.theme).toBe("dark");
  });

  it("acompanha a barra do navegador", () => {
    const meta = document.createElement("meta");
    meta.name = "theme-color";
    document.head.appendChild(meta);

    setTheme("light");
    expect(meta.content).toBe("#ffffff");
    setTheme("dark");
    expect(meta.content).toBe("#1b1d20");
    meta.remove();
  });

  it("o botão alterna, diz para onde vai e guarda a escolha", async () => {
    const user = userEvent.setup();
    setTheme("dark");
    render(<ThemeToggle />);

    await user.click(screen.getByRole("button", { name: "Usar tema claro" }));
    expect(document.documentElement.dataset.theme).toBe("light");
    expect(window.localStorage.getItem("ntix.theme")).toBe("light");

    await user.click(screen.getByRole("button", { name: "Usar tema escuro" }));
    expect(document.documentElement.dataset.theme).toBe("dark");
  });
});
