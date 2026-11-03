import { QRCodeSVG } from "qrcode.react";
import { useState } from "react";
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { Badge, Button, ButtonLink, Scene, Segmented, Stamp } from "../../design-system";
import { markTicketUsed, ticketCount, type CustomerOrder, type IssuedTicket, type OrderPayment } from "../../customer/orders";
import { useCustomer } from "../../customer/session";
import { Avatar } from "../../features/avatar/Avatar";
import { AvatarBuilder } from "../../features/avatar/AvatarBuilder";
import { ShareStory } from "../../features/share/ShareStory";
import { eventDay } from "../../hub/rules";
import { usePurchase } from "../../hub/useHub";
import { formatCents } from "../../platform/money";
import { fullDate } from "../../platform/selectors";
import type { CheckIn } from "../../platform/types";
import { useProducerSession } from "../../producer/session";
import { AuthForm } from "../hub/AuthForm";
import { EventImage } from "../hub/parts";

export function paymentLabel(payment: OrderPayment): string {
  if (payment.method === "PIX") return "Pix";
  const installments = payment.installments && payment.installments > 1 ? ` em ${payment.installments}x` : " à vista";
  return `${payment.brand ?? "Cartão"} final ${payment.last4}${installments}`;
}

function orderStatus(order: CustomerOrder): { label: string; live: boolean } {
  if (order.payment.status === "PENDING") return { label: "Aguardando pagamento", live: true };
  const used = order.tickets.filter((ticket) => ticket.status === "USED").length;
  if (used === order.tickets.length && used > 0) return { label: "Utilizado", live: false };
  if (used > 0) return { label: `${used} de ${order.tickets.length} utilizados`, live: false };
  return { label: "Pago", live: true };
}

export function CustomerLoginPage() {
  const { customer } = useCustomer();
  const navigate = useNavigate();
  const { state } = useLocation();

  if (customer.loggedIn && customer.account) return <Navigate to="/conta" replace />;

  return (
    <div className="account-login">
      <title>Entrar · Ntix</title>
      <div className="account-login__card">
        <h1 className="n-display n-display--md">Sua conta Ntix.</h1>
        <p className="n-lead">Ingressos, pedidos e seu avatar, em um lugar só.</p>
        <AuthForm onDone={() => navigate((state as { from?: string } | null)?.from ?? "/conta")} />
      </div>
    </div>
  );
}

export function AccountLayout() {
  const { customer, avatarOrDefault } = useCustomer();
  const { pathname } = useLocation();

  if (!customer.loggedIn || !customer.account) return <Navigate to="/conta/entrar" replace state={{ from: pathname }} />;

  const links = [
    { to: "/conta/ingressos", label: "Meus ingressos" },
    { to: "/conta/pedidos", label: "Pedidos" },
    { to: "/conta/avatar", label: "Avatar" },
    { to: "/conta/dados", label: "Meus dados" },
  ];

  return (
    <div className="account">
      <div className="n-container">
        <header className="account__head">
          <span className="account__face"><Avatar config={avatarOrDefault} crop="head" /></span>
          <div>
            <p className="n-label">Minha conta</p>
            <p className="account__name">{customer.account.name}</p>
          </div>
        </header>
        <nav className="account__tabs" aria-label="Minha conta">
          {links.map((link) => <NavLink key={link.to} to={link.to}>{link.label}</NavLink>)}
        </nav>
        <div className="account__content"><Outlet /></div>
      </div>
    </div>
  );
}

function ticketEntries(orders: CustomerOrder[]) {
  return orders.flatMap((order) => order.tickets.map((ticket) => ({ order, ticket })));
}

