import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Badge, ButtonLink, Icon, Meter, Segmented } from "../../../design-system";
import { formatCents } from "../../../platform/money";
import {
  CHANNEL_LABELS,
  affiliateStats,
  attentionItems,
  channelTotals,
  currentBalance,
  eventStats,
  fullDate,
  percentChange,
  periodSummary,
  salesSeries,
} from "../../../platform/selectors";
import { useProducerSession } from "../../../producer/session";
import { RevenueChart } from "../RevenueChart";
import { usePlatform } from "./usePlatform";
import { panelPath } from "../../../producer/paths";

const PERIODS = [
  { value: "7", label: "7 dias" },
  { value: "30", label: "30 dias" },
  { value: "90", label: "90 dias" },
] as const;

type Period = (typeof PERIODS)[number]["value"];

function Delta({ current, previous, days }: { current: number; previous: number; days: number }) {
  const change = percentChange(current, previous);
  if (change === null) return <span className="delta">Sem base de comparação</span>;
  const rounded = Math.round(change);
  const direction = rounded > 0 ? "is-up" : rounded < 0 ? "is-down" : "";
  const sign = rounded > 0 ? "▲ " : rounded < 0 ? "▼ " : "";
  return (
    <span className={`delta ${direction}`}>
      {sign}{Math.abs(rounded)}% <small>vs. {days} dias anteriores</small>
    </span>
  );
}

function FirstSteps() {
  const { session } = useProducerSession();
  const { data } = usePlatform();
  const hasEvent = data.events.length > 0;
  const hasBatch = data.events.some((event) => event.batches.length > 0);
  const published = data.events.some((event) => event.status === "ON_SALE");
  const steps = [
    { done: hasEvent, text: "Crie seu primeiro evento", to: panelPath("eventos/novo") },
    { done: hasBatch, text: "Adicione os lotes de ingresso", to: hasEvent ? panelPath(`eventos/${data.events[0].id}`) : panelPath("eventos") },
    { done: published, text: "Publique para começar a vender no hub", to: hasEvent ? panelPath(`eventos/${data.events[0].id}`) : panelPath("eventos") },
    { done: session.team.length > 1, text: "Convide sua equipe", to: panelPath("equipe") },
  ];
  const next = steps.find((step) => !step.done);

  return (
    <section className="tile first-steps" aria-labelledby="first-steps-title">
      <h2 className="n-display n-display--sm" id="first-steps-title">Cortinas abertas. Hora da estreia.</h2>
      <p className="n-lead">Sua produtora está ativa. Quando as vendas começarem, seus números aparecem aqui.</p>
      <ol className="first-steps__list">
        {steps.map((step) => (
          <li key={step.text} className={step.done ? "is-done" : undefined}>
            <Icon name={step.done ? "check" : "calendar"} />
            {step.done ? <span>{step.text}</span> : <Link to={step.to}>{step.text}</Link>}
          </li>
        ))}
      </ol>
      {next && <ButtonLink to={next.to} arrow>{next.text}</ButtonLink>}
    </section>
  );
}

