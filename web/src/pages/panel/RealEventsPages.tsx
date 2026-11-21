import { Suspense, lazy, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Link, Navigate, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { Badge, Button, ButtonLink, Meter, Segmented, TextField } from "../../design-system";
import { MarkdownView } from "../../design-system/components/MarkdownView";
import {
  createProducerEvent,
  getEventDailySales,
  getProducerEvent,
  getProducerEvents,
  getTicketBatches,
  publishProducerEvent,
  updateProducerEvent,
  type DailySales,
  type EventStatus,
  type ProducerEvent,
  type TicketBatch,
} from "../../api/events";
import type { ApiDataResult } from "../../api/client";
import { formatCents } from "../../platform/money";
import { hubEventHref, panelPath } from "../../producer/paths";
import { useProducerSession } from "../../producer/session";
import { EventBatchesSection } from "./EventBatchesSection";
import { SalesChart } from "./SalesChart";
import {
  AGE_RATING_LABELS,
  CATEGORY_LABELS,
  EMPTY_EVENT_FORM,
  LIMITS,
  STATES,
  STATUS_LABELS,
  formFromEvent,
  hasErrors,
  mapEventNotifications,
  toEventRequest,
  validateEvent,
  type EventForm,
  type EventFormErrors,
  type EventFormField,
} from "./events";

// O editor só carrega na criação e na edição (RFC 0010): a lista e a página do evento não pagam o peso dele.
const RichTextEditor = lazy(() => import("../../design-system/components/RichTextEditor"));

const DATE_FORMAT = new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium", timeStyle: "short" });
const LOAD_ERROR = "Não foi possível carregar os eventos.";

/** Filtros da lista (REQ14, RN25), com o nome que vai na URL (`?situacao=`). */
const FILTERS = [
  { value: "todos", label: "Todos", status: undefined },
  { value: "rascunho", label: "Rascunho", status: "DRAFT" },
  { value: "publicados", label: "Publicados", status: "PUBLISHED" },
  { value: "encerrados", label: "Encerrados", status: "ENDED" },
] as const satisfies readonly { value: string; label: string; status: EventStatus | undefined }[];

type FilterValue = (typeof FILTERS)[number]["value"];

/** RN24: publicados, rascunhos e encerrados; dentro de cada grupo, por início. */
const STATUS_ORDER: EventStatus[] = ["PUBLISHED", "DRAFT", "ENDED"];

function sortEvents(events: ProducerEvent[], filtered: boolean): ProducerEvent[] {
  return [...events].sort((a, b) =>
    (filtered ? 0 : STATUS_ORDER.indexOf(a.status) - STATUS_ORDER.indexOf(b.status)) || a.startsAt.localeCompare(b.startsAt));
}

function placeOf(event: ProducerEvent): string {
  return `${DATE_FORMAT.format(new Date(event.startsAt))} · ${event.venueName}, ${event.city}-${event.state}`;
}

/** Evento ainda editável (REQ9, RN11): não começou. */
function canEdit(event: ProducerEvent, now = Date.now()): boolean {
  return event.status !== "ENDED" && new Date(event.startsAt).getTime() > now;
}

type Load<T> = { data?: T; error?: string; notFound?: boolean };

/** Carrega na montagem e quando `key` muda; cancela ao sair. 404 vira `notFound`, para a página redirecionar (RN34). */
function useLoad<T>(load: (signal: AbortSignal) => Promise<ApiDataResult<T>>, key: string): Load<T> {
  const [state, setState] = useState<Load<T>>({});

  useEffect(() => {
    const controller = new AbortController();

    load(controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (result.ok) setState({ data: result.data });
      else setState({ error: result.notifications[0]?.message ?? LOAD_ERROR, notFound: result.notifications.some((notification) => notification.code.endsWith("NOT_FOUND")) });
    }).catch(() => {
      if (!controller.signal.aborted) setState({ error: LOAD_ERROR });
    });

    return () => controller.abort();
    // `load` muda a cada renderização; quem decide recarregar é a `key`.
  }, [key]);

  return state;
}

