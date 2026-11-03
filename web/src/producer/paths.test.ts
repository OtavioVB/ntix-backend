import { describe, expect, it } from "vitest";
import { isProducerHost, overviewSegment, panelPath, producerAreaHref, signupPath } from "./paths";

const PRODUCER = "produtores.ntix.com.br";
const PUBLIC = "ntix.com.br";
const LOCAL = "localhost";

describe("endereços da área do produtor", () => {
  it("reconhece o subdomínio do produtor, inclusive em desenvolvimento", () => {
    expect(isProducerHost(PRODUCER)).toBe(true);
    expect(isProducerHost("produtores.localhost")).toBe(true);
    expect(isProducerHost(PUBLIC)).toBe(false);
    expect(isProducerHost(LOCAL)).toBe(false);
  });

  it("no subdomínio, o painel fica na raiz e a visão geral tem caminho próprio", () => {
    expect(panelPath("", PRODUCER)).toBe("/visao-geral");
    expect(panelPath("eventos", PRODUCER)).toBe("/eventos");
    expect(panelPath("eventos/42", PRODUCER)).toBe("/eventos/42");
    expect(overviewSegment(PRODUCER)).toBe("visao-geral");
    expect(signupPath(PRODUCER)).toBe("/");
  });

  it("fora do subdomínio, o painel segue sob /painel e o cadastro em /produtores", () => {
    expect(panelPath("", LOCAL)).toBe("/painel");
    expect(panelPath("eventos", LOCAL)).toBe("/painel/eventos");
    expect(overviewSegment(LOCAL)).toBeUndefined();
    expect(signupPath(LOCAL)).toBe("/produtores");
  });

  it("no domínio público, os links do hub levam para o subdomínio do produtor", () => {
    expect(producerAreaHref("signup", PUBLIC)).toBe("https://produtores.ntix.com.br/");
    expect(producerAreaHref("login", "www.ntix.com.br")).toBe("https://produtores.ntix.com.br/entrar");
  });

  it("em desenvolvimento, os links do hub continuam no mesmo app", () => {
    expect(producerAreaHref("signup", LOCAL)).toBe("/produtores");
    expect(producerAreaHref("login", LOCAL)).toBe("/entrar");
  });
});
