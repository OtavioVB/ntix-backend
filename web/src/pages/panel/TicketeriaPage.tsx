import { useState } from "react";
import { Link } from "react-router-dom";
import { Badge, Beam, Button, ButtonLink, Icon, Scene, TextField, brandVars } from "../../design-system";
import { StoreControls, StorePreview } from "../../features/store/StoreBuilder";
import { contractTicketeria } from "../../platform/actions";
import { TICKETERIA_MONTHLY_CENTS } from "../../platform/demoData";
import { ticketeriaInsights } from "../../platform/insights";
import { formatCents, parseCents } from "../../platform/money";
import { channelTotals, summarize } from "../../platform/selectors";
import { slugify } from "../../producer/text";
import { useProducerSession } from "../../producer/session";

const COMPARISON = [
  { label: "Endereço", hub: "Dentro do hub da Ntix", own: "O domínio da sua produtora" },
  { label: "Identidade visual", hub: "A da Ntix", own: "Seu nome, suas cores" },
  { label: "Eventos na tela", hub: "Os de todos os produtores", own: "Só os seus" },
  { label: "Quem o comprador vê", hub: "A Ntix", own: "A sua produtora" },
  { label: "Login do comprador", hub: "No hub", own: "A partir da sua ticketeria" },
];

function Simulator() {
  const [tickets, setTickets] = useState(342);
  const [priceText, setPriceText] = useState("120,00");
  const priceCents = parseCents(priceText);
  const insights = ticketeriaInsights(tickets, priceCents ?? 0);

  return (
    <section className="tile" aria-labelledby="simulator-title">
      <h2 className="tile__title" id="simulator-title">Faça as contas com os seus números</h2>
      <div className="simulator">
        <div className="simulator__inputs">
          <div className="n-field">
            <label className="n-field__label" htmlFor="sim-tickets">Ingressos por mês: <b className="n-figure">{tickets}</b></label>
            <input
              id="sim-tickets"
              className="simulator__range"
              type="range"
              min={50}
              max={3000}
              step={10}
              value={tickets}
              onChange={(event) => setTickets(Number(event.target.value))}
            />
          </div>
          <TextField
            id="sim-price"
            label="Preço médio do ingresso (R$)"
            inputMode="decimal"
            className="n-figure"
            value={priceText}
            error={priceCents === null ? "Informe um valor, como 120,00." : undefined}
            onChange={(event) => setPriceText(event.target.value)}
          />
        </div>
        <dl className="simulator__results" aria-live="polite">
          <div>
            <dt>Custa por ingresso</dt>
            <dd>{formatCents(insights.costPerTicketCents)}</dd>
          </div>
          <div>
            <dt>Da sua receita mensal</dt>
            <dd>{insights.shareOfRevenue.toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%</dd>
          </div>
          <div>
            <dt>Investimento no ano</dt>
            <dd>{formatCents(insights.yearlyCents)}</dd>
          </div>
        </dl>
      </div>
      <p className="simulator__insight">
        <Icon name="percent" />
        <span>
          Com uma comissão de afiliado de 5%, <strong>{insights.ticketsToBreakEven} ingressos por mês</strong> vendidos direto na sua
          ticketeria, em vez de por afiliados, economizam o valor da mensalidade.
        </span>
      </p>
      <p className="tile__note">Mensalidade e comissão são valores de exemplo. A conta usa só os números acima e não promete aumento de vendas.</p>
    </section>
  );
}

/** Quem contratou: a ticketeria no ar, com a marca editável. */
function ActiveTicketeria() {
  const { session, update } = useProducerSession();
  const orders = session.platform?.orders ?? [];
  const storeOrders = orders.filter((order) => order.channel === "STORE");
  const summary = summarize(storeOrders);
  const total = summarize(orders).tickets;
  const share = total ? Math.round((channelTotals(orders).find((channel) => channel.channel === "STORE")!.tickets / total) * 100) : 0;

  return (
    <>
      <title>Ticketeria · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Sua ticketeria</h1>
        <p className="n-lead">Mude o nome e as cores quando quiser. O comprador vê a mudança na hora.</p>
      </header>
      <dl className="stat-row">
        <div className="tile"><dt>Endereço</dt><dd className="stat-row__text">{slugify(session.name) || "sua-produtora"}.com.br</dd></div>
        <div className="tile"><dt>Vendas brutas pela ticketeria</dt><dd>{formatCents(summary.grossCents)}</dd></div>
        <div className="tile"><dt>Ingressos vendidos</dt><dd>{summary.tickets}</dd></div>
        <div className="tile"><dt>Das suas vendas totais</dt><dd>{share}%</dd></div>
      </dl>
      <div className="store-page">
        <div className="store-page__controls">
          <StoreControls name={session.name} onNameChange={(name) => update({ name })} look={session.storeLook} onLookChange={(storeLook) => update({ storeLook })} />
        </div>
        <StorePreview name={session.name} look={session.storeLook} />
      </div>
    </>
  );
}

/** Conversão: mostra como ficaria e faz as contas. A ticketeria não vem para todo produtor. */
function TicketeriaOffer() {
  const { session, update } = useProducerSession();
  const isActive = session.status === "ACTIVE";
  const selected = session.ticketeria === "SELECTED";
  const inReview = session.status === "PENDING_APPROVAL";
  const price = formatCents(TICKETERIA_MONTHLY_CENTS);

  function contractNow() {
    if (!session.platform) return;
    update({ ticketeria: "ACTIVE", platform: contractTicketeria(session.platform, Date.now()) });
  }

  return (
    <>
      <title>Ticketeria própria · Ntix</title>

      <Scene light="palco" fixed className="offer" aria-labelledby="offer-title">
        <div className="n-backdrop">
          <Beam x="70%" width="44vw" tilt={-8} swing={6} power={20} sway={16} />
          <Beam x="96%" width="30vw" tilt={-14} swing={5} power={14} sway={13} />
        </div>
        <div className="offer__grid">
          <div className="offer__copy">
            <Badge>Serviço opcional</Badge>
            <h1 className="n-display n-display--md" id="offer-title">Vá além do hub. Venda na sua própria ticketeria.</h1>
            <p className="n-lead">
              Todo produtor vende no hub da Ntix, ao lado de todos os outros. Com a ticketeria, o seu público compra em um
              endereço com o seu nome, as suas cores e só os seus eventos.
            </p>
            <div className="offer__price">
              <p className="offer__value">{price}<small>por mês</small></p>
              <Badge>Valor de exemplo</Badge>
            </div>
            {isActive && <Button size="lg" arrow onClick={contractNow}>Contratar agora</Button>}
            {!isActive && inReview && (
              <p className="inline-note">
                {selected ? "Ela já está incluída no cadastro em análise e entra no ar com a aprovação." : "Seu cadastro está em análise. Depois da aprovação, você pode contratar por aqui."}
              </p>
            )}
            {!isActive && !inReview && !selected && (
              <Button size="lg" arrow onClick={() => update({ ticketeria: "SELECTED" })}>Quero a minha ticketeria</Button>
            )}
            {!isActive && !inReview && selected && (
              <div className="offer__selected">
                <p><Icon name="check" /> <strong>Incluída na sua contratação.</strong> Ela entra no ar com a aprovação.</p>
                <ButtonLink to="/painel/aprovacao" size="lg" arrow>Ir para a aprovação</ButtonLink>
                <button type="button" className="signup__back" onClick={() => update({ ticketeria: "NONE" })}>Remover da contratação</button>
              </div>
            )}
          </div>
          <div style={brandVars("#c99b14")}>
            <p className="n-label offer__preview-label">Como a sua ficaria</p>
            <StorePreview name={session.name} look={session.storeLook} />
            <p className="brand-lab__caption">Evento e preços são exemplos ilustrativos.</p>
          </div>
        </div>
      </Scene>

      <div className="offer-details">
        <Simulator />

        <section className="tile" aria-labelledby="compare-title">
          <h2 className="tile__title" id="compare-title">O que muda em relação ao hub</h2>
          <div className="n-ledger-scroll">
            <table className="n-ledger">
              <thead>
                <tr><th scope="col"><span className="n-visually-hidden">Item</span></th><th scope="col">Só no hub</th><th scope="col">Com ticketeria</th></tr>
              </thead>
              <tbody>
                {COMPARISON.map((row) => (
                  <tr key={row.label}>
                    <th scope="row">{row.label}</th>
                    <td>{row.hub}</td>
                    <td><strong>{row.own}</strong></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="tile__note">Seus eventos continuam no hub também. A ticketeria é um canal a mais, não um substituto.</p>
        </section>

        <section className="tile" aria-labelledby="try-title">
          <h2 className="tile__title" id="try-title">Teste com a sua marca</h2>
          <div className="offer-try">
            <StoreControls name={session.name} onNameChange={(name) => update({ name })} look={session.storeLook} onLookChange={(storeLook) => update({ storeLook })} />
          </div>
          <p className="tile__note">A prévia lá em cima muda na hora. <Link to="/painel/equipe">Prefere chamar a equipe para opinar?</Link></p>
        </section>
      </div>
    </>
  );
}

export function TicketeriaPage() {
  const { session } = useProducerSession();
  return session.ticketeria === "ACTIVE" ? <ActiveTicketeria /> : <TicketeriaOffer />;
}
