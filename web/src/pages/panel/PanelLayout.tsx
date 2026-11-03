import "./panel.css";

import { useEffect, useRef, useState } from "react";
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate } from "react-router-dom";
import { Badge, ButtonLink, CurtainOpening, Icon, Meter, Scene, ThemeToggle, Wordmark } from "../../design-system";
import { contractTicketeria } from "../../platform/actions";
import { emptyPlatformData, generateDemoData } from "../../platform/demoData";
import { formatCents } from "../../platform/money";
import { currentBalance } from "../../platform/selectors";
import { approvalSteps, useProducerSession } from "../../producer/session";
import { PANEL_GROUPS } from "./navigation";

const APPROVAL_PATH = "/painel/aprovacao";

const STATUS_LABELS = {
  PENDING: "Cadastro pendente",
  PENDING_APPROVAL: "Em aprovação",
  ACTIVE: "Ativa",
};

function SideCard({ onNavigate }: { onNavigate: () => void }) {
  const { session } = useProducerSession();

  if (session.status === "ACTIVE") {
    return (
      <Link className="panel-side__approval" to="/painel/carteira" onClick={onNavigate}>
        <span className="n-label n-label--accent">Saldo na carteira</span>
        <strong className="panel-side__balance">{formatCents(session.platform ? currentBalance(session.platform) : 0)}</strong>
        <small>Ver extrato e sacar</small>
      </Link>
    );
  }

  const steps = approvalSteps(session);
  const done = steps.filter((step) => step.done).length;
  const inReview = session.status === "PENDING_APPROVAL";

  return (
    <Link className="panel-side__approval" to={APPROVAL_PATH} onClick={onNavigate}>
      <span className="n-label n-label--accent">{inReview ? "Em análise" : "Sua estreia"}</span>
      <strong>{inReview ? "Cadastro enviado" : `${done} de ${steps.length} etapas prontas`}</strong>
      <Meter value={inReview ? steps.length : done} max={steps.length} label={`${done} de ${steps.length} etapas concluídas`} />
      <small>{inReview ? "A equipe Ntix está analisando" : "Envie para aprovação e abra tudo"}</small>
    </Link>
  );
}

function RehearsalBar() {
  const { session } = useProducerSession();
  const { pathname } = useLocation();
  const inReview = session.status === "PENDING_APPROVAL";

  return (
    <div className="rehearsal" data-scene="ouro" role="region" aria-label="Situação do cadastro">
      <p className="rehearsal__text">
        <strong>{inReview ? "Cadastro em análise." : "Modo ensaio."}</strong>{" "}
        {inReview
          ? "Os dados continuam sendo de exemplo. As áreas bloqueadas abrem quando a Ntix aprovar sua produtora."
          : "Estes dados são de exemplo. Envie para aprovação para operar de verdade e abrir toda a plataforma."}
      </p>
      {!inReview && pathname !== APPROVAL_PATH && (
        <ButtonLink to={APPROVAL_PATH} className="rehearsal__cta">Enviar para aprovação</ButtonLink>
      )}
    </div>
  );
}

/** Sem API, a produção roda sobre dados locais: isso precisa estar sempre à vista. */
function DemoStrip() {
  const { session, update } = useProducerSession();
  const hasStore = session.ticketeria === "ACTIVE";
  const isDemo = session.platform?.isDemo;

  function toggle() {
    const now = Date.now();
    if (!isDemo) {
      update({ platform: generateDemoData(now, hasStore) });
      return;
    }
    const empty = emptyPlatformData(now);
    update({ platform: hasStore ? contractTicketeria(empty, now) : empty });
  }

  return (
    <div className="demo-strip" role="region" aria-label="Ambiente de demonstração">
      <p>{isDemo ? "Demonstração: vendas fictícias, sem API conectada." : "Demonstração: sua conta está sem vendas, como a de quem acabou de ser aprovado."}</p>
      <button type="button" onClick={toggle}>{isDemo ? "Ver como uma conta nova" : "Carregar vendas fictícias"}</button>
    </div>
  );
}