function EventCard({ event }: Readonly<{ event: ProducerEvent }>) {
  const { capacity, soldQuantity, grossCents } = event.salesSummary;

  return (
    <Link className="event-card" to={panelPath(`eventos/${event.id}`)}>
      <div className="event-card__head">
        <h2 className="event-card__name">{event.title}</h2>
        <Badge live={event.status === "PUBLISHED"}>{STATUS_LABELS[event.status]}</Badge>
      </div>
      <p className="event-card__meta">{placeOf(event)}</p>
      {capacity > 0 ? (
        <>
          <Meter value={soldQuantity} max={capacity} label={`${soldQuantity} de ${capacity} ingressos vendidos`} />
          <p className="event-card__numbers n-figure"><span>{soldQuantity} de {capacity}</span><span>{formatCents(grossCents)}</span></p>
        </>
      ) : (
        <p className="event-card__meta">Sem lotes ainda</p>
      )}
    </Link>
  );
}

/** Aba Eventos com a API real (REQ14): cards com o resumo de vendas e filtro por situação na URL. */
export function ProducerEventsListPage() {
  const { session } = useProducerSession();
  const [params, setParams] = useSearchParams();
  const filter = FILTERS.find((item) => item.value === params.get("situacao")) ?? FILTERS[0];
  const events = useLoad((signal) => getProducerEvents(session.slug, filter.status, signal), `${session.slug}:${filter.value}`);

  function choose(value: FilterValue) {
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (value === "todos") next.delete("situacao");
      else next.set("situacao", value);
      return next;
    }, { replace: true });
  }

  const list = events.data ? sortEvents(events.data, filter.status !== undefined) : undefined;

  return (
    <>
      <title>Eventos e lotes · Ntix</title>
      <header className="page-head page-head--action">
        <div>
          <h1 className="n-display n-display--md">Eventos e lotes</h1>
          <p className="n-lead">Acompanhe a venda de cada evento. Eles só aparecem para o comprador depois de publicados.</p>
        </div>
        <ButtonLink to={panelPath("eventos/novo")}>Criar evento</ButtonLink>
      </header>

      <div className="event-filters">
        <Segmented name="event-status" legend="Situação" options={FILTERS} value={filter.value} onChange={choose} />
      </div>

      {events.error && <p className="n-field__error" role="alert">{events.error}</p>}
      {!events.data && !events.error && <p className="tile__note">Carregando os eventos…</p>}

      {list?.length === 0 && (
        <section className="tile empty-state">
          {filter.status ? (
            <h2 className="n-title">Nenhum evento nesta situação</h2>
          ) : (
            <>
              <h2 className="n-title">Nenhum evento ainda</h2>
              <p>Crie o primeiro para começar a vender no hub da Ntix.</p>
              <ButtonLink to={panelPath("eventos/novo")}>Criar evento</ButtonLink>
            </>
          )}
        </section>
      )}

      {list && list.length > 0 && (
        <ul className="event-cards">
          {list.map((event) => <li key={event.id}><EventCard event={event} /></li>)}
        </ul>
      )}
    </>
  );
}

function SelectField({ id, label, value, error, onChange, children }: Readonly<{ id: string; label: string; value: string; error?: string; onChange: (value: string) => void; children: ReactNode }>) {
  return (
    <div className="n-field">
      <label className="n-field__label" htmlFor={id}>{label}</label>
      <select id={id} className="n-input" value={value} aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined} onChange={(event) => onChange(event.target.value)}>
        {children}
      </select>
      <p className="n-field__error" id={`${id}-error`}>{error}</p>
    </div>
  );
}

