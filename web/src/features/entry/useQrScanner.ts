import { useCallback, useEffect, useRef, useState, type RefObject } from "react";

export type ScannerState = "IDLE" | "STARTING" | "SCANNING" | "DENIED" | "UNAVAILABLE";

const FRAME_INTERVAL_MS = 120;
const MAX_WIDTH = 640;
/** O mesmo QR continua na frente da câmera por alguns segundos: só conta a primeira leitura. */
const REPEAT_WINDOW_MS = 3000;

/** Câmera só abre por um toque da pessoa: o navegador pede a permissão nesse momento. */
export function canUseCamera(): boolean {
  return typeof navigator !== "undefined" && Boolean(navigator.mediaDevices?.getUserMedia) && window.isSecureContext;
}

/** Lê QR Codes pela câmera traseira. `paused` segura a leitura enquanto o resultado está na tela. */
export function useQrScanner(videoRef: RefObject<HTMLVideoElement | null>, onCode: (code: string) => void, paused: boolean) {
  const [state, setState] = useState<ScannerState>(canUseCamera() ? "IDLE" : "UNAVAILABLE");
  const streamRef = useRef<MediaStream | null>(null);
  const onCodeRef = useRef(onCode);
  const pausedRef = useRef(paused);
  onCodeRef.current = onCode;
  pausedRef.current = paused;

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    setState((current) => (current === "SCANNING" || current === "STARTING" ? "IDLE" : current));
  }, []);

  const start = useCallback(async () => {
    if (!canUseCamera()) return setState("UNAVAILABLE");
    setState("STARTING");
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: "environment" } }, audio: false });
      streamRef.current = stream;
      const video = videoRef.current;
      if (!video) return stream.getTracks().forEach((track) => track.stop());
      video.srcObject = stream;
      await video.play();
      setState("SCANNING");
    } catch (error) {
      const name = (error as DOMException).name;
      setState(name === "NotAllowedError" || name === "SecurityError" ? "DENIED" : "UNAVAILABLE");
    }
  }, [videoRef]);

  useEffect(() => {
    if (state !== "SCANNING") return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout>;
    let last = { code: "", at: 0 };
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("2d", { willReadFrequently: true });

    // Carregado só aqui: quem nunca abre a portaria não baixa o leitor.
    void import("jsqr").then(({ default: jsQR }) => {
      const tick = () => {
        if (cancelled) return;
        const video = videoRef.current;
        if (video && context && video.videoWidth && !pausedRef.current) {
          const scale = Math.min(1, MAX_WIDTH / video.videoWidth);
          canvas.width = Math.round(video.videoWidth * scale);
          canvas.height = Math.round(video.videoHeight * scale);
          context.drawImage(video, 0, 0, canvas.width, canvas.height);
          const image = context.getImageData(0, 0, canvas.width, canvas.height);
          const found = jsQR(image.data, image.width, image.height, { inversionAttempts: "dontInvert" });
          const now = Date.now();
          if (found?.data && !(found.data === last.code && now - last.at < REPEAT_WINDOW_MS)) {
            last = { code: found.data, at: now };
            onCodeRef.current(found.data);
          }
        }
        timer = setTimeout(tick, FRAME_INTERVAL_MS);
      };
      tick();
    });

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [state, videoRef]);

  useEffect(() => () => streamRef.current?.getTracks().forEach((track) => track.stop()), []);

  return { state, start, stop };
}

/** Retorno que não depende de olhar para a tela: vibração e um bipe, curto se entrou e triplo se não. */
export function signal(ok: boolean) {
  navigator.vibrate?.(ok ? 90 : [70, 60, 70, 60, 70]);
  try {
    const audio = new AudioContext();
    const beeps = ok ? [0] : [0, 0.16, 0.32];
    beeps.forEach((offset) => {
      const oscillator = audio.createOscillator();
      const gain = audio.createGain();
      oscillator.frequency.value = ok ? 880 : 220;
      gain.gain.value = 0.15;
      oscillator.connect(gain).connect(audio.destination);
      oscillator.start(audio.currentTime + offset);
      oscillator.stop(audio.currentTime + offset + 0.12);
    });
    setTimeout(() => void audio.close(), 800);
  } catch {
    // Sem áudio, fica a vibração e a cor.
  }
}
