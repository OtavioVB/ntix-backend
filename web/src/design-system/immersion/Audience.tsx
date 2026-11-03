import { useEffect, useRef } from "react";
import { prefersReducedMotion } from "./motion";

type Seat = { x: number; y: number; size: number; depth: number; sold: boolean; flash: number };

const SOLD_TARGET = 0.62;
const FRAME_MS = 33;
const SALE_MS = 110;

/** Plateia: fileiras em arco de frente para o palco, com corredores.
    Os assentos vão sendo vendidos e acendem em ouro. */
export function Audience({ className }: { className?: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx) return;

    const scene = canvas.closest<HTMLElement>(".n-scene") ?? canvas.parentElement!;
    let seats: Seat[] = [];
    let soldCount = 0;
    let width = 0;
    let height = 0;
    let visible = true;
    let lastSale = 0;
    let lastFrame = 0;
    let frame = 0;
    let resizeTimer = 0;
    const pointer = { x: -9999, y: -9999 };

    function draw() {
      ctx!.clearRect(0, 0, width, height);
      for (const seat of seats) {
        const dx = seat.x - pointer.x;
        const dy = seat.y - pointer.y;
        const lit = Math.max(0, 1 - Math.sqrt(dx * dx + dy * dy) / 240);
        const base = 0.14 + seat.depth * 0.16;
        ctx!.fillStyle = seat.sold
          ? `rgba(201,155,20,${Math.min(1, base * 2.4 + lit * 0.5 + seat.flash)})`
          : `rgba(146,149,155,${Math.min(1, base * 0.8 + lit * 0.45)})`;
        ctx!.fillRect(seat.x, seat.y, seat.size, seat.size * 0.82);
        if (seat.flash > 0) seat.flash = Math.max(0, seat.flash - 0.035);
      }
    }

    function build() {
      const rect = canvas!.getBoundingClientRect();
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      width = rect.width;
      height = rect.height;
      canvas!.width = Math.round(width * ratio);
      canvas!.height = Math.round(height * ratio);
      ctx!.setTransform(ratio, 0, 0, ratio, 0, 0);

      seats = [];
      soldCount = 0;
      const cx = width * 0.5;
      const cy = -height * 0.1;
      const maxRadius = Math.hypot(width * 0.75, height * 1.15);
      let radius = height * 0.42;
      let row = 0;
      while (radius < maxRadius && radius > 0) {
        const depth = Math.min(1, row / 26);
        const size = 1.7 + depth * 2.6;
        const step = (size * 2.9) / radius;
        for (let angle = 0.12; angle < Math.PI - 0.12; angle += step) {
          // Corredores: blocos de assentos separados por vãos radiais.
          if (Math.abs(((angle * 7) % 1) - 0.5) < 0.035) continue;
          const x = cx + Math.cos(angle) * radius * 1.25;
          const y = cy + Math.sin(angle) * radius;
          if (x < -6 || x > width + 6 || y > height + 6) continue;
          const sold = Math.random() < 0.3;
          if (sold) soldCount++;
          seats.push({ x, y, size, depth, sold, flash: 0 });
        }
        radius += size * 3.5;
        row++;
      }
      draw();
    }

    function sell() {
      if (soldCount / seats.length >= SOLD_TARGET) return;
      for (let i = 0; i < 3; i++) {
        const seat = seats[Math.floor(Math.random() * seats.length)];
        if (seat && !seat.sold) {
          seat.sold = true;
          seat.flash = 1;
          soldCount++;
        }
      }
    }

    function loop(now: number) {
      frame = requestAnimationFrame(loop);
      if (!visible || now - lastFrame < FRAME_MS) return;
      lastFrame = now;
      if (now - lastSale > SALE_MS) {
        sell();
        lastSale = now;
      }
      draw();
    }

    const onResize = () => {
      clearTimeout(resizeTimer);
      resizeTimer = window.setTimeout(build, 160);
    };
    const onPointerMove = (event: PointerEvent) => {
      if (event.pointerType === "touch") return;
      const rect = canvas.getBoundingClientRect();
      pointer.x = event.clientX - rect.left;
      pointer.y = event.clientY - rect.top;
    };
    const onVisibility = () => {
      cancelAnimationFrame(frame);
      if (!document.hidden) frame = requestAnimationFrame(loop);
    };

    window.addEventListener("resize", onResize, { passive: true });
    build();

    let observer: IntersectionObserver | undefined;
    const animated = !prefersReducedMotion();
    if (animated) {
      scene.addEventListener("pointermove", onPointerMove, { passive: true });
      document.addEventListener("visibilitychange", onVisibility);
      if ("IntersectionObserver" in window) {
        observer = new IntersectionObserver(([entry]) => {
          visible = entry.isIntersecting;
        });
        observer.observe(canvas);
      }
      frame = requestAnimationFrame(loop);
    }

    return () => {
      cancelAnimationFrame(frame);
      clearTimeout(resizeTimer);
      observer?.disconnect();
      window.removeEventListener("resize", onResize);
      scene.removeEventListener("pointermove", onPointerMove);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, []);

  return <canvas ref={canvasRef} className={["n-audience", className].filter(Boolean).join(" ")} aria-hidden="true" />;
}
