import { useMemo, useState, type CSSProperties } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { Badge, Button, Icon, Scene, Stepper } from "../../design-system";
import { itemsFromSelection, orderTotal, ticketCount } from "../../customer/orders";
import { CART_MINUTES, useCustomer } from "../../customer/session";
import type { HubBatch, HubEvent } from "../../hub/catalog";
import { MAX_TICKETS_PER_ORDER, batchState, eventDay, maxSelectable, priceFrom, remaining, scarcity, type BatchState } from "../../hub/rules";
import { useHub } from "../../hub/useHub";
import { formatCents } from "../../platform/money";
import { EventCard, EventImage, PriceFrom, Rail } from "./parts";

const STATE_LABELS: Record<Exclude<BatchState, "ON_SALE">, string> = {
  SOLD_OUT: "Esgotado",
  UPCOMING: "Em breve",
  CLOSED: "Vendas encerradas",
};

function BatchRow({ batch, state, left, value, max, onChange, opensAt }: { batch: HubBatch; state: BatchState; left: number; value: number; max: number; onChange: (value: number) => void; opensAt: string }) {
  const onSale = state === "ON_SALE";
  return (
    <li className={onSale ? "batch-ticket" : "batch-ticket is-off"}>
      <div className="batch-ticket__info">
        <h3>{batch.name}</h3>
        {batch.note && <p>{batch.note}</p>}
        {onSale && left <= 20 && <p className="batch-ticket__left">Restam {left}</p>}
        {state === "UPCOMING" && <p>Abre em {opensAt}</p>}
      </div>
      <div className="batch-ticket__buy">
        <strong className="n-figure">{formatCents(batch.priceCents)}</strong>
        {onSale ? <Stepper label={batch.name} value={value} max={max} onChange={onChange} /> : <Badge>{STATE_LABELS[state]}</Badge>}
      </div>
    </li>
  );
}

