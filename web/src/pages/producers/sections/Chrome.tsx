import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button, Reveal, Scene, ThemeToggle, Wordmark } from "../../../design-system";
import { focusSignup } from "../signup";

const NAV_LINKS = [
  { href: "#ticketeria", label: "Ticketeria" },
  { href: "#operacao", label: "Operação" },
  { href: "#como-comecar", label: "Como começar" },
  { href: "#duvidas", label: "Dúvidas" },
];

export function SiteNav() {
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const update = () => setScrolled(window.scrollY > 24);
    window.addEventListener("scroll", update, { passive: true });
    update();
    return () => window.removeEventListener("scroll", update);
  }, []);

  return (
    <header className={scrolled ? "site-nav is-scrolled" : "site-nav"} data-scene="palco" data-fixed="">
      <div className="n-container site-nav__inner">
        <Wordmark href="#inicio" label="Ntix, início" />
        <nav className="site-nav__links" aria-label="Seções">
          {NAV_LINKS.map((link) => (
            <a key={link.href} href={link.href}>{link.label}</a>
          ))}
        </nav>
        <ThemeToggle className="site-nav__theme" />
        <Link className="site-nav__login" to="/entrar">Entrar</Link>
        <Button href="#cadastro" className="site-nav__cta" onClick={focusSignup}>Criar cadastro</Button>
      </div>
    </header>
  );
}

export function Finale() {
  return (
    <Scene light="ouro" className="finale" id="decisao" aria-labelledby="finale-title">
      <div className="n-container finale__inner">
        <Reveal as="h2" className="n-display finale__title" id="finale-title">
          O próximo evento já pode ser na sua bilheteria.
        </Reveal>
        <Reveal>
          <Button href="#cadastro" size="lg" arrow onClick={focusSignup}>Criar meu cadastro</Button>
        </Reveal>
      </div>
    </Scene>
  );
}

export function SiteFooter() {
  return (
    <Scene light="palco" as="footer" className="site-footer">
      <div className="n-container site-footer__inner">
        <Wordmark />
        <p>Plataforma whitelabel de venda de ingressos e <Link to="/">hub de eventos</Link>.</p>
      </div>
    </Scene>
  );
}

/** Ação sempre à mão no celular, exceto quando o formulário ou a seção final já estão na tela. */
export function MobileCta({ signupComplete }: { signupComplete: boolean }) {
  const [blocked, setBlocked] = useState(true);

  useEffect(() => {
    if (!("IntersectionObserver" in window)) return;
    const onScreen = new Set<Element>();
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) onScreen.add(entry.target);
        else onScreen.delete(entry.target);
      });
      setBlocked(onScreen.size > 0);
    });
    ["cadastro", "decisao"].forEach((id) => {
      const element = document.getElementById(id);
      if (element) observer.observe(element);
    });
    return () => observer.disconnect();
  }, []);

  return (
    <div className="mobile-cta" hidden={blocked || signupComplete}>
      <Button href="#cadastro" block onClick={focusSignup}>Criar meu cadastro</Button>
    </div>
  );
}
