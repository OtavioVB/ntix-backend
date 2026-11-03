import { Link } from "react-router-dom";
import { Badge, Curtain, Icon, Meter, Reveal } from "../../design-system";
import { useProducerSession } from "../../producer/session";
import { RevenueChart } from "./RevenueChart";
import { CHANNELS, SALES_DAYS, SAMPLE_EVENT, SUMMARY, money } from "./sampleData";

const TOP_AFFILIATES = [
  { name: "Afiliado 1", tickets: 31 },
  { name: "Afiliado 2", tickets: 17 },
  { name: "Afiliado 3", tickets: 10 },
];

function LockedCard({ title, text }: { title: string; text: string }) {
  return (
    <>
      <p className="locked-title"><Icon name="lock" /> {title}</p>
      <p className="locked-text">{text}</p>
      <Link className="locked-link" to="/painel/aprovacao">Ver o que falta para liberar</Link>
    </>
  );
}

export function OverviewPage() {
  const { session } = useProducerSession();
  const firstName = session.name.trim();
  const maxChannel = Math.max(...CHANNELS.map((channel) => channel.tickets));
  const capacity = SAMPLE_EVENT.batches.reduce((sum, batch) => sum + batch.total, 0);

  return (
    <>
      <title>Visão geral · Ntix</title>
      <header className="page-head">
        <Reveal immediate as="h1" className="n-display n-display--md">
          {firstName ? `Ensaio geral, ${firstName}.` : "Ensaio geral."}
        </Reveal>
        <p className="n-lead">É assim que a sua operação vai aparecer aqui. Explore à vontade: nada do que está nesta tela é real ainda.</p>
      </header>

      <div className="overview">
        <section className="tile overview__revenue" aria-labelledby="revenue-title">
          <div className="tile__head">
            <h2 className="tile__title" id="revenue-title">Receita líquida</h2>
            <Badge>Exemplo · 30 dias</Badge>
          </div>
          <p className="hero-figure">{money(SUMMARY.net)}</p>
          <dl className="figures">
            <div><dt>Vendas brutas</dt><dd>{money(SUMMARY.gross)}</dd></div>
            <div><dt>Ingressos vendidos</dt><dd>{SUMMARY.tickets}</dd></div>
            <div><dt>Taxa de intermediação</dt><dd>{money(SUMMARY.fee)}</dd></div>
            <div><dt>Comissão de afiliados</dt><dd>{money(SUMMARY.commission)}</dd></div>
          </dl>
          <RevenueChart days={SALES_DAYS} />
        </section>

        <section className="tile overview__event" aria-labelledby="event-title">
          <div className="tile__head">
            <h2 className="tile__title" id="event-title">Próximo evento</h2>
            <Badge>Exemplo</Badge>
          </div>
          <p className="event-name">{SAMPLE_EVENT.name}</p>
          <p className="event-meta">{SAMPLE_EVENT.when} · {SAMPLE_EVENT.place}</p>
          <p className="event-countdown"><b>{SAMPLE_EVENT.daysLeft}</b> dias para abrir as portas</p>
          <ul className="batch-list">
            {SAMPLE_EVENT.batches.map((batch) => (
              <li key={batch.name}>
                <div className="batch-list__head">
                  <span>{batch.name}</span>
                  <Badge live={batch.live}>{batch.status}</Badge>
                </div>
                <Meter value={batch.sold} max={batch.total} label={`${batch.sold} de ${batch.total} ingressos vendidos`} />
                <p className="batch-list__meta n-figure">{batch.sold} de {batch.total}</p>
              </li>
            ))}
          </ul>
          <p className="event-total">{SUMMARY.tickets} de {capacity} ingressos vendidos no total</p>
        </section>

        <section className="tile overview__channels" aria-labelledby="channels-title">
          <div className="tile__head">
            <h2 className="tile__title" id="channels-title">De onde vêm as vendas</h2>
            <Badge>Exemplo</Badge>
          </div>
          <ul className="channel-bars">
            {CHANNELS.map((channel) => (
              <li key={channel.name}>
                <span>{channel.name}</span>
                <div className="channel-bars__track">
                  <i style={{ width: `${(channel.tickets / maxChannel) * 100}%` }} />
                  <b className="n-figure">{channel.tickets}</b>
                </div>
              </li>
            ))}
          </ul>
          <p className="tile__note">Ingressos por canal. Cada pedido registra por onde a venda aconteceu.</p>
          <Link className="upsell-row" to="/painel/ticketeria">
            <Icon name="store" />
            <span><strong>Um terceiro canal:</strong> sua ticketeria própria. Veja como ficaria.</span>
          </Link>
        </section>

        <div className="overview__locked-a">
          <Curtain
            compact
            preview={
              <ul className="preview-list">
                {TOP_AFFILIATES.map((affiliate) => (
                  <li key={affiliate.name}><span>{affiliate.name}</span><b>{affiliate.tickets}</b></li>
                ))}
              </ul>
            }
          >
            <LockedCard title="Afiliados que mais vendem" text="Quem está trazendo público e quanto cada um gerou." />
          </Curtain>
        </div>

        <div className="overview__locked-b">
          <Curtain
            compact
            preview={
              <ul className="preview-list">
                <li><span>Clientes novos</span><b>214</b></li>
                <li><span>Compraram de novo</span><b>38</b></li>
                <li><span>Ticket médio</span><b>R$ 102</b></li>
              </ul>
            }
          >
            <LockedCard title="Quem é o seu público" text="Clientes novos, quem voltou a comprar e o ticket médio." />
          </Curtain>
        </div>
      </div>
    </>
  );
}
