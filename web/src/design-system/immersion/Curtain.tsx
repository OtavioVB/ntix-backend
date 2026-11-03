import { useEffect, useState, type ReactNode } from "react";
import { prefersReducedMotion } from "./motion";

type CurtainProps = {
  /** O que está por trás: aparece desfocado, fora do teclado e do leitor de tela. */
  preview: ReactNode;
  compact?: boolean;
  children: ReactNode;
};

/** Conteúdo ainda indisponível: a pessoa vê que existe, mas a cortina só abre depois. */
export function Curtain({ preview, compact, children }: CurtainProps) {
  return (
    <div className={compact ? "n-curtain n-curtain--compact" : "n-curtain"}>
      <div className="n-curtain__preview" inert aria-hidden="true">{preview}</div>
      <div className="n-curtain__drape n-curtain__drape--left" />
      <div className="n-curtain__drape n-curtain__drape--right" />
      <div className="n-curtain__card" data-scene="palco" data-fixed="">{children}</div>
    </div>
  );
}

const OPENING_MS = 1500;

/** A cortina abre uma vez, na entrada. Some por tempo: nunca fica cobrindo a tela. */
export function CurtainOpening() {
  const [visible, setVisible] = useState(() => !prefersReducedMotion());

  useEffect(() => {
    const timer = setTimeout(() => setVisible(false), OPENING_MS);
    return () => clearTimeout(timer);
  }, []);

  if (!visible) return null;
  return (
    <div className="n-curtain-opening" aria-hidden="true">
      <div className="n-curtain-opening__side n-curtain-opening__side--left" />
      <div className="n-curtain-opening__side n-curtain-opening__side--right" />
    </div>
  );
}