export function PanelLayout() {
  const { session, update } = useProducerSession();
  const { pathname, state, key } = useLocation();
  const navigate = useNavigate();
  const [menuOpen, setMenuOpen] = useState(false);
  const [curtainKey, setCurtainKey] = useState<string>();
  const contentRef = useRef<HTMLElement>(null);
  const isFirstRender = useRef(true);
  const isActive = session.status === "ACTIVE";
  const wantsCurtain = Boolean((state as { openCurtain?: boolean } | null)?.openCurtain);

  // A cortina abre no cadastro e na aprovação, e só nesses momentos.
  useEffect(() => {
    if (wantsCurtain) setCurtainKey(key);
  }, [wantsCurtain, key]);

  // Troca de rota: volta ao topo e leva o foco ao conteúdo, como uma navegação comum faria.
  useEffect(() => {
    setMenuOpen(false);
    if (isFirstRender.current) {
      isFirstRender.current = false;
      return;
    }
    window.scrollTo(0, 0);
    contentRef.current?.focus({ preventScroll: true });
  }, [pathname]);

  useEffect(() => {
    if (!menuOpen) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMenuOpen(false);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [menuOpen]);

  if (!session.loggedIn) return <Navigate to="/entrar" replace />;

  const admin = session.team.find((member) => member.isAdmin);

  function logout() {
    update({ loggedIn: false });
    navigate("/entrar");
  }

  return (
    <div className="panel">
      {curtainKey && <CurtainOpening key={curtainKey} />}
      <a className="n-skip-link" href="#panel-content">Ir para o conteúdo</a>

      <header className="panel-topbar" data-scene="palco">
        <Wordmark href="/painel" label="Ntix, visão geral" />
        <button
          type="button"
          className="panel-topbar__menu"
          aria-expanded={menuOpen}
          aria-controls="panel-side"
          aria-label={menuOpen ? "Fechar menu" : "Abrir menu"}
          onClick={() => setMenuOpen((open) => !open)}
        >
          <Icon name={menuOpen ? "close" : "menu"} />
        </button>
      </header>

      {menuOpen && <div className="panel-scrim" onClick={() => setMenuOpen(false)} />}

      <Scene light="palco" as="aside" className={menuOpen ? "panel-side is-open" : "panel-side"} id="panel-side" aria-label="Menu da plataforma">
        <div className="panel-side__brand">
          <Wordmark href="/painel" label="Ntix, visão geral" />
        </div>

        <div className="panel-side__producer">
          <strong>{session.name || "Sua produtora"}</strong>
          <Badge live={isActive}>{STATUS_LABELS[session.status]}</Badge>
        </div>

        <nav className="panel-side__nav" aria-label="Áreas da plataforma">
          {PANEL_GROUPS.map((group) => (
            <div className="panel-side__group" key={group.label}>
              <p className="n-label">{group.label}</p>
              <ul>
                {group.areas.map((area) => (
                  <li key={area.path}>
                    <NavLink to={area.path} end={area.path === "/painel"} className="panel-side__link">
                      <Icon name={area.icon} />
                      <span>{area.label}</span>{" "}
                      {area.lockedInRehearsal && !isActive && (
                        <span className="panel-side__lock">
                          <Icon name="lock" />
                          <span className="n-visually-hidden">(liberado após aprovação)</span>
                        </span>
                      )}
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>

        <SideCard onNavigate={() => setMenuOpen(false)} />

        <div className="panel-side__user">
          <span>{admin?.name ?? session.adminEmail}</span>
          <ThemeToggle />
          <button type="button" onClick={logout}>Sair</button>
        </div>
      </Scene>

      <Scene light="penumbra" as="div" className="panel-main">
        {isActive ? <DemoStrip /> : <RehearsalBar />}
        <main className="panel-content" id="panel-content" ref={contentRef} tabIndex={-1}>
          <Outlet />
        </main>
      </Scene>
    </div>
  );
}
