import { QRCodeSVG } from "qrcode.react";
import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import type { ApiDataResult } from "../../api/client";
import { getMyTicket, listMyOrders, listMyTickets, type CustomerOrderItem, type CustomerTicketDetail, type CustomerTicketItem, type TicketWhen } from "../../api/customerArea";
import { getOrder, type ApiOrder } from "../../api/orders";
import { Badge, ButtonLink, Segmented, Stamp } from "../../design-system";
import { formatCents } from "../../platform/money";
import { fullDate } from "../../platform/selectors";

/*
 * Telas da conta do comprador ligadas à API (REQ13), com `?api=real`. A demonstração continua em AccountPages.tsx.
 * Os dados vêm do servidor a cada abertura; o que a API não tem (ex.: entrada registrada na portaria) não aparece aqui.
 */

type Load<T> = { state: "loading" } | { state: "ready"; data: T } | { state: "error"; message: string };

const GENERIC_ERROR = "Não foi possível carregar agora. Tente de novo em instantes.";

/** Carrega uma vez por mudança de `key`; cancela a chamada ao sair da tela. */
function useLoad<T>(load: (signal: AbortSignal) => Promise<ApiDataResult<T>>, key: string): Load<T> {
  const [result, setResult] = useState<Load<T>>({ state: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    setResult({ state: "loading" });

    load(controller.signal)
      .then((response) => {
        if (controller.signal.aborted) return;
        setResult(response.ok ? { state: "ready", data: response.data } : { state: "error", message: response.notifications[0]?.message ?? GENERIC_ERROR });
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ state: "error", message: GENERIC_ERROR });
      });

    return () => controller.abort();
    // `load` muda a cada renderização; a chave decide quando recarregar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  return result;
}

const STATUS_LABEL: Record<CustomerOrderItem["status"], { label: string; live: boolean }> = {
  PAID: { label: "Pago", live: true },
  PENDING_PAYMENT: { label: "Aguardando pagamento", live: false },
  EXPIRED: { label: "Expirado", live: false },
};

function Feedback({ message }: Readonly<{ message: string }>) {
  return <p role="alert" className="account-alert">{message}</p>;
}

export function RealOrdersPage() {
  const orders = useLoad((signal) => listMyOrders({}, signal), "orders");

  return (
    <>
      <title>Pedidos · Ntix</title>
      <h1 className="n-display n-display--md">Pedidos</h1>
      {orders.state === "loading" && <p>Carregando seus pedidos…</p>}
      {orders.state === "error" && <Feedback message={orders.message} />}
      {orders.state === "ready" && (orders.data.items.length === 0 ? (
        <div className="hub-empty">
          <h2 className="n-title">Você ainda não fez pedidos</h2>
          <ButtonLink to="/" arrow>Ver eventos</ButtonLink>
        </div>
      ) : (
        <ul className="order-list">
          {orders.data.items.map((order) => {
            const status = STATUS_LABEL[order.status];
            return (
              <li key={order.code}>
                <Link className="order-row" to={`/conta/pedidos/${order.code}`}>
                  <span className="order-row__event">
                    <strong>{order.event.title}</strong>
                    <span>Comprado em {fullDate(Date.parse(order.createdAt))} · <span className="n-figure">{order.code}</span></span>
                  </span>
                  <span className="order-row__pay">
                    <strong className="n-figure">{formatCents(order.totalCents)}</strong>
                    <span>{order.ticketCount} ingresso(s)</span>
                  </span>
                  <span className="ticket-row__status"><Badge live={status.live}>{status.label}</Badge></span>
                </Link>
              </li>
            );
          })}
        </ul>
      ))}
    </>
  );
}

