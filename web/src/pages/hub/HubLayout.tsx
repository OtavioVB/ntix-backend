import "./hub.css";

import { useEffect, useState, type FormEvent } from "react";
import { Link, Outlet, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { Icon, Scene, ThemeToggle, Wordmark } from "../../design-system";
import { useCustomer } from "../../customer/session";
import { Avatar } from "../../features/avatar/Avatar";
import { producerAreaHref } from "../../producer/paths";

function HubSearch() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const current = params.get("q") ?? "";
  const [value, setValue] = useState(current);

  useEffect(() => setValue(current), [current]);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    navigate(value.trim() ? `/?q=${encodeURIComponent(value.trim())}#eventos` : "/#eventos");
  }

  return (
    <form className="hub-search" role="search" onSubmit={handleSubmit}>
      <label className="n-visually-hidden" htmlFor="hub-search">Buscar eventos</label>
      <input id="hub-search" type="search" placeholder="Busque por evento, cidade ou produtor" value={value} onChange={(event) => setValue(event.target.value)} />
      <button type="submit" aria-label="Buscar">
        <svg viewBox="0 0 24 24" className="n-icon" aria-hidden="true"><path d="M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM20 20l-4-4" /></svg>
      </button>
    </form>
  );
}

function HubHeader() {
  const { customer, avatarOrDefault } = useCustomer();
  const signedIn = customer.loggedIn && customer.account;

  return (
    <header className="hub-header" data-scene="palco">
      <div className="n-container hub-header__inner">
        <Wordmark href="/" label="Ntix, início" />
        <HubSearch />
        <nav className="hub-header__nav" aria-label="Principal">
          <Link className="hub-header__link hub-header__link--wide" to={producerAreaHref("signup")}>Para produtores</Link>
          <ThemeToggle />
          {signedIn ? (
            <Link className="hub-account" to="/conta" aria-label="Minha conta">
              <span className="hub-account__face"><Avatar config={avatarOrDefault} crop="head" /></span>
              <span className="hub-account__name">{customer.account!.name.split(" ")[0]}</span>
            </Link>
          ) : (
            <Link className="hub-header__link hub-header__enter" to="/conta/entrar">
              <Icon name="team" /> Entrar
            </Link>
          )}
        </nav>
      </div>
    </header>
  );
}

export function HubLayout() {
  const { pathname, hash } = useLocation();

  // Ao trocar de página, volta ao topo; âncoras (#eventos) cuidam de si.
  useEffect(() => {
    if (hash) {
      document.querySelector(hash)?.scrollIntoView();
      return;
    }
    window.scrollTo(0, 0);
  }, [pathname, hash]);

  return (
    <div className="hub">
      <a className="n-skip-link" href="#hub-content">Ir para o conteúdo</a>
      <HubHeader />
      <main id="hub-content">
        <Outlet />
      </main>
      <Scene light="palco" as="footer" className="hub-footer">
        <div className="n-container hub-footer__inner">
          <Wordmark href="/" label="Ntix, início" />
          <p>O hub reúne os eventos dos produtores da Ntix. Cada evento é organizado pelo produtor indicado na página dele.</p>
          <nav className="hub-footer__links" aria-label="Rodapé">
            <Link to="/conta">Minha conta</Link>
            <Link to={producerAreaHref("signup")}>Venda seus ingressos na Ntix</Link>
            <Link to={producerAreaHref("login")}>Acesso do produtor</Link>
          </nav>
        </div>
      </Scene>
    </div>
  );
}