function EventBody({ event }: { event: HubEvent }) {
  const { now, extra, events } = useHub();
  const { update } = useCustomer();
  const navigate = useNavigate();
  const { state } = useLocation();
  const [selection, setSelection] = useState<Record<string, number>>({});

  const day = eventDay(event.date);
  const items = useMemo(() => itemsFromSelection(event, selection), [event, selection]);
  const total = orderTotal(items);
  const count = ticketCount(items);
  const flag = scarcity(event, now, extra);
  const isPast = event.date < now;
  const related = events.filter((other) => other.id !== event.id && other.date >= now && (other.category === event.category || other.city === event.city)).slice(0, 8);
  const expired = Boolean((state as { cartExpired?: boolean } | null)?.cartExpired);

  function continueToCheckout() {
    update({ cart: { eventId: event.id, slug: event.slug, selection, expiresAt: Date.now() + CART_MINUTES * 60_000 } });
    navigate(`/eventos/${event.slug}/checkout`);
  }

  const style = { "--event-accent": event.accent } as CSSProperties;

  return (
    <div className="event-page" style={style}>
      <title>{`${event.title} · Ntix`}</title>
      <meta name="description" content={`${event.tagline}. ${day.full}, ${event.venue}, ${event.city}.`} />

      <Scene light="palco" fixed className="event-hero">
        <div className="event-hero__bg" aria-hidden="true"><EventImage event={event} eager /></div>
        <div className="event-hero__scrim" />
        <div className="n-container event-hero__grid">
          <div className="event-hero__poster"><EventImage event={event} eager /></div>
          <div className="event-hero__copy">
            <p className="n-label n-label--accent">{event.category}</p>
            <h1 className="n-display n-display--lg">{event.title}</h1>
            <p className="n-lead">{event.tagline}</p>
            <dl className="event-facts">
              <div><dt><Icon name="calendar" /><span className="n-visually-hidden">Data</span></dt><dd>{day.full}<small>Portões {event.doorsAt} · Início {event.startsAt}</small></dd></div>
              <div><dt><Icon name="store" /><span className="n-visually-hidden">Local</span></dt><dd>{event.venue}<small>{event.city}{event.state ? `, ${event.state}` : ""}</small></dd></div>
              <div><dt><Icon name="team" /><span className="n-visually-hidden">Classificação</span></dt><dd>Classificação: {event.ageRating}<small>Por {event.producer}</small></dd></div>
            </dl>
            {!isPast && (
              <div className="event-hero__cta">
                <a className="n-btn n-btn--primary n-btn--lg" href="#ingressos">Escolher ingressos</a>
                <PriceFrom event={event} now={now} extra={extra} />
              </div>
            )}
          </div>
        </div>
      </Scene>

      <Scene light="penumbra" className="event-main">
        <div className="n-container event-main__grid">
          <div className="event-main__content">
            <section className="event-section" id="ingressos" aria-labelledby="tickets-title">
              <div className="event-section__head">
                <h2 className="n-display n-display--sm" id="tickets-title">Ingressos</h2>
                {flag === "LAST" && <Badge live>Últimos ingressos</Badge>}
              </div>
              {expired && <p className="event-notice" role="alert">Sua reserva expirou e os ingressos voltaram para a venda. Escolha de novo para continuar.</p>}
              {isPast ? (
                <p className="event-notice">Este evento já aconteceu.</p>
              ) : (
                <>
                  <ul className="batch-tickets">
                    {event.batches.map((batch) => {
                      const selectedElsewhere = count - (selection[batch.id] ?? 0);
                      return (
                        <BatchRow
                          key={batch.id}
                          batch={batch}
                          state={batchState(batch, now, extra)}
                          left={remaining(batch, extra)}
                          value={selection[batch.id] ?? 0}
                          max={maxSelectable(batch, selectedElsewhere, extra)}
                          opensAt={eventDay(batch.startsAt).full}
                          onChange={(value) => setSelection((current) => ({ ...current, [batch.id]: value }))}
                        />
                      );
                    })}
                  </ul>
                  <p className="event-small">Até {MAX_TICKETS_PER_ORDER} ingressos por pedido. Cada ingresso tem um código único, disponível na sua conta depois do pagamento.</p>
                </>
              )}
            </section>

            <section className="event-section" aria-labelledby="about-title">
              <h2 className="n-display n-display--sm" id="about-title">Sobre o evento</h2>
              <div className="event-text">{event.description.map((paragraph) => <p key={paragraph}>{paragraph}</p>)}</div>
              {event.included.length > 0 && (
                <ul className="event-included">
                  {event.included.map((item) => <li key={item}><Icon name="check" /> {item}</li>)}
                </ul>
              )}
            </section>

            {event.schedule.length > 0 && (
              <section className="event-section" aria-labelledby="schedule-title">
                <h2 className="n-display n-display--sm" id="schedule-title">Programação</h2>
                <ol className="event-schedule">
                  {event.schedule.map((item) => (
                    <li key={item.time + item.title}><span className="n-figure">{item.time}</span><span>{item.title}</span></li>
                  ))}
                </ol>
              </section>
            )}

            <section className="event-section" aria-labelledby="place-title">
              <h2 className="n-display n-display--sm" id="place-title">Local</h2>
              <p className="event-place"><strong>{event.venue}</strong><br />{event.address}<br />{event.city}{event.state ? `, ${event.state}` : ""}</p>
              <p className="event-small">Organizado por {event.producer}. A Ntix é a plataforma de venda dos ingressos.</p>
            </section>
          </div>

          {!isPast && (
            <aside className="purchase-box" aria-label="Resumo da compra">
              <p className="n-label n-label--accent">{day.day} {day.month} · {event.city}</p>
              <p className="purchase-box__title">{event.title}</p>
              {count === 0 ? (
                <p className="purchase-box__empty">
                  {priceFrom(event, now, extra) === null ? "Nenhum ingresso à venda agora." : "Escolha seus ingressos para continuar."}
                </p>
              ) : (
                <ul className="purchase-box__items">
                  {items.map((item) => (
                    <li key={item.batchId}><span>{item.quantity} × {item.name}</span><b className="n-figure">{formatCents(item.quantity * item.unitPriceCents)}</b></li>
                  ))}
                </ul>
              )}
              <div className="purchase-box__total"><span>Total</span><strong>{formatCents(total)}</strong></div>
              <Button block size="lg" arrow disabled={count === 0} onClick={continueToCheckout}>Continuar</Button>
              <p className="event-small">Sua seleção fica reservada por {CART_MINUTES} minutos na próxima etapa.</p>
            </aside>
          )}
        </div>

        {related.length > 0 && (
          <div className="n-container">
            <Rail title="Você também pode gostar">
              {related.map((other) => <EventCard key={other.id} event={other} now={now} extra={extra} />)}
            </Rail>
          </div>
        )}
      </Scene>

      {!isPast && count > 0 && (
        <div className="purchase-bar">
          <div>
            <small>{count === 1 ? "1 ingresso" : `${count} ingressos`}</small>
            <strong>{formatCents(total)}</strong>
          </div>
          <Button arrow onClick={continueToCheckout}>Continuar</Button>
        </div>
      )}
    </div>
  );
}

export function EventPage() {
  const { slug } = useParams();
  const { findBySlug } = useHub();
  const event = findBySlug(slug);

  if (!event) {
    return (
      <Scene light="penumbra" className="hub-notfound">
        <title>Evento não encontrado · Ntix</title>
        <div className="n-container">
          <h1 className="n-display n-display--md">Este evento saiu de cartaz.</h1>
          <p className="n-lead">Ele pode ter terminado ou mudado de endereço. <Link to="/">Veja o que está em cartaz</Link>.</p>
        </div>
      </Scene>
    );
  }

  // A chave zera a seleção ao trocar de um evento para outro.
  return <EventBody key={event.id} event={event} />;
}