export function RealOrderDetailPage() {
  const { code = "" } = useParams();
  const order = useLoad<ApiOrder>((signal) => getOrder(code, signal), code);

  if (order.state === "loading") return <p>Carregando o pedido…</p>;
  if (order.state === "error") return <Feedback message={order.message} />;

  const data = order.data;
  const status = STATUS_LABEL[data.status];

  return (
    <>
      <title>{`Pedido ${data.code} · Ntix`}</title>
      <Link className="back-link" to="/conta/pedidos">Pedidos</Link>
      <header className="order-head">
        <div>
          <h1 className="n-display n-display--sm">{data.event.title}</h1>
          <p>{fullDate(Date.parse(data.event.startsAt))} · {data.event.venueName}, {data.event.city}</p>
          <Badge live={status.live}>{status.label}</Badge>
        </div>
      </header>

      <section aria-labelledby="order-items-title">
        <h2 className="tile__title" id="order-items-title">Itens</h2>
        <ul>
          {data.items.map((item) => (
            <li key={item.batchId}>{item.quantity} × {item.batchName} · {formatCents(item.totalCents)}</li>
          ))}
        </ul>
        <p><strong className="n-figure">Total {formatCents(data.totalCents)}</strong></p>
      </section>

      {data.status === "PENDING_PAYMENT" && (
        <p className="signup__note">Pagamento pendente. O Pix deste pedido segue o fluxo do checkout.</p>
      )}

      {data.tickets.length > 0 && (
        <section aria-labelledby="order-tickets-title">
          <h2 className="tile__title" id="order-tickets-title">Ingressos deste pedido</h2>
          <ul>
            {data.tickets.map((ticket) => (
              <li key={ticket.code}>
                <Link to={`/conta/ingressos/${ticket.code}`}>
                  <span className="n-figure">{ticket.code}</span> · {ticket.batchName} · {ticket.holderName}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}

export function RealTicketsPage() {
  const [when, setWhen] = useState<TicketWhen>("UPCOMING");
  const tickets = useLoad((signal) => listMyTickets(when, signal), when);

  return (
    <>
      <title>Meus ingressos · Ntix</title>
      <h1 className="n-display n-display--md">Meus ingressos</h1>

      <div className="account-tabs-inline">
        <Segmented
          name="ticket-tab"
          legend="Mostrar"
          options={[{ value: "UPCOMING", label: "Próximos" }, { value: "PAST", label: "Já foram" }] as const}
          value={when}
          onChange={setWhen}
        />
      </div>

      {tickets.state === "loading" && <p>Carregando seus ingressos…</p>}
      {tickets.state === "error" && <Feedback message={tickets.message} />}
      {tickets.state === "ready" && (tickets.data.items.length === 0 ? (
        <div className="hub-empty">
          <h2 className="n-title">{when === "UPCOMING" ? "Nenhum ingresso por vir" : "Nenhum evento no histórico"}</h2>
          <p>Quando você comprar, seus ingressos aparecem aqui.</p>
          <ButtonLink to="/" arrow>Ver eventos</ButtonLink>
        </div>
      ) : (
        <ul className="ticket-list">
          {tickets.data.items.map((ticket) => <TicketRow key={ticket.code} ticket={ticket} />)}
        </ul>
      ))}
    </>
  );
}

function TicketRow({ ticket }: Readonly<{ ticket: CustomerTicketItem }>) {
  return (
    <li>
      <Link className="ticket-row" to={`/conta/ingressos/${ticket.code}`}>
        <span className="ticket-row__body">
          <strong>{ticket.event.title}</strong>
          <span>{fullDate(Date.parse(ticket.event.startsAt))} · {ticket.event.venueName}</span>
          <span className="ticket-row__meta">{ticket.batchName} · <span className="n-figure">{ticket.code}</span></span>
        </span>
        <span className="ticket-row__status">
          {ticket.status === "USED" ? <Badge>Utilizado</Badge> : <Badge live>Válido</Badge>}
        </span>
      </Link>
    </li>
  );
}

export function RealTicketDetailPage() {
  const { code = "" } = useParams();
  const ticket = useLoad<CustomerTicketDetail>((signal) => getMyTicket(code, signal), code);

  if (ticket.state === "loading") return <p>Carregando o ingresso…</p>;
  if (ticket.state === "error") return <Feedback message={ticket.message} />;

  const data = ticket.data;
  const used = data.status === "USED";

  return (
    <>
      <title>{`Ingresso ${data.code} · Ntix`}</title>
      <Link className="back-link" to="/conta/ingressos">Meus ingressos</Link>
      <div className="ticket-view">
        <article className={used ? "ticket-paper is-used" : "ticket-paper"} aria-label={`Ingresso de ${data.event.title}`}>
          <div className="ticket-paper__body">
            <h1 className="ticket-paper__title">{data.event.title}</h1>
            <dl className="ticket-paper__facts">
              <div><dt>Data</dt><dd>{fullDate(Date.parse(data.event.startsAt))}</dd></div>
              <div><dt>Local</dt><dd>{data.event.venueName}, {data.event.address}, {data.event.city}</dd></div>
              <div><dt>Ingresso</dt><dd>{data.batchName}</dd></div>
              <div><dt>Titular</dt><dd>{data.holderName}</dd></div>
              <div><dt>Classificação</dt><dd>{data.ageRating} anos</dd></div>
            </dl>
          </div>
          <div className="ticket-paper__stub">
            {used ? (
              <div className="ticket-paper__used"><Stamp>Utilizado</Stamp></div>
            ) : (
              <>
                <div className="ticket-paper__qr"><QRCodeSVG value={data.code} size={168} marginSize={2} title={`Código do ingresso ${data.code}`} /></div>
                <p>Apresente este código na entrada.</p>
              </>
            )}
            <p className="ticket-paper__code n-figure">{data.code}</p>
          </div>
        </article>

        <aside className="ticket-side">
          <dl className="ticket-side__facts">
            <div><dt>Pedido</dt><dd><Link to={`/conta/pedidos/${data.orderCode}`}>{data.orderCode}</Link></dd></div>
            <div><dt>Emitido em</dt><dd>{fullDate(Date.parse(data.issuedAt))}</dd></div>
          </dl>
        </aside>
      </div>
    </>
  );
}