export function TicketsPage() {
  const { customer } = useCustomer();
  const [tab, setTab] = useState<"NEXT" | "PAST">("NEXT");
  const now = Date.now();
  const entries = ticketEntries(customer.orders);
  const upcoming = entries.filter(({ order }) => order.event.date >= now).sort((a, b) => a.order.event.date - b.order.event.date);
  const past = entries.filter(({ order }) => order.event.date < now).sort((a, b) => b.order.event.date - a.order.event.date);
  const shown = tab === "NEXT" ? upcoming : past;
  const pending = customer.orders.filter((order) => order.payment.status === "PENDING");

  return (
    <>
      <title>Meus ingressos · Ntix</title>
      <h1 className="n-display n-display--md">Meus ingressos</h1>

      {pending.map((order) => (
        <Link key={order.code} className="account-alert" to={`/conta/pedidos/${order.code}`}>
          <strong>Falta pagar o Pix de {order.event.title}.</strong> Os ingressos saem quando o pagamento for confirmado.
        </Link>
      ))}

      <div className="account-tabs-inline">
        <Segmented
          name="ticket-tab"
          legend="Mostrar"
          options={[{ value: "NEXT", label: `Próximos (${upcoming.length})` }, { value: "PAST", label: `Já foram (${past.length})` }] as const}
          value={tab}
          onChange={setTab}
        />
      </div>

      {shown.length === 0 ? (
        <div className="hub-empty">
          <h2 className="n-title">{tab === "NEXT" ? "Nenhum ingresso por vir" : "Nenhum evento no histórico"}</h2>
          <p>Quando você comprar, seus ingressos aparecem aqui.</p>
          <ButtonLink to="/" arrow>Ver eventos</ButtonLink>
        </div>
      ) : (
        <ul className="ticket-list">
          {shown.map(({ order, ticket }) => <TicketRow key={ticket.code} order={order} ticket={ticket} />)}
        </ul>
      )}
    </>
  );
}

/** Ingresso de evento de um produtor da plataforma: a portaria dele marca a entrada. */
function entryAt(ticket: IssuedTicket, checkIns: CheckIn[] = []): number | undefined {
  return ticket.usedAt ?? checkIns.find((checkIn) => checkIn.ticketCode === ticket.code)?.at;
}

function TicketRow({ order, ticket }: { order: CustomerOrder; ticket: IssuedTicket }) {
  const day = eventDay(order.event.date);
  const { session } = useProducerSession();
  const usedAt = entryAt(ticket, session.platform?.checkIns);
  const used = ticket.status === "USED" || usedAt !== undefined;
  return (
    <li>
      <Link className={used ? "ticket-row is-used" : "ticket-row"} to={`/conta/ingressos/${ticket.code}`}>
        <span className="ticket-row__image"><EventImage event={order.event} /></span>
        <span className="ticket-row__body">
          <strong>{order.event.title}</strong>
          <span>{day.full} · {order.event.venue}</span>
          <span className="ticket-row__meta">{ticket.batchName} · <span className="n-figure">{ticket.code}</span></span>
        </span>
        <span className="ticket-row__status">
          {used ? <Badge>Utilizado</Badge> : order.event.date < Date.now() ? <Badge>Não utilizado</Badge> : <Badge live>Válido</Badge>}
        </span>
      </Link>
    </li>
  );
}

export function TicketDetailPage() {
  const { code } = useParams();
  const { customer, avatarOrDefault, updateOrder } = useCustomer();
  const { session } = useProducerSession();
  const order = customer.orders.find((candidate) => candidate.tickets.some((ticket) => ticket.code === code));
  const ticket = order?.tickets.find((candidate) => candidate.code === code);

  if (!order || !ticket) return <Navigate to="/conta/ingressos" replace />;

  const day = eventDay(order.event.date);
  const usedAt = entryAt(ticket, session.platform?.checkIns);
  const used = ticket.status === "USED" || usedAt !== undefined;
  const isPast = order.event.date < Date.now();

  return (
    <>
      <title>{`Ingresso ${ticket.code} · Ntix`}</title>
      <Link className="back-link" to="/conta/ingressos">Meus ingressos</Link>
      <div className="ticket-view">
        <article className={used ? "ticket-paper is-used" : "ticket-paper"} aria-label={`Ingresso de ${order.event.title}`}>
          <div className="ticket-paper__image"><EventImage event={order.event} eager /></div>
          <div className="ticket-paper__body">
            <h1 className="ticket-paper__title">{order.event.title}</h1>
            <dl className="ticket-paper__facts">
              <div><dt>Data</dt><dd>{day.full}</dd></div>
              <div><dt>Portões</dt><dd>{order.event.doorsAt}</dd></div>
              <div><dt>Local</dt><dd>{order.event.venue}, {order.event.city}</dd></div>
              <div><dt>Ingresso</dt><dd>{ticket.batchName}</dd></div>
              <div><dt>Titular</dt><dd>{customer.account?.name}</dd></div>
            </dl>
          </div>
          <div className="ticket-paper__stub">
            {used ? (
              <div className="ticket-paper__used">
                <Stamp>Utilizado</Stamp>
                {usedAt && <p>Entrada em {fullDate(usedAt)}</p>}
              </div>
            ) : (
              <>
                <div className="ticket-paper__qr"><QRCodeSVG value={ticket.code} size={168} marginSize={2} title={`Código do ingresso ${ticket.code}`} /></div>
                <p>Apresente este código na entrada.</p>
              </>
            )}
            <p className="ticket-paper__code n-figure">{ticket.code}</p>
          </div>
        </article>

        <aside className="ticket-side">
          <Avatar config={avatarOrDefault} ticket={{ title: order.event.title, accent: order.event.accent }} label={`Seu avatar segurando o ingresso de ${order.event.title}`} />
          <Link className="ticket-side__link" to="/conta/avatar">Mudar meu avatar</Link>
          <dl className="ticket-side__facts">
            <div><dt>Pedido</dt><dd><Link to={`/conta/pedidos/${order.code}`}>{order.code}</Link></dd></div>
            <div><dt>Pagamento</dt><dd>{paymentLabel(order.payment)}</dd></div>
            <div><dt>Situação</dt><dd>{used ? "Já utilizado" : isPast ? "Não utilizado" : "Válido para a entrada"}</dd></div>
          </dl>
          {!used && !isPast && (
            <Button variant="ghost" block onClick={() => updateOrder(order.code, (current) => markTicketUsed(current, ticket.code, Date.now()))}>
              Simular entrada no evento
            </Button>
          )}
        </aside>
      </div>
    </>
  );
}

