/* Instagram e TikTok não oferecem API para um site publicar stories, e o WhatsApp não aceita imagem
   por link. O único caminho com a imagem já anexada é o compartilhamento nativo do sistema. Sem ele,
   a imagem é baixada e a pessoa recebe a instrução de onde colocar. */

export type ShareTarget = "instagram" | "tiktok" | "whatsapp";

export const TARGET_NAMES: Record<ShareTarget, string> = {
  instagram: "Instagram",
  tiktok: "TikTok",
  whatsapp: "WhatsApp",
};

export type ShareInput = { blob: Blob; slug: string; eventTitle: string; eventUrl: string };

export type ShareResult = { kind: "shared" | "cancelled" | "downloaded"; message: string };

export function shareText(eventTitle: string, eventUrl: string): string {
  return `Comprei meu ingresso para ${eventTitle}! Bora junto? ${eventUrl}`;
}

export function whatsappUrl(text: string): string {
  return `https://wa.me/?text=${encodeURIComponent(text)}`;
}

export function storyFileName(slug: string): string {
  return `ntix-${slug}-story.png`;
}

export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30_000);
}

const DOWNLOAD_HINTS: Record<ShareTarget, string> = {
  instagram: "Imagem baixada. No celular, abra o Instagram, crie um story e escolha esta imagem.",
  tiktok: "Imagem baixada. No celular, abra o TikTok, toque em criar, depois em Stories, e escolha esta imagem.",
  whatsapp: "Imagem baixada e conversa aberta. Anexe a imagem na conversa ou no seu status.",
};

export async function shareStory(target: ShareTarget, input: ShareInput): Promise<ShareResult> {
  const name = TARGET_NAMES[target];
  const file = new File([input.blob], storyFileName(input.slug), { type: "image/png" });
  const text = shareText(input.eventTitle, input.eventUrl);

  if (typeof navigator.canShare === "function" && navigator.canShare({ files: [file] })) {
    try {
      // O sistema decide a lista de aplicativos: não dá para abrir direto em um deles.
      await navigator.share({ files: [file], text, title: input.eventTitle });
      return { kind: "shared", message: `Pronto! Se escolheu o ${name}, é só publicar por lá.` };
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        return { kind: "cancelled", message: "" };
      }
      // Alguns navegadores anunciam suporte e falham: cai no download.
    }
  }

  downloadBlob(input.blob, storyFileName(input.slug));
  if (target === "whatsapp") window.open(whatsappUrl(text), "_blank", "noopener,noreferrer");
  return { kind: "downloaded", message: DOWNLOAD_HINTS[target] };
}
