import archivoFontUrl from "@fontsource-variable/archivo/files/archivo-latin-wdth-normal.woff2?url";
import { createElement } from "react";
import type { CustomerOrder, EventSnapshot } from "../../customer/orders";
import { WORDMARK } from "../../design-system";
import { artToImage } from "../../hub/art";
import { eventDay } from "../../hub/rules";
import type { AvatarConfig } from "../avatar/avatarOptions";

/* Story 9:16 de cada compra. Fundo com a imagem do evento, por cima o nome da pessoa e o avatar
   com o ingresso na mão. De propósito, nunca leva QR Code nem código do ingresso: a foto circula
   em público e quem tivesse o código poderia entrar no lugar da pessoa. */

export const STORY_WIDTH = 1080;
export const STORY_HEIGHT = 1920;

export function escapeXml(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

/** Quebra o título em até três linhas. O que sobra vira reticências em vez de sair da imagem. */
export function wrapTitle(title: string, maxChars = 15, maxLines = 3): string[] {
  const lines: string[] = [];
  let current = "";
  for (const word of title.toUpperCase().split(/\s+/)) {
    if (`${current} ${word}`.trim().length > maxChars && current) {
      lines.push(current);
      current = word;
    } else {
      current = `${current} ${word}`.trim();
    }
  }
  if (current) lines.push(current);
  if (lines.length <= maxLines) return lines;
  const kept = lines.slice(0, maxLines);
  kept[maxLines - 1] = `${kept[maxLines - 1].slice(0, maxChars - 1)}…`;
  return kept;
}

const AVATAR_FLOOR = 1590;
const AVATAR_RATIO = 424 / 530;

/** O avatar ocupa o que sobra abaixo do texto: quanto maior o título, menor ele fica, sem cobrir nada. */
export function avatarBox(textBottom: number) {
  const y = Math.round(textBottom + 22);
  const height = AVATAR_FLOOR - y;
  const width = Math.round(height * AVATAR_RATIO);
  return { x: Math.round((STORY_WIDTH - width) / 2), y, width, height };
}

export type StoryContent = {
  firstName: string;
  event: Pick<EventSnapshot, "title" | "date" | "venue" | "city" | "accent">;
  /** O avatar já em SVG, encaixado no meio da imagem. */
  avatarMarkup: string;
  fontCss?: string;
};

/** A camada de texto e avatar. Função pura: o que aparece no story está todo aqui. */
export function storyOverlaySvg({ firstName, event, avatarMarkup, fontCss = "" }: StoryContent): string {
  const name = firstName.toUpperCase();
  const nameSize = name.length > 9 ? Math.max(96, Math.round(1500 / (name.length + 4))) : 168;
  const lines = wrapTitle(event.title);
  const titleSize = lines.some((line) => line.length > 12) ? 84 : 104;
  const day = eventDay(event.date);
  const when = `${day.full.charAt(0).toUpperCase()}${day.full.slice(1)}`;
  const titleTop = 300 + nameSize * 0.92;
  const whenTop = titleTop + lines.length * titleSize * 1.02 + 6;
  const { N, T, I, X } = WORDMARK;
  const box = avatarBox(whenTop + 92);
  const placedAvatar = avatarMarkup.replace("<svg ", `<svg x="${box.x}" y="${box.y}" width="${box.width}" height="${box.height}" `);

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${STORY_WIDTH}" height="${STORY_HEIGHT}" viewBox="0 0 ${STORY_WIDTH} ${STORY_HEIGHT}">
<defs>
<style>${fontCss}
.t{font-family:"Archivo Variable",Archivo,"Arial Narrow",Arial,sans-serif;font-stretch:72%;font-weight:800;paint-order:stroke;stroke:rgb(10 10 12 / 0.55);stroke-width:7px;stroke-linejoin:round}
.s{font-family:"Archivo Variable",Archivo,Arial,sans-serif;font-weight:600;paint-order:stroke;stroke:rgb(10 10 12 / 0.6);stroke-width:5px}
</style>
<linearGradient id="story-beam" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#ffe27a" stop-opacity="0.42"/><stop offset="1" stop-color="#ffe27a" stop-opacity="0.04"/></linearGradient>
<radialGradient id="story-floor"><stop offset="0" stop-color="#ffe27a" stop-opacity="0.7"/><stop offset="1" stop-color="#ffe27a" stop-opacity="0"/></radialGradient>
</defs>
<path d="M420 ${box.y}H660L1010 1600H70Z" fill="url(#story-beam)"/>
<ellipse cx="540" cy="1578" rx="470" ry="44" fill="url(#story-floor)"/>
<text class="t" x="80" y="${300 + nameSize * 0.72}" font-size="${nameSize}" fill="#ffcf33">${escapeXml(name)} VAI!</text>
${lines.map((line, index) => `<text class="t" x="80" y="${titleTop + titleSize * (index + 0.82)}" font-size="${titleSize}" fill="#ffffff">${escapeXml(line)}</text>`).join("\n")}
<text class="s" x="80" y="${whenTop + 44}" font-size="38" fill="#ffffff">${escapeXml(when)}</text>
<text class="s" x="80" y="${whenTop + 92}" font-size="34" fill="#f4f4f2">${escapeXml(`${event.venue}, ${event.city}`)}</text>
${placedAvatar}
<g transform="translate(80 1612) scale(0.05)">
<g transform="matrix(1 0 0 -1 -61 804)"><path fill="#ffcf33" d="${N}"/><g fill="#ffffff"><path transform="translate(660)" d="${T}"/><path transform="translate(1032)" d="${I}"/><path transform="translate(1319)" d="${X}"/></g></g>
</g>
<text class="s" x="190" y="1652" font-size="30" fill="#ffffff">Ingressos na Ntix</text>
</svg>`;
}

let fontCssCache: Promise<string> | null = null;

/** Imagem SVG não enxerga as fontes da página: a fonte vai embutida. Sem ela, vale a fonte do sistema. */
function loadFontCss(): Promise<string> {
  fontCssCache ??= fetch(archivoFontUrl)
    .then((response) => response.arrayBuffer())
    .then((buffer) => {
      let binary = "";
      new Uint8Array(buffer).forEach((byte) => {
        binary += String.fromCharCode(byte);
      });
      return `@font-face{font-family:"Archivo Variable";font-weight:100 900;font-stretch:62% 125%;src:url(data:font/woff2;base64,${btoa(binary)}) format("woff2")}`;
    })
    .catch(() => "");
  return fontCssCache;
}

function loadImage(src: string, crossOrigin = false): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    if (crossOrigin) image.crossOrigin = "anonymous";
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("Imagem não carregou"));
    image.src = src;
  });
}

function drawCover(ctx: CanvasRenderingContext2D, image: HTMLImageElement) {
  const scale = Math.max(STORY_WIDTH / image.naturalWidth, STORY_HEIGHT / image.naturalHeight);
  const width = image.naturalWidth * scale;
  const height = image.naturalHeight * scale;
  ctx.drawImage(image, (STORY_WIDTH - width) / 2, (STORY_HEIGHT - height) / 2, width, height);
}

/** Monta o PNG. As importações ficam aqui dentro para só pesar em quem de fato abre o story. */
export async function buildStoryImage(order: CustomerOrder, avatar: AvatarConfig, firstName: string): Promise<Blob> {
  const [{ renderToStaticMarkup }, { Avatar }, fontCss] = await Promise.all([
    import("react-dom/server"),
    import("../avatar/Avatar"),
    loadFontCss(),
  ]);

  const count = order.items.reduce((sum, item) => sum + item.quantity, 0);
  const markup = renderToStaticMarkup(createElement(Avatar, { config: avatar, ticket: { title: order.event.title, count, accent: order.event.accent } }));
  const overlay = storyOverlaySvg({ firstName, event: order.event, avatarMarkup: markup, fontCss });

  const canvas = document.createElement("canvas");
  canvas.width = STORY_WIDTH;
  canvas.height = STORY_HEIGHT;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("Canvas indisponível");

  const art = artToImage(order.event.art);
  const background = order.event.imageUrl
    ? await loadImage(order.event.imageUrl, true).catch(() => loadImage(art))
    : await loadImage(art);
  drawCover(ctx, background);

  // Véu: o texto precisa ser lido sobre qualquer foto que o produtor enviar.
  const veil = ctx.createLinearGradient(0, 0, 0, STORY_HEIGHT);
  veil.addColorStop(0, "rgba(14,14,18,0.72)");
  veil.addColorStop(0.42, "rgba(14,14,18,0.5)");
  veil.addColorStop(1, "rgba(14,14,18,0.86)");
  ctx.fillStyle = veil;
  ctx.fillRect(0, 0, STORY_WIDTH, STORY_HEIGHT);

  const overlayImage = await loadImage(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(overlay)}`);
  ctx.drawImage(overlayImage, 0, 0, STORY_WIDTH, STORY_HEIGHT);

  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error("Falha ao gerar a imagem"))), "image/png");
  });
}