export function OrdersPage() {
  const { customer } = useCustomer();
  const orders = [...customer.orders].sort((a, b) => b.createdAt - a.createdAt);

  return (
    <>
      <title>Pedidos · Ntix</title>
      <h1 className="n-display n-display--md">Pedidos</h1>
      {orders.length === 0 ? (
        <div className="hub-empty">
          <h2 className="n-title">Você ainda não fez pedidos</h2>
          <ButtonLink to="/" arrow>Ver eventos</ButtonLink>
        </div>
      ) : (
        <ul className="order-list">
          {orders.map((order) => {
            const status = orderStatus(order);
            return (
              <li key={order.code}>
                <Link className="order-row" to={`/conta/pedidos/${order.code}`}>
                  <span className="ticket-row__image"><EventImage event={order.event} /></span>
                  <span className="order-row__event">
                    <strong>{order.event.title}</strong>
                    <span>Comprado em {fullDate(order.createdAt)} · <span className="n-figure">{order.code}</span></span>
                  </span>
                  <span className="order-row__pay">
                    <strong className="n-figure">{formatCents(order.totalCents)}</strong>
                    <span>{paymentLabel(order.payment)}</span>
                  </span>
                  <span className="ticket-row__status"><Badge live={status.live}>{status.label}</Badge></span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </>
  );
}

function PixPanel({ order }: { order: CustomerOrder }) {
  const { payPending } = usePurchase();
  const navigate = useNavigate();
  const [copied, setCopied] = useState(false);
  const pixCode = `DEMONSTRACAO-NTIX-PIX-${order.code}`;

  async function copy() {
    try {
      await navigator.clipboard.writeText(pixCode);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  return (
    <section className="pix-panel" aria-labelledby="pix-title">
      <div className="ticket-paper__qr"><QRCodeSVG value={pixCode} size={176} marginSize={2} title="Código Pix de demonstração" /></div>
      <div className="pix-panel__copy">
        <h2 className="n-title" id="pix-title">Pague {formatCents(order.totalCents)} com Pix</h2>
        <p>Abra o aplicativo do banco, escolha pagar com Pix e leia o código ou cole o código abaixo.</p>
        <code className="pix-panel__code">{pixCode}</code>
        <div className="pix-panel__actions">
          <Button variant="ghost" onClick={copy}>{copied ? "Código copiado" : "Copiar código"}</Button>
          <Button
            arrow
            onClick={() => {
              payPending(order);
              navigate(`/conta/pedidos/${order.code}/confirmado`, { replace: true });
            }}
          >
            Simular pagamento do Pix
          </Button>
        </div>
        <p className="signup__note">Demonstração: este código não faz pagamento nenhum. Em produção, a confirmação chega sozinha do provedor.</p>
      </div>
    </section>
  );
}

export function OrderDetailPage() {
  const { code } = useParams();
  const { customer, avatarOrDefault } = useCustomer();
  const order = customer.orders.find((candidate) => candidate.code === code);

  if (!order) return <Navigate to="/conta/pedidos" replace />;

  const status = orderStatus(order);
  const day = eventDay(order.event.date);
  const count = ticketCount(order.items);

  return (
    <>
      <title>{`Pedido ${order.code} · Ntix`}</title>
      <Link className="back-link" to="/conta/pedidos">Pedidos</Link>
      <header className="order-head">
        <span className="order-head__image"><EventImage event={order.event} eager /></span>
        <div>
          <h1 className="n-display n-display--sm">{order.event.title}</h1>
          <p>{day.full} · {order.event.venue}, {order.event.city}</p>
          <Badge live={status.live}>{status.label}</Badge>
        </div>
      </header>

      {order.payment.status === "PENDING" && <PixPanel order={order} />}
      {order.payment.status === "PAID" && order.event.date >= Date.now() && (
        <ShareStory order={order} avatar={avatarOrDefault} customerName={customer.account?.name ?? ""} />
      )}

      <div className="order-panels">
        <section className="order-panel" aria-labelledby="items-title">
          <h2 className="tile__title" id="items-title">O que você comprou</h2>
          <ul className="purchase-box__items">
            {order.items.map((item) => (
              <li key={item.batchId}><span>{item.quantity} × {item.name}</span><b className="n-figure">{formatCents(item.quantity * item.unitPriceCents)}</b></li>
            ))}
          </ul>
          <div className="purchase-box__total"><span>Total · {count} {count === 1 ? "ingresso" : "ingressos"}</span><strong>{formatCents(order.totalCents)}</strong></div>
        </section>

        <section className="order-panel" aria-labelledby="payment-title">
          <h2 className="tile__title" id="payment-title">Como você pagou</h2>
          <dl className="ticket-side__facts">
            <div><dt>Forma de pagamento</dt><dd>{paymentLabel(order.payment)}</dd></div>
            <div><dt>Situação</dt><dd>{order.payment.status === "PAID" ? "Pago" : "Aguardando pagamento"}</dd></div>
            {order.payment.paidAt && <div><dt>Pago em</dt><dd>{fullDate(order.payment.paidAt)}</dd></div>}
            <div><dt>Pedido feito em</dt><dd>{fullDate(order.createdAt)}</dd></div>
            <div><dt>Código do pedido</dt><dd className="n-figure">{order.code}</dd></div>
          </dl>
        </section>
      </div>

      {order.tickets.length > 0 && (
        <section aria-labelledby="order-tickets-title">
          <h2 className="tile__title order-tickets-title" id="order-tickets-title">Ingressos deste pedido</h2>
          <ul className="ticket-list">
            {order.tickets.map((ticket) => <TicketRow key={ticket.code} order={order} ticket={ticket} />)}
          </ul>
        </section>
      )}
    </>
  );
}

export function AvatarPage() {
  const { customer, update, avatarOrDefault } = useCustomer();
  const nextEvent = customer.orders.find((order) => order.tickets.length > 0 && order.event.date >= Date.now())?.event;

  return (
    <>
      <title>Meu avatar · Ntix</title>
      <h1 className="n-display n-display--md">Seu avatar</h1>
      <p className="n-lead account-lead">É ele quem segura o ingresso a cada compra. Mude quando quiser.</p>
      <div className="avatar-page">
        <Scene light="palco" fixed className="avatar-page__stage">
          <div className="stage__beam" aria-hidden="true" />
          <Avatar config={avatarOrDefault} ticket={{ title: nextEvent?.title ?? "Seu próximo evento", accent: nextEvent?.accent }} label="Prévia do seu avatar" />
          <div className="stage__floor" aria-hidden="true" />
        </Scene>
        <AvatarBuilder value={avatarOrDefault} onChange={(avatar) => update({ avatar })} />
      </div>
    </>
  );
}

export function ProfilePage() {
  const { customer, update } = useCustomer();
  const navigate = useNavigate();
  const account = customer.account!;

  return (
    <>
      <title>Meus dados · Ntix</title>
      <h1 className="n-display n-display--md">Meus dados</h1>
      <section className="order-panel profile-panel">
        <dl className="ticket-side__facts">
          <div><dt>Nome</dt><dd>{account.name}</dd></div>
          <div><dt>E-mail</dt><dd>{account.email}</dd></div>
          <div><dt>CPF</dt><dd className="n-figure">{account.document}</dd></div>
        </dl>
        <Button variant="ghost" onClick={() => { update({ loggedIn: false }); navigate("/"); }}>Sair da conta</Button>
        <button
          type="button"
          className="signup__back"
          onClick={() => {
            update({ account: null, loggedIn: false, avatar: null, orders: [], cart: null });
            navigate("/");
          }}
        >
          Apagar esta conta de demonstração
        </button>
        <p className="signup__note">Sua conta é única e vale no hub e nas ticketerias dos produtores. Demonstração: estes dados ficam só neste navegador.</p>
      </section>
    </>
  );
}
