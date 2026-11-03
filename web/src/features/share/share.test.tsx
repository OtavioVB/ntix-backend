import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createOrder } from "../../customer/orders";
import { buildCatalog } from "../../hub/catalog";
import { DEFAULT_AVATAR } from "../avatar/avatarOptions";
import { ShareStory } from "./ShareStory";
import { shareStory, shareText, whatsappUrl } from "./socialShare";
import { storyOverlaySvg, wrapTitle } from "./story";

vi.mock("./story", async (original) => ({
  ...(await original<typeof import("./story")>()),
  buildStoryImage: vi.fn(async () => new Blob(["png"], { type: "image/png" })),
}));

const NOW = Date.UTC(2026, 10, 2, 15);
const event = buildCatalog(NOW).find((candidate) => candidate.slug === "festival-horizonte")!;
const order = createOrder(event, [{ batchId: event.batches[1].id, name: "Pista", quantity: 2, unitPriceCents: 18_000 }], { method: "PIX", status: "PAID", paidAt: NOW }, NOW);
const input = { blob: new Blob(["png"], { type: "image/png" }), slug: event.slug, eventTitle: event.title, eventUrl: "https://ntix.test/eventos/festival-horizonte" };

function stubShare(canShare: boolean, share = vi.fn(async () => undefined)) {
  Object.defineProperty(navigator, "canShare", { configurable: true, value: () => canShare });
  Object.defineProperty(navigator, "share", { configurable: true, value: share });
  return share;
}

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => "blob:story");
  URL.revokeObjectURL = vi.fn();
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
  vi.spyOn(window, "open").mockImplementation(() => null);
});

afterEach(() => {
  vi.restoreAllMocks();
  Object.defineProperty(navigator, "canShare", { configurable: true, value: undefined });
  Object.defineProperty(navigator, "share", { configurable: true, value: undefined });
});

describe("conteúdo do story", () => {
  const svg = storyOverlaySvg({ firstName: "Carla", event, avatarMarkup: "<svg></svg>" });

  it("é específico do evento e usa só o primeiro nome", () => {
    expect(svg).toContain("CARLA VAI!");
    expect(svg).toContain("FESTIVAL");
    expect(svg).toContain("HORIZONTE");
    expect(svg).toContain(event.venue);
    expect(svg).not.toContain("Teste");
  });

  it("nunca leva o código do ingresso", () => {
    const withCode = storyOverlaySvg({ firstName: "Carla", event: order.event, avatarMarkup: "" });
    expect(withCode).not.toContain(order.code);
  });

  it("escapa caracteres que quebrariam a imagem", () => {
    const hostile = storyOverlaySvg({ firstName: "A&B", event: { ...event, title: "Rock & <Roll>", venue: 'Bar "Top"' }, avatarMarkup: "" });
    expect(hostile).toContain("A&amp;B VAI!");
    expect(hostile).toContain("ROCK &amp; &lt;ROLL&gt;");
    expect(hostile).toContain("Bar &quot;Top&quot;");
    expect(hostile).not.toContain("<Roll>");
  });

  it("o avatar nunca cobre o texto, mesmo com título de três linhas", () => {
    const place = (title: string, firstName = "Carla") => {
      const result = storyOverlaySvg({ firstName, event: { ...event, title }, avatarMarkup: '<svg viewBox="0 0 1 1"></svg>' });
      const lastText = Math.max(...[...result.matchAll(/<text class="[ts]" x="80" y="([\d.]+)"/g)].map((match) => Number(match[1])));
      const avatarY = Number(/<svg x="\d+" y="(\d+)"/.exec(result)?.[1]);
      const height = Number(/<svg x="\d+" y="\d+" width="\d+" height="(\d+)"/.exec(result)?.[1]);
      return { lastText, avatarY, height };
    };

    for (const title of ["Riso Solto", "Festival Horizonte", "Um nome de evento realmente muito comprido para caber"]) {
      const { lastText, avatarY, height } = place(title);
      expect(avatarY).toBeGreaterThan(lastText);
      expect(avatarY + height).toBeLessThanOrEqual(1600);
      expect(height).toBeGreaterThan(600);
    }
  });

  it("quebra títulos longos sem estourar as linhas", () => {
    expect(wrapTitle("Festival")).toEqual(["FESTIVAL"]);
    const lines = wrapTitle("Um nome de evento realmente muito comprido para caber em uma imagem");
    expect(lines).toHaveLength(3);
    expect(lines[2].endsWith("…")).toBe(true);
  });
});

describe("compartilhar", () => {
  it("usa o compartilhamento do sistema com a imagem anexada quando existe", async () => {
    const share = stubShare(true);

    const result = await shareStory("instagram", input);

    expect(result.kind).toBe("shared");
    const payload = share.mock.calls[0] as unknown as [ShareData];
    expect(payload[0].files?.[0].name).toBe("ntix-festival-horizonte-story.png");
    expect(payload[0].files?.[0].type).toBe("image/png");
    expect(payload[0].text).toContain(event.title);
    expect(window.open).not.toHaveBeenCalled();
  });

  it("desistir na lista do sistema não baixa nada nem mostra erro", async () => {
    stubShare(true, vi.fn(async () => { throw new DOMException("cancelou", "AbortError"); }));

    const result = await shareStory("tiktok", input);

    expect(result).toEqual({ kind: "cancelled", message: "" });
    expect(HTMLAnchorElement.prototype.click).not.toHaveBeenCalled();
  });

  it("se o sistema falhar, cai no download", async () => {
    stubShare(true, vi.fn(async () => { throw new DOMException("falhou", "NotAllowedError"); }));

    const result = await shareStory("instagram", input);

    expect(result.kind).toBe("downloaded");
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
  });

  it("sem compartilhamento: baixa e explica; no WhatsApp abre a conversa com o texto", async () => {
    stubShare(false);

    const instagram = await shareStory("instagram", input);
    expect(instagram.message).toContain("Instagram");
    expect(window.open).not.toHaveBeenCalled();

    const tiktok = await shareStory("tiktok", input);
    expect(tiktok.message).toContain("TikTok");

    const whatsapp = await shareStory("whatsapp", input);
    expect(whatsapp.kind).toBe("downloaded");
    expect(window.open).toHaveBeenCalledWith(whatsappUrl(shareText(event.title, input.eventUrl)), "_blank", "noopener,noreferrer");
  });

  it("monta o link do WhatsApp com o texto codificado", () => {
    expect(whatsappUrl("Rock & Roll?")).toBe("https://wa.me/?text=Rock%20%26%20Roll%3F");
  });
});

describe("tela de compartilhar", () => {
  it("libera as redes quando a imagem fica pronta e mostra a instrução", async () => {
    stubShare(false);
    const user = userEvent.setup();
    render(<ShareStory order={order} avatar={DEFAULT_AVATAR} customerName="Carla Teste" />);

    expect(screen.getByRole("button", { name: "Instagram" })).toBeDisabled();
    await screen.findByRole("img", { name: /Story com seu avatar/ }, { timeout: 2000 });
    expect(screen.getByRole("button", { name: "TikTok" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "WhatsApp" })).toBeEnabled();

    await user.click(screen.getByRole("button", { name: "Instagram" }));

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("crie um story"));
  });
});
