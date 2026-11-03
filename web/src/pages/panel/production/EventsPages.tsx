import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { Badge, Button, ButtonLink, Checkbox, Meter, TextField, Window } from "../../../design-system";
import { addBatch, createEvent, publishEvent, setAllowsAffiliates } from "../../../platform/actions";
import { formatCents, parseCents } from "../../../platform/money";
import { batchSold, eventStats, fullDate } from "../../../platform/selectors";
import type { EventStatus } from "../../../platform/types";
import { usePlatform } from "./usePlatform";
import { panelPath } from "../../../producer/paths";

const STATUS_LABELS: Record<EventStatus, string> = { DRAFT: "Rascunho", ON_SALE: "Em venda", ENDED: "Encerrado" };

function parseDate(value: string): number | null {
  const time = new Date(`${value}T12:00:00`).getTime();
  return Number.isNaN(time) ? null : time;
}

export function EventsListPage() {
  const { data } = usePlatform();
  const order: EventStatus[] = ["ON_SALE", "DRAFT", "ENDED"];
  const events = [...data.events].sort((a, b) => order.indexOf(a.status) - order.indexOf(b.status) || a.date - b.date);

  return (
    <>
      <title>Eventos e lotes · Ntix</title>
      <header className="page-head page-head--action">
        <div>
          <h1 className="n-display n-display--md">Eventos e lotes</h1>
          <p className="n-lead">Cada evento tem quantos lotes precisar, com preço, quantidade e período de venda próprios.</p>
        </div>
        <ButtonLink to={panelPath("eventos/novo")}>Criar evento</ButtonLink>
      </header>

      {events.length === 0 ? (
        <section className="tile empty-state">
          <h2 className="n-title">Nenhum evento ainda</h2>
          <p>Crie o primeiro para começar a vender no hub da Ntix.</p>
        </section>
      ) : (
        <ul className="event-cards">
          {events.map((event) => {
            const stats = eventStats(data, event);
            return (
              <li key={event.id}>
                <Link className="event-card" to={panelPath(`eventos/${event.id}`)}>
                  <div className="event-card__head">
                    <h2 className="event-card__name">{event.name}</h2>
                    <Badge live={event.status === "ON_SALE"}>{STATUS_LABELS[event.status]}</Badge>
                  </div>
                  <p className="event-card__meta">{fullDate(event.date)} · {event.venue}</p>
                  {event.batches.length > 0 ? (
                    <>
                      <Meter value={stats.tickets} max={Math.max(1, stats.capacity)} label={`${stats.tickets} de ${stats.capacity} ingressos vendidos`} />
                      <p className="event-card__numbers n-figure"><span>{stats.tickets} de {stats.capacity}</span><span>{formatCents(stats.grossCents)}</span></p>
                    </>
                  ) : (
                    <p className="event-card__meta">Sem lotes ainda</p>
                  )}
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </>
  );
}

export function EventFormPage() {
  const { data, save } = usePlatform();
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [venue, setVenue] = useState("");
  const [date, setDate] = useState("");
  const [allowsAffiliates, setAllows] = useState(false);
  const [errors, setErrors] = useState<{ name?: string; venue?: string; date?: string }>({});

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const time = parseDate(date);
    const found: typeof errors = {};
    if (name.trim().length < 2) found.name = "Informe o nome do evento.";
    if (venue.trim().length < 2) found.venue = "Informe o local.";
    if (time === null) found.date = "Informe a data.";
    else if (time < Date.now()) found.date = "A data precisa ser futura.";
    setErrors(found);
    if (found.name || found.venue || found.date || time === null) return;

    const created = createEvent(data, { name: name.trim(), venue: venue.trim(), date: time, allowsAffiliates });
    save(created.data);
    navigate(panelPath(`eventos/${created.event.id}`));
  }

  return (
    <>
      <title>Novo evento · Ntix</title>
      <header className="page-head">
        <Link className="back-link" to={panelPath("eventos")}>Eventos e lotes</Link>
        <h1 className="n-display n-display--md">Novo evento</h1>
      </header>
      <form className="tile form-tile" onSubmit={handleSubmit} noValidate>
        <TextField id="event-name" label="Nome do evento" maxLength={120} value={name} error={errors.name} onChange={(event) => setName(event.target.value)} />
        <TextField id="event-venue" label="Local" maxLength={120} value={venue} error={errors.venue} onChange={(event) => setVenue(event.target.value)} />
        <TextField id="event-date" label="Data" type="date" value={date} error={errors.date} onChange={(event) => setDate(event.target.value)} />
        <Checkbox checked={allowsAffiliates} onChange={(event) => setAllows(event.target.checked)}>
          Este evento aceita afiliados
        </Checkbox>
        <Button type="submit">Criar como rascunho</Button>
      </form>
    </>
  );
}

function BatchForm({ eventId, eventDate }: { eventId: string; eventDate: number }) {
  const { data, save } = usePlatform();
  const [name, setName] = useState("");
  const [price, setPrice] = useState("");
  const [quantity, setQuantity] = useState("");
  const [starts, setStarts] = useState("");
  const [ends, setEnds] = useState("");
  const [errors, setErrors] = useState<{ name?: string; price?: string; quantity?: string; starts?: string; ends?: string }>({});

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const priceCents = parseCents(price);
    const amount = Number(quantity);
    const startsAt = parseDate(starts);
    const endsAt = parseDate(ends);
    const found: typeof errors = {};
    if (name.trim().length < 2) found.name = "Informe o nome do lote.";
    if (priceCents === null || priceCents <= 0) found.price = "Informe um preço, como 120,00.";
    if (!Number.isInteger(amount) || amount <= 0) found.quantity = "Informe uma quantidade inteira.";
    if (startsAt === null) found.starts = "Informe o início das vendas.";
    if (endsAt === null) found.ends = "Informe o fim das vendas.";
    else if (startsAt !== null && endsAt <= startsAt) found.ends = "O fim precisa ser depois do início.";
    else if (endsAt > eventDate) found.ends = "As vendas precisam terminar até a data do evento.";
    setErrors(found);
    if (Object.keys(found).length || priceCents === null || startsAt === null || endsAt === null) return;

    save(addBatch(data, eventId, { name: name.trim(), priceCents, quantity: amount, startsAt, endsAt }));
    setName("");
    setPrice("");
    setQuantity("");
    setStarts("");
    setEnds("");
  }

  return (
    <form className="tile form-tile form-tile--grid" onSubmit={handleSubmit} noValidate aria-labelledby="batch-form-title">
      <h2 className="tile__title" id="batch-form-title">Adicionar lote</h2>
      <TextField id="batch-name" label="Nome do lote" maxLength={60} placeholder="1º lote" value={name} error={errors.name} onChange={(event) => setName(event.target.value)} />
      <TextField id="batch-price" label="Preço (R$)" inputMode="decimal" className="n-figure" value={price} error={errors.price} onChange={(event) => setPrice(event.target.value)} />
      <TextField id="batch-quantity" label="Quantidade" inputMode="numeric" className="n-figure" value={quantity} error={errors.quantity} onChange={(event) => setQuantity(event.target.value)} />
      <TextField id="batch-starts" label="Início das vendas" type="date" value={starts} error={errors.starts} onChange={(event) => setStarts(event.target.value)} />
      <TextField id="batch-ends" label="Fim das vendas" type="date" value={ends} error={errors.ends} onChange={(event) => setEnds(event.target.value)} />
      <Button type="submit" variant="ghost">Adicionar lote</Button>
    </form>
  );
}

export function EventDetailPage() {
  const { eventId } = useParams();
  const { data, save } = usePlatform();
  const event = data.events.find((candidate) => candidate.id === eventId);

  if (!event) return <Navigate to={panelPath("eventos")} replace />;

  const stats = eventStats(data, event);
  const canPublish = event.status === "DRAFT" && event.batches.length > 0;

  return (
    <>
      <title>{`${event.name} · Ntix`}</title>
      <header className="page-head page-head--action">
        <div>
          <Link className="back-link" to={panelPath("eventos")}>Eventos e lotes</Link>
          <h1 className="n-display n-display--md">{event.name}</h1>
          <p className="n-lead">{fullDate(event.date)} · {event.venue}</p>
        </div>
        {event.status === "DRAFT" && (
          <Button disabled={!canPublish} onClick={() => save(publishEvent(data, event.id))}>Publicar evento</Button>
        )}
      </header>

      <div className="detail-bar">
        <Badge live={event.status === "ON_SALE"}>{STATUS_LABELS[event.status]}</Badge>
        {event.status === "DRAFT" && !canPublish && <span className="inline-note">Adicione um lote para poder publicar.</span>}
        <Checkbox checked={event.allowsAffiliates} disabled={event.status === "ENDED"} onChange={(input) => save(setAllowsAffiliates(data, event.id, input.target.checked))}>
          Aceita afiliados
        </Checkbox>
      </div>

      <dl className="stat-row">
        <div className="tile"><dt>Ingressos vendidos</dt><dd>{stats.tickets} <small>de {stats.capacity}</small></dd></div>
        <div className="tile"><dt>Vendas brutas</dt><dd>{formatCents(stats.grossCents)}</dd></div>
        <div className="tile"><dt>Receita líquida</dt><dd>{formatCents(stats.netCents)}</dd></div>
        <div className="tile"><dt>Pedidos</dt><dd>{stats.orders}</dd></div>
      </dl>

      {event.batches.length > 0 && (
        <Window bar={<span>Lotes</span>}>
          <ul className="batch-table">
            {event.batches.map((batch) => {
              const sold = batchSold(data.orders, batch.id);
              const now = Date.now();
              const label = sold >= batch.quantity ? "Esgotado" : now >= batch.endsAt ? "Encerrado" : now < batch.startsAt ? "Agendado" : "Em venda";
              return (
                <li key={batch.id}>
                  <div className="batch-table__name">
                    <strong>{batch.name}</strong>
                    <Badge live={label === "Em venda" && event.status === "ON_SALE"}>{label}</Badge>
                  </div>
                  <Meter value={sold} max={batch.quantity} label={`${sold} de ${batch.quantity} ingressos vendidos`} />
                  <dl className="batch-table__data">
                    <div><dt>Preço</dt><dd className="n-figure">{formatCents(batch.priceCents)}</dd></div>
                    <div><dt>Vendidos</dt><dd className="n-figure">{sold} de {batch.quantity}</dd></div>
                    <div><dt>Período</dt><dd>{fullDate(batch.startsAt)} a {fullDate(batch.endsAt)}</dd></div>
                  </dl>
                </li>
              );
            })}
          </ul>
        </Window>
      )}

      {event.status !== "ENDED" && <BatchForm eventId={event.id} eventDate={event.date} />}
    </>
  );
}