export function DashboardPage() {
  const { session } = useProducerSession();
  const { data, hasStore } = usePlatform();
  const [period, setPeriod] = useState<Period>("30");
  const days = Number(period);
  const now = useMemo(() => Date.now(), []);

  const { orders, current, previous } = useMemo(() => periodSummary(data.orders, now, days), [data.orders, now, days]);
  const series = useMemo(() => salesSeries(orders, now, days), [orders, now, days]);
  const channels = useMemo(() => channelTotals(orders).filter((channel) => hasStore || channel.channel !== "STORE"), [orders, hasStore]);
  const attention = useMemo(() => attentionItems(data, now), [data, now]);
  const topAffiliates = useMemo(() => affiliateStats(data).slice(0, 4), [data]);
  const maxChannel = Math.max(1, ...channels.map((channel) => channel.tickets));
  const upcoming = data.events.filter((event) => event.status === "ON_SALE").sort((a, b) => a.date - b.date);
  const recent = [...orders].sort((a, b) => b.createdAt - a.createdAt).slice(0, 6);
  const eventName = (id: string) => data.events.find((event) => event.id === id)?.name ?? "";
  const hasSales = data.orders.length > 0;

  return (
    <>
      <title>Visão geral · Ntix</title>
      <header className="page-head page-head--action">
        <div>
          <h1 className="n-display n-display--md">{session.name || "Visão geral"}</h1>
        </div>
        {hasSales && (
          <div className="period-filter">
            <Segmented name="period" legend="Período" options={PERIODS} value={period} onChange={setPeriod} />
          </div>
        )}
      </header>

      {!hasSales ? (
        <FirstSteps />
      ) : (
        <div className="overview">
          <section className="tile overview__revenue" aria-labelledby="net-title">
            <div className="tile__head">
              <h2 className="tile__title" id="net-title">Receita líquida</h2>
              <Delta current={current.netCents} previous={previous.netCents} days={days} />
            </div>
            <p className="hero-figure">{formatCents(current.netCents)}</p>
            <dl className="figures">
              <div><dt>Vendas brutas</dt><dd>{formatCents(current.grossCents)}</dd><Delta current={current.grossCents} previous={previous.grossCents} days={days} /></div>
              <div><dt>Ingressos vendidos</dt><dd>{current.tickets.toLocaleString("pt-BR")}</dd><Delta current={current.tickets} previous={previous.tickets} days={days} /></div>
              <div><dt>Pedidos</dt><dd>{current.orders.toLocaleString("pt-BR")}</dd><Delta current={current.orders} previous={previous.orders} days={days} /></div>
              <div><dt>Taxas e comissões</dt><dd>{formatCents(current.feeCents + current.commissionCents)}</dd></div>
            </dl>
            <RevenueChart days={series} peakLabel={days > 30 ? "Melhor semana" : "Melhor dia"} />
          </section>

          <div className="overview__side">
            <section className="tile" aria-labelledby="balance-title">
              <h2 className="tile__title" id="balance-title">Saldo na carteira</h2>
              <p className="side-figure">{formatCents(currentBalance(data))}</p>
              <Link className="locked-link" to={panelPath("carteira")}>Ver extrato e sacar</Link>
            </section>

            <section className="tile" aria-labelledby="attention-title">
              <h2 className="tile__title" id="attention-title">Pede sua atenção</h2>
              {attention.length === 0 ? (
                <p className="tile__note">Nada pendente agora.</p>
              ) : (
                <ul className="attention-list">
                  {attention.map((item) => (
                    <li key={item.text}><Link to={item.to}>{item.text}</Link></li>
                  ))}
                </ul>
              )}
            </section>
          </div>

          <section className="tile overview__half" aria-labelledby="channels-title">
            <h2 className="tile__title" id="channels-title">De onde vêm as vendas</h2>
            <ul className="channel-bars">
              {channels.map((channel) => (
                <li key={channel.channel}>
                  <span>{CHANNEL_LABELS[channel.channel]}</span>
                  <div className="channel-bars__track">
                    <i style={{ width: `${(channel.tickets / maxChannel) * 100}%` }} />
                    <b className="n-figure">{channel.tickets}</b>
                  </div>
                </li>
              ))}
            </ul>
            <p className="tile__note">Ingressos por canal no período.</p>
            {!hasStore && (
              <Link className="upsell-row" to={panelPath("ticketeria")}>
                <Icon name="store" />
                <span><strong>Um canal só seu:</strong> veja como ficaria a sua ticketeria.</span>
              </Link>
            )}
          </section>

          <section className="tile overview__half" aria-labelledby="events-title">
            <div className="tile__head">
              <h2 className="tile__title" id="events-title">Eventos em venda</h2>
              <Link className="locked-link" to={panelPath("eventos")}>Ver todos</Link>
            </div>
            <ul className="batch-list">
              {upcoming.map((event) => {
                const stats = eventStats(data, event);
                return (
                  <li key={event.id}>
                    <div className="batch-list__head">
                      <Link to={panelPath(`eventos/${event.id}`)}>{event.name}</Link>
                      <span className="n-figure">{fullDate(event.date)}</span>
                    </div>
                    <Meter value={stats.tickets} max={Math.max(1, stats.capacity)} label={`${stats.tickets} de ${stats.capacity} ingressos vendidos`} />
                    <p className="batch-list__meta n-figure">{stats.tickets} de {stats.capacity} · {formatCents(stats.grossCents)}</p>
                  </li>
                );
              })}
            </ul>
          </section>

          <section className="tile overview__wide" aria-labelledby="recent-title">
            <div className="tile__head">
              <h2 className="tile__title" id="recent-title">Últimos pedidos</h2>
              <Link className="locked-link" to={panelPath("vendas")}>Ver todas as vendas</Link>
            </div>
            <div className="n-ledger-scroll">
              <table className="n-ledger">
                <thead>
                  <tr><th scope="col">Pedido</th><th scope="col">Evento</th><th scope="col">Canal</th><th scope="col">Data</th><th scope="col" className="is-number">Valor</th></tr>
                </thead>
                <tbody>
                  {recent.map((order) => (
                    <tr key={order.id}>
                      <td className="n-figure">{order.code}</td>
                      <td>{eventName(order.eventId)}</td>
                      <td>{CHANNEL_LABELS[order.channel]}</td>
                      <td>{fullDate(order.createdAt)}</td>
                      <td className="is-number">{formatCents(order.totalCents)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="tile overview__narrow" aria-labelledby="affiliates-title">
            <div className="tile__head">
              <h2 className="tile__title" id="affiliates-title">Afiliados que mais vendem</h2>
              <Badge>Todo o período</Badge>
            </div>
            <ul className="rank-list">
              {topAffiliates.map((row) => (
                <li key={row.affiliate.id}>
                  <span>{row.affiliate.name}</span>
                  <b className="n-figure">{row.tickets}</b>
                </li>
              ))}
            </ul>
            <Link className="locked-link" to={panelPath("afiliados")}>Ver afiliados</Link>
          </section>
        </div>
      )}
    </>
  );
}
