import { useEffect, useRef, type CSSProperties, type ElementType, type HTMLAttributes } from "react";
import { prefersReducedMotion } from "./motion";

export type SceneLight = "palco" | "penumbra" | "casa" | "ouro";

type SceneProps = HTMLAttributes<HTMLElement> & {
  light: SceneLight;
  as?: ElementType;
  /** A luz da cena segue o ponteiro. Use com <FollowSpot />. */
  spotlight?: boolean;
  /** As luzes da casa acendem conforme a cena entra na tela. */
  lightsUp?: boolean;
  /** Continua escura no tema claro: para texto sobre imagem e momentos de palco. */
  fixed?: boolean;
};

/** Seção com iluminação própria: os componentes dentro dela se adaptam sozinhos. */
export function Scene({ light, as: Tag = "section", spotlight, lightsUp, fixed, className, children, ...rest }: SceneProps) {
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    const scene = ref.current;
    if (!spotlight || !scene || prefersReducedMotion()) return;
    const onPointerMove = (event: PointerEvent) => {
      if (event.pointerType === "touch") return;
      const rect = scene.getBoundingClientRect();
      scene.style.setProperty("--n-spot-x", `${event.clientX - rect.left}px`);
      scene.style.setProperty("--n-spot-y", `${event.clientY - rect.top}px`);
    };
    scene.addEventListener("pointermove", onPointerMove, { passive: true });
    return () => scene.removeEventListener("pointermove", onPointerMove);
  }, [spotlight]);

  useEffect(() => {
    const scene = ref.current;
    if (!lightsUp || !scene || prefersReducedMotion()) return;
    let frame = 0;
    const update = () => {
      frame = 0;
      const viewport = window.innerHeight;
      const progress = Math.min(1, Math.max(0, (viewport - scene.getBoundingClientRect().top) / (viewport * 0.6)));
      scene.style.setProperty("--n-house-dark", (0.88 * (1 - progress)).toFixed(3));
    };
    const schedule = () => {
      if (!frame) frame = requestAnimationFrame(update);
    };
    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule, { passive: true });
    update();
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("scroll", schedule);
      window.removeEventListener("resize", schedule);
    };
  }, [lightsUp]);

  return (
    <Tag
      ref={ref}
      className={["n-scene", className].filter(Boolean).join(" ")}
      data-scene={light}
      data-lights-up={lightsUp || undefined}
      data-fixed={fixed || undefined}
      {...rest}
    >
      {children}
    </Tag>
  );
}

type BeamProps = {
  /** Posição horizontal na cena, como "18%". */
  x: string;
  /** Largura, como "44vw". */
  width: string;
  tilt: number;
  swing: number;
  /** Intensidade da luz, em porcentagem. */
  power: number;
  /** Duração do balanço, em segundos. */
  sway: number;
};

/** Canhão de luz. */
export function Beam({ x, width, tilt, swing, power, sway }: BeamProps) {
  const style = {
    "--_x": x,
    "--_w": width,
    "--_tilt": `${tilt}deg`,
    "--_swing": `${swing}deg`,
    "--_power": `${power}%`,
    "--_sway": `${sway}s`,
  } as CSSProperties;
  return <div className="n-beam" style={style} />;
}

export function FollowSpot() {
  return <div className="n-follow-spot" />;
}
