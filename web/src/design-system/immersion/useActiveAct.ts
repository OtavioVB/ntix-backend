import { useEffect, useState, type RefObject } from "react";

/** Narrativa por rolagem: devolve o data-act do elemento que cruza o meio da tela. */
export function useActiveAct(rootRef: RefObject<HTMLElement | null>): string | undefined {
  const [active, setActive] = useState<string>();

  useEffect(() => {
    const root = rootRef.current;
    if (!root || !("IntersectionObserver" in window)) return;
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) setActive(entry.target.getAttribute("data-act") ?? undefined);
        });
      },
      { rootMargin: "-50% 0px -50% 0px" },
    );
    root.querySelectorAll("[data-act]").forEach((act) => observer.observe(act));
    return () => observer.disconnect();
  }, [rootRef]);

  return active;
}
