import { useMemo, useState } from "react";
import { Badge } from "../../../design-system";
import { formatCents, describeTier } from "../../../platform/money";
import { CHANNEL_LABELS, affiliateStats, customerStats, fullDate, summarize } from "../../../platform/selectors";
import type { Channel, Order } from "../../../platform/types";
import { ticketCodesOf } from "../../../platform/entry";
import { usePlatform } from "./usePlatform";

const PAGE_SIZE = 25;

function OrderDetail({ order, onClose }: { order: Order; onClose: () => void }) {
  const { data } = usePlatform();
  const event = data.events.find((candidate) => candidate.id === order.eventId);
  const batch = event?.batches.find((candidate) => candidate.id === order.batchId);
  const customer = data.customers.find((candidate) => candidate.id === order.customerId);
  const affiliate = data.affiliates.find((candidate) => candidate.id === order.affiliateId);
  const net = order.totalCents - order.feeCents - order.commissionCents;

  return (
    <section className="tile order-detail" aria-labelledby="order-detail-title">
      <div className="tile__head">
        <h2 className="tile__title n-figure" id="order-detail-title">{order.code}</h2>
        <button type="button" className="signup__back" onClick={onClose}>Fechar</button>
      </div>
      <dl className="detail-list">
        <div><dt>Evento</dt><dd>{event?.name} · {batch?.name}</dd></div>
        <div><dt>Comprador</dt><dd>{customer?.name}<br />{customer?.email}</dd></div>
        <div><dt>Canal</dt><dd>{CHANNEL_LABELS[order.channel]}{affiliate ? ` · ${affiliate.name}` : ""}</dd></div>
        <div><dt>Data</dt><dd>{fullDate(order.createdAt)}</dd></div>
      </dl>
      <div>
        <h3 className="detail-heading">De onde vem o valor</h3>
        <table className="n-ledger">
          <tbody>
            <tr><td>{order.quantity} × {formatCents(order.unitPriceCents)}</td><td className="is-number">{formatCents(order.totalCents)}</td></tr>
            <tr><td>Taxa de intermediação<small>{order.feeRule}</small></td><td className="is-number">− {formatCents(order.feeCents)}</td></tr>
            {order.commissionCents > 0 && <tr><td>Comissão de afiliado<small>{order.commissionRule}</small></td><td className="is-number">− {formatCents(order.commissionCents)}</td></tr>}
            <tr><td><strong>Líquido</strong></td><td className="is-number"><strong>{formatCents(net)}</strong></td></tr>
          </tbody>
        </table>
      </div>
      <div>
        <h3 className="detail-heading">Ingressos emitidos</h3>
        <ul className="ticket-codes n-figure">
          {ticketCodesOf(order).map((code) => <li key={code}>{code}</li>)}
        </ul>
      </div>
    </section>
  );
}