/** Criação e edição: o mesmo formulário, porque a edição substitui todos os campos (REQ9). */
function EventFormFields({ editing, onSave, onCancel }: Readonly<{ editing?: ProducerEvent; onSave: (form: EventForm) => Promise<EventFormErrors>; onCancel: () => void }>) {
  const [form, setForm] = useState<EventForm>(editing ? formFromEvent(editing) : EMPTY_EVENT_FORM);
  const [errors, setErrors] = useState<EventFormErrors>({});
  const [sending, setSending] = useState(false);

  const set = (field: EventFormField) => (value: string) => setForm((current) => ({ ...current, [field]: value }));
  const descriptionLength = form.description.trim().length;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const found = validateEvent(form);
    setErrors(found);
    if (hasErrors(found)) return;

    setSending(true);
    const result = await onSave(form);
    setSending(false);
    setErrors(result);
  }

  return (
    <form className="tile form-tile event-form" onSubmit={handleSubmit} noValidate aria-label={editing ? `Editar ${editing.title}` : "Criar evento"}>
      <TextField id="event-title" label="Título" maxLength={LIMITS.title} value={form.title} error={errors.title} onChange={(event) => set("title")(event.target.value)} />
      <Suspense fallback={<p className="tile__note">Carregando o editor…</p>}>
        <RichTextEditor
          id="event-description"
          label="Descrição"
          value={form.description}
          onChange={set("description")}
          hint={`${descriptionLength} de ${LIMITS.description} caracteres. Use títulos, listas e negrito para separar atrações, horários e regras.`}
          error={errors.description}
        />
      </Suspense>
      <div className="form-tile--grid event-form__grid">
        <SelectField id="event-category" label="Categoria" value={form.category} error={errors.category} onChange={set("category")}>
          <option value="">Escolha</option>
          {Object.entries(CATEGORY_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </SelectField>
        <SelectField id="event-age-rating" label="Classificação etária" value={form.ageRating} error={errors.ageRating} onChange={set("ageRating")}>
          <option value="">Escolha</option>
          {Object.entries(AGE_RATING_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </SelectField>
        <TextField id="event-starts-at" label="Começa em" type="datetime-local" value={form.startsAt} error={errors.startsAt} onChange={(event) => set("startsAt")(event.target.value)} />
        <TextField id="event-ends-at" label="Termina em" type="datetime-local" value={form.endsAt} error={errors.endsAt} onChange={(event) => set("endsAt")(event.target.value)} />
        <TextField id="event-doors-open-at" label="Abertura dos portões (opcional)" type="datetime-local" value={form.doorsOpenAt} error={errors.doorsOpenAt} onChange={(event) => set("doorsOpenAt")(event.target.value)} />
        <TextField id="event-venue-name" label="Local" maxLength={LIMITS.venueName} value={form.venueName} error={errors.venueName} onChange={(event) => set("venueName")(event.target.value)} />
        <TextField id="event-address" label="Endereço" maxLength={LIMITS.address} value={form.address} error={errors.address} onChange={(event) => set("address")(event.target.value)} />
        <TextField id="event-city" label="Cidade" maxLength={LIMITS.city} value={form.city} error={errors.city} onChange={(event) => set("city")(event.target.value)} />
        <SelectField id="event-state" label="UF" value={form.state} error={errors.state} onChange={set("state")}>
          <option value="">Escolha</option>
          {STATES.map((state) => <option key={state} value={state}>{state}</option>)}
        </SelectField>
        <TextField id="event-image-url" label="Imagem (https, opcional)" type="url" maxLength={LIMITS.imageUrl} value={form.imageUrl} error={errors.imageUrl} onChange={(event) => set("imageUrl")(event.target.value)} />
      </div>
      {errors.general && <p className="n-field__error" role="alert">{errors.general}</p>}
      <div className="event-form__actions">
        <Button type="submit" disabled={sending || descriptionLength > LIMITS.description}>{editing ? "Salvar alterações" : "Criar em rascunho"}</Button>
        <Button type="button" variant="ghost" onClick={onCancel}>Cancelar</Button>
      </div>
      {!editing && <p className="tile__note">O endereço do evento é gerado na criação e não muda, para não quebrar links já divulgados.</p>}
    </form>
  );
}

/** Criação em página própria (RN28): ao salvar, leva à página do evento criado. */
export function ProducerEventCreatePage() {
  const { session } = useProducerSession();
  const navigate = useNavigate();

  async function save(form: EventForm): Promise<EventFormErrors> {
    const result = await createProducerEvent(session.slug, toEventRequest(form));
    if (!result.ok) return mapEventNotifications(result.notifications);

    void navigate(panelPath(`eventos/${result.data.id}`));
    return {};
  }

  return (
    <>
      <title>Novo evento · Ntix</title>
      <header className="page-head">
        <Link className="back-link" to={panelPath("eventos")}>Eventos e lotes</Link>
        <h1 className="n-display n-display--md">Novo evento</h1>
        <p className="n-lead">Ele nasce em rascunho. Depois você cria os lotes e publica.</p>
      </header>
      <EventFormFields onSave={save} onCancel={() => { void navigate(panelPath("eventos")); }} />
    </>
  );
}

/** Edição em página própria (RN29), com o nome do evento no título; depois do início, volta à página do evento. */
export function ProducerEventEditPage() {
  const { eventId = "" } = useParams();
  const { session } = useProducerSession();
  const navigate = useNavigate();
  const event = useLoad((signal) => getProducerEvent(session.slug, eventId, signal), `${session.slug}:${eventId}`);
  const detailPath = panelPath(`eventos/${eventId}`);

  if (event.notFound) return <Navigate to={panelPath("eventos")} replace />;
  if (event.error) return <p className="n-field__error" role="alert">{event.error}</p>;
  if (!event.data) return <p className="tile__note">Carregando o evento…</p>;
  if (!canEdit(event.data)) return <Navigate to={detailPath} replace />;

  async function save(form: EventForm): Promise<EventFormErrors> {
    const result = await updateProducerEvent(session.slug, eventId, toEventRequest(form));
    if (!result.ok) return mapEventNotifications(result.notifications);

    void navigate(detailPath);
    return {};
  }

  return (
    <>
      <title>{`Editar ${event.data.title} · Ntix`}</title>
      <header className="page-head">
        <Link className="back-link" to={detailPath}>{event.data.title}</Link>
        <h1 className="n-display n-display--md">Editar {event.data.title}</h1>
      </header>
      <EventFormFields key={event.data.id} editing={event.data} onSave={save} onCancel={() => { void navigate(detailPath); }} />
    </>
  );
}

const PERIODS = [
  { value: "7", label: "7 dias" },
  { value: "30", label: "30 dias" },
  { value: "90", label: "90 dias" },
] as const;

type PeriodValue = (typeof PERIODS)[number]["value"];

const BRASILIA_DATE = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit" });

/** Período do gráfico em datas de Brasília, terminando hoje (o mesmo fuso da API). */
function periodDates(days: number, now = new Date()): { from: string; to: string } {
  const to = BRASILIA_DATE.format(now);
  const start = new Date(`${to}T12:00:00Z`);
  start.setUTCDate(start.getUTCDate() - (days - 1));
  return { from: start.toISOString().slice(0, 10), to };
}

function DailySalesSection({ slug, eventId, version }: Readonly<{ slug: string; eventId: string; version: number }>) {
  const [period, setPeriod] = useState<PeriodValue>("30");
  const report = useLoad((signal) => getEventDailySales(slug, eventId, periodDates(Number(period)), signal), `${eventId}:${period}:${version}`);
  const days: DailySales[] | undefined = report.data?.days;

  return (
    <section className="tile event-section" aria-labelledby="daily-sales-title">
      <div className="event-section__head">
        <h2 className="tile__title" id="daily-sales-title">Vendas por dia</h2>
        <Segmented name="sales-period" legend="Período" options={PERIODS} value={period} onChange={setPeriod} />
      </div>
      {report.error && <p className="n-field__error" role="alert">{report.error}</p>}
      {days ? <SalesChart days={days} /> : !report.error && <p className="tile__note">Carregando as vendas…</p>}
    </section>
  );
}

/** Página do evento (REQ14, RN30 a RN34): resumo, vendas por dia, lotes com o detalhe de vendas e publicação. */
export function ProducerEventDetailPage() {
  const { eventId = "" } = useParams();
  const { session } = useProducerSession();
  const [version, setVersion] = useState(0);
  const [message, setMessage] = useState<string>();
  const [publishing, setPublishing] = useState(false);
  const event = useLoad((signal) => getProducerEvent(session.slug, eventId, signal), `${session.slug}:${eventId}:${version}`);
  const batches = useLoad<TicketBatch[]>((signal) => getTicketBatches(session.slug, eventId, signal), `${session.slug}:${eventId}:${version}`);

  if (event.notFound) return <Navigate to={panelPath("eventos")} replace />;
  if (event.error) return <p className="n-field__error" role="alert">{event.error}</p>;
  if (!event.data) return <p className="tile__note">Carregando o evento…</p>;

  const current = event.data;
  const { capacity, soldQuantity, grossCents, paidOrders } = current.salesSummary;
  const now = Date.now();
  const hasSellableBatch = (batches.data ?? []).some((batch) => batch.status === "ACTIVE" && new Date(batch.salesEndsAt).getTime() > now);
  const editable = canEdit(current, now);

  async function publish() {
    setPublishing(true);
    const result = await publishProducerEvent(session.slug, eventId);
    setPublishing(false);
    setMessage(result.ok ? undefined : result.notifications[0]?.message ?? "Não foi possível publicar o evento.");
    if (result.ok) setVersion((value) => value + 1);
  }

  return (
    <>
      <title>{`${current.title} · Ntix`}</title>
      <header className="page-head page-head--action">
        <div>
          <Link className="back-link" to={panelPath("eventos")}>Eventos e lotes</Link>
          <h1 className="n-display n-display--md">{current.title}</h1>
          <p className="n-lead">{placeOf(current)}</p>
        </div>
        {current.status === "DRAFT" && (
          <Button disabled={!hasSellableBatch || publishing} onClick={publish}>Publicar evento</Button>
        )}
      </header>

      <div className="detail-bar">
        <Badge live={current.status === "PUBLISHED"}>{STATUS_LABELS[current.status]}</Badge>
        {current.status === "DRAFT" && !hasSellableBatch && <span className="inline-note">Adicione um lote para poder publicar.</span>}
        {editable && <ButtonLink variant="ghost" to={panelPath(`eventos/${current.id}/editar`)}>Editar evento</ButtonLink>}
        {current.status === "PUBLISHED" && (
          <a className="n-btn n-btn--ghost" href={hubEventHref(current.slug)} target="_blank" rel="noopener noreferrer">Ver na vitrine</a>
        )}
      </div>

      {message && <p className="n-field__error" role="alert">{message}</p>}

      <dl className="stat-row">
        <div className="tile"><dt>Ingressos vendidos</dt><dd className="n-figure">{soldQuantity} <small>de {capacity}</small></dd></div>
        <div className="tile"><dt>Vendas brutas</dt><dd className="n-figure">{formatCents(grossCents)} <small>antes das taxas</small></dd></div>
        <div className="tile"><dt>Pedidos</dt><dd className="n-figure">{paidOrders}</dd></div>
      </dl>

      <DailySalesSection slug={session.slug} eventId={current.id} version={version} />

      {batches.error && <p className="n-field__error" role="alert">{batches.error}</p>}
      <EventBatchesSection slug={session.slug} event={current} batches={batches.data ?? []} canAddBatch={editable} onChanged={() => setVersion((value) => value + 1)} />

      {current.description && (
        <section className="tile event-section" aria-labelledby="description-title">
          <h2 className="tile__title" id="description-title">Descrição</h2>
          <MarkdownView markdown={current.description} />
        </section>
      )}
    </>
  );
}
