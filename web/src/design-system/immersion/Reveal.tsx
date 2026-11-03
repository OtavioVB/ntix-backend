import { useEffect, useRef, useState, type CSSProperties, type ElementType, type HTMLAttributes } from "react";

type RevealProps = HTMLAttributes<HTMLElement> & {
  as?: ElementType;
  /** Atraso em ms, para escalonar elementos vizinhos. */
  delay?: number;
  /** Para o que já nasce na tela, como título e formulário da abertura: entra por tempo,
      sem esperar o observador, que só dispara em quadros renderizados. */
  immediate?: boolean;
};

/** O elemento entra na tela ao rolar até ele. */
export function Reveal({ as: Tag = "div", delay, immediate, className, style, children, ...rest }: RevealProps) {
  const ref = useRef<HTMLElement>(null);
  const [revealed, setRevealed] = useState(false);

  useEffect(() => {
    const element = ref.current;
    if (!element || immediate) return;
    if (!("IntersectionObserver" in window)) {
      setRevealed(true);
      return;
    }
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting) return;
        setRevealed(true);
        observer.disconnect();
      },
      { rootMargin: "0px 0px -8% 0px" },
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, [immediate]);

  const classes = [className, immediate && "n-enter", revealed && "is-revealed"].filter(Boolean).join(" ") || undefined;
  const delayStyle = delay ? ({ "--n-reveal-delay": `${delay}ms` } as CSSProperties) : undefined;

  return (
    <Tag ref={ref} data-reveal={immediate ? undefined : ""} className={classes} style={{ ...delayStyle, ...style }} {...rest}>
      {children}
    </Tag>
  );
}