export function SalesPage() {
  const { data, hasStore } = usePlatform();
  const [eventId, setEventId] = useState("");
  const [channel, setChannel] = useState("");
  const [search, setSearch] = useState("");
  const [limit, setLimit] = useState(PAGE_SIZE);
  const [selected, setSelected] = useState<string>();

  const customers = useMemo(() => new Map(data.customers.map((customer) => [customer.id, customer])), [data.customers]);
  const eventName = (id: string) => data.events.find((event) => event.id === id)?.name ?? "";
  const channels: Channel[] = hasStore ? ["HUB", "STORE", "AFFILIATE"] : ["HUB", "AFFILIATE"];

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return data.orders
      .filter((order) => !eventId || order.eventId === eventId)
      .filter((order) => !channel || order.channel === channel)
      .filter((order) => !term || order.code.toLowerCase().includes(term) || (customers.get(order.customerId)?.name.toLowerCase().includes(term) ?? false))
      .sort((a, b) => b.createdAt - a.createdAt);
  }, [data.orders, eventId, channel, search, customers]);

  const totals = summarize(filtered);
  const selectedOrder = data.orders.find((order) => order.id === selected);

  return (
    <>
      <title>Vendas e ingressos · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Vendas e ingressos</h1>
      </header>

      <div className="filters">
        <div className="n-field">
          <label className="n-field__label" htmlFor="sales-search">Buscar</label>
          <input id="sales-search" className="n-input" type="search" placeholder="Pedido ou comprador" value={search} onChange={(event) => { setSearch(event.target.value); setLimit(PAGE_SIZE); }} />
        </div>
        <div className="n-field">
          <label className="n-field__label" htmlFor="sales-event">Evento</label>
          <select id="sales-event" className="n-input" value={eventId} onChange={(event) => { setEventId(event.target.value); setLimit(PAGE_SIZE); }}>
            <option value="">Todos</option>
            {data.events.map((event) => <option key={event.id} value={event.id}>{event.name}</option>)}
          </select>
        </div>
        <div className="n-field">
          <label className="n-field__label" htmlFor="sales-channel">Canal</label>
          <select id="sales-channel" className="n-input" value={channel} onChange={(event) => { setChannel(event.target.value); setLimit(PAGE_SIZE); }}>
            <option value="">Todos</option>
            {channels.map((value) => <option key={value} value={value}>{CHANNEL_LABELS[value]}</option>)}
          </select>
        </div>
      </div>

      <dl className="stat-row">
        <div className="tile"><dt>Pedidos</dt><dd>{totals.orders.toLocaleString("pt-BR")}</dd></div>
        <div className="tile"><dt>Ingressos</dt><dd>{totals.tickets.toLocaleString("pt-BR")}</dd></div>
        <div className="tile"><dt>Vendas brutas</dt><dd>{formatCents(totals.grossCents)}</dd></div>
        <div className="tile"><dt>Receita líquida</dt><dd>{formatCents(totals.netCents)}</dd></div>
      </dl>

      {selectedOrder && <OrderDetail order={selectedOrder} onClose={() => setSelected(undefined)} />}

      {filtered.length === 0 ? (
        <section className="tile empty-state">
          <h2 className="n-title">{data.orders.length ? "Nada encontrado" : "Nenhuma venda ainda"}</h2>
          <p>{data.orders.length ? "Tente outros filtros." : "Publique um evento e as vendas aparecem aqui."}</p>
        </section>
      ) : (
        <section className="tile" aria-label="Lista de pedidos">
          <div className="n-ledger-scroll">
            <table className="n-ledger">
              <thead>
                <tr>
                  <th scope="col">Pedido</th><th scope="col">Comprador</th><th scope="col">Evento</th><th scope="col">Canal</th><th scope="col">Data</th>
                  <th scope="col" className="is-number">Ingressos</th><th scope="col" className="is-number">Valor</th>
                </tr>
              </thead>
              <tbody>
                {filtered.slice(0, limit).map((order) => (
                  <tr key={order.id}>
                    <td><button type="button" className="table-link n-figure" onClick={() => { setSelected(order.id); window.scrollTo(0, 0); }}>{order.code}</button></td>
                    <td>{customers.get(order.customerId)?.name}</td>
                    <td>{eventName(order.eventId)}</td>
                    <td>{CHANNEL_LABELS[order.channel]}</td>
                    <td>{fullDate(order.createdAt)}</td>
                    <td className="is-number">{order.quantity}</td>
                    <td className="is-number">{formatCents(order.totalCents)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {filtered.length > limit && (
            <button type="button" className="load-more" onClick={() => setLimit((current) => current + PAGE_SIZE)}>
              Mostrar mais ({filtered.length - limit} restantes)
            </button>
          )}
        </section>
      )}
    </>
  );
}

export function CustomersPage() {
  const { data } = usePlatform();
  const [search, setSearch] = useState("");
  const [limit, setLimit] = useState(PAGE_SIZE);
  const rows = useMemo(() => customerStats(data), [data]);
  const term = search.trim().toLowerCase();
  const filtered = rows.filter((row) => !term || row.customer.name.toLowerCase().includes(term) || row.customer.email.toLowerCase().includes(term));
  const returning = rows.filter((row) => row.orders > 1).length;
  const gross = rows.reduce((sum, row) => sum + row.grossCents, 0);
  const orders = rows.reduce((sum, row) => sum + row.orders, 0);

  return (
    <>
      <title>Clientes · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Clientes</h1>
      </header>

      <dl className="stat-row">
        <div className="tile"><dt>Clientes</dt><dd>{rows.length.toLocaleString("pt-BR")}</dd></div>
        <div className="tile"><dt>Compraram mais de uma vez</dt><dd>{returning.toLocaleString("pt-BR")}</dd></div>
        <div className="tile"><dt>Valor médio por pedido</dt><dd>{formatCents(orders ? Math.round(gross / orders) : 0)}</dd></div>
      </dl>

      {rows.length === 0 ? (
        <section className="tile empty-state">
          <h2 className="n-title">Nenhum cliente ainda</h2>
          <p>Quem comprar nos seus eventos aparece aqui.</p>
        </section>
      ) : (
        <>
          <div className="filters filters--single">
            <div className="n-field">
              <label className="n-field__label" htmlFor="customer-search">Buscar</label>
              <input id="customer-search" className="n-input" type="search" placeholder="Nome ou e-mail" value={search} onChange={(event) => { setSearch(event.target.value); setLimit(PAGE_SIZE); }} />
            </div>
          </div>
          <section className="tile" aria-label="Lista de clientes">
            <div className="n-ledger-scroll">
              <table className="n-ledger">
                <thead>
                  <tr><th scope="col">Cliente</th><th scope="col">Última compra</th><th scope="col" className="is-number">Pedidos</th><th scope="col" className="is-number">Ingressos</th><th scope="col" className="is-number">Total</th></tr>
                </thead>
                <tbody>
                  {filtered.slice(0, limit).map((row) => (
                    <tr key={row.customer.id}>
                      <td>{row.customer.name}<small>{row.customer.email}</small></td>
                      <td>{fullDate(row.lastAt)}</td>
                      <td className="is-number">{row.orders}</td>
                      <td className="is-number">{row.tickets}</td>
                      <td className="is-number">{formatCents(row.grossCents)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {filtered.length > limit && (
              <button type="button" className="load-more" onClick={() => setLimit((current) => current + PAGE_SIZE)}>
                Mostrar mais ({filtered.length - limit} restantes)
              </button>
            )}
          </section>
        </>
      )}
    </>
  );
}

export function AffiliatesPage() {
  const { data } = usePlatform();
  const rows = useMemo(() => affiliateStats(data), [data]);
  const totals = summarize(data.orders.filter((order) => order.channel === "AFFILIATE"));
  const events = data.events.filter((event) => event.allowsAffiliates);

  return (
    <>
      <title>Afiliados · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Afiliados</h1>
        <p className="n-lead">Promotores independentes que divulgam seus eventos com links rastreáveis e recebem comissão pelo que vendem.</p>
      </header>

      <dl className="stat-row">
        <div className="tile"><dt>Vendas por afiliados</dt><dd>{formatCents(totals.grossCents)}</dd></div>
        <div className="tile"><dt>Ingressos</dt><dd>{totals.tickets.toLocaleString("pt-BR")}</dd></div>
        <div className="tile"><dt>Comissões pagas</dt><dd>{formatCents(totals.commissionCents)}</dd></div>
      </dl>

      <div className="two-columns">
        <section className="tile" aria-labelledby="affiliate-list-title">
          <h2 className="tile__title" id="affiliate-list-title">Quem está vendendo</h2>
          {rows.length === 0 ? (
            <p className="tile__note">Nenhum afiliado vendeu ainda.</p>
          ) : (
            <div className="n-ledger-scroll">
              <table className="n-ledger">
                <thead>
                  <tr><th scope="col">Afiliado</th><th scope="col" className="is-number">Ingressos</th><th scope="col" className="is-number">Vendas</th><th scope="col" className="is-number">Comissão</th></tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.affiliate.id}>
                      <td>{row.affiliate.name}<small className="n-figure">ref={row.affiliate.shareCode}</small></td>
                      <td className="is-number">{row.tickets}</td>
                      <td className="is-number">{formatCents(row.grossCents)}</td>
                      <td className="is-number">{formatCents(row.commissionCents)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <div className="stack">
          <section className="tile" aria-labelledby="commission-title">
            <h2 className="tile__title" id="commission-title">Planos de comissão</h2>
            {data.commissionPlans.map((plan) => (
              <div key={plan.id}>
                <p><strong>{plan.name}</strong></p>
                <ul className="tier-list">
                  {plan.tiers.map((tier, index) => <li key={index}>{describeTier(tier, plan.tiers[index - 1])}</li>)}
                </ul>
              </div>
            ))}
          </section>
          <section className="tile" aria-labelledby="affiliate-events-title">
            <h2 className="tile__title" id="affiliate-events-title">Eventos abertos a afiliados</h2>
            {events.length === 0 ? (
              <p className="tile__note">Nenhum evento aceita afiliados. Ative em cada evento.</p>
            ) : (
              <ul className="rank-list">
                {events.map((event) => <li key={event.id}><span>{event.name}</span><Badge live={event.status === "ON_SALE"}>{event.status === "ON_SALE" ? "Em venda" : event.status === "DRAFT" ? "Rascunho" : "Encerrado"}</Badge></li>)}
              </ul>
            )}
          </section>
        </div>
      </div>
    </>
  );
}
