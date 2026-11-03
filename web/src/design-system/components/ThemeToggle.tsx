import { setTheme, useTheme } from "../theme";
import { Icon } from "./Icon";

/** Troca entre claro e escuro. O ícone mostra para onde vai, não onde está. */
export function ThemeToggle({ className }: { className?: string }) {
  const theme = useTheme();
  const next = theme === "light" ? "dark" : "light";
  const label = next === "light" ? "Usar tema claro" : "Usar tema escuro";

  return (
    <button type="button" className={["n-theme-toggle", className].filter(Boolean).join(" ")} aria-label={label} title={label} onClick={() => setTheme(next)}>
      <Icon name={next === "light" ? "sun" : "moon"} />
    </button>
  );
}
