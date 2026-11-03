import type { AnchorHTMLAttributes, ButtonHTMLAttributes, ReactNode } from "react";
import { Link, type LinkProps } from "react-router-dom";

type ButtonStyle = {
  variant?: "primary" | "ghost";
  size?: "md" | "lg";
  block?: boolean;
  arrow?: boolean;
  children: ReactNode;
};

function buttonClasses({ variant = "primary", size = "md", block }: ButtonStyle, className?: string) {
  return ["n-btn", `n-btn--${variant}`, size === "lg" && "n-btn--lg", block && "n-btn--block", className].filter(Boolean).join(" ");
}

function ButtonContent({ arrow, children }: ButtonStyle) {
  return (
    <>
      {children}
      {arrow && (
        <svg className="n-btn__arrow" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M4 12h15M13 6l6 6-6 6" />
        </svg>
      )}
    </>
  );
}

type ButtonProps = ButtonStyle &
  (({ href: string } & AnchorHTMLAttributes<HTMLAnchorElement>) | ({ href?: undefined } & ButtonHTMLAttributes<HTMLButtonElement>));

/** Com href vira link; sem href, botão. Em cada vista, uma única ação primária. */
export function Button({ variant, size, block, arrow, className, children, ...rest }: ButtonProps) {
  const style = { variant, size, block, arrow, children };
  if (rest.href !== undefined) {
    return (
      <a className={buttonClasses(style, className)} {...(rest as AnchorHTMLAttributes<HTMLAnchorElement>)}>
        <ButtonContent {...style} />
      </a>
    );
  }
  return (
    <button className={buttonClasses(style, className)} {...(rest as ButtonHTMLAttributes<HTMLButtonElement>)}>
      <ButtonContent {...style} />
    </button>
  );
}

/** Mesma aparência de Button, para navegar entre rotas da aplicação. */
export function ButtonLink({ variant, size, block, arrow, className, children, ...rest }: ButtonStyle & LinkProps) {
  const style = { variant, size, block, arrow, children };
  return (
    <Link className={buttonClasses(style, className)} {...rest}>
      <ButtonContent {...style} />
    </Link>
  );
}
