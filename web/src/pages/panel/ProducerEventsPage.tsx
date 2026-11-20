import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Badge, Button, TextField } from "../../design-system";
import { createProducerEvent, getProducerEvents, updateProducerEvent, type EventStatus, type ProducerEvent } from "../../api/events";
import { useProducerSession } from "../../producer/session";
import { EventBatchesSection } from "./EventBatchesSection";
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

const DATE_FORMAT = new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium", timeStyle: "short" });

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
function EventFormSection({ editing, onSave, onCancel }: Readonly<{ editing?: ProducerEvent; onSave: (form: EventForm) => Promise<EventFormErrors>; onCancel: () => void }>) {
  const [form, setForm] = useState<EventForm>(editing ? formFromEvent(editing) : EMPTY_EVENT_FORM);
  const [errors, setErrors] = useState<EventFormErrors>({});
  const [sending, setSending] = useState(false);

  useEffect(() => {
    setForm(editing ? formFromEvent(editing) : EMPTY_EVENT_FORM);
    setErrors({});
  }, [editing]);

  const set = (field: EventFormField) => (value: string) => setForm((current) => ({ ...current, [field]: value }));

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();

    const found = validateEvent(form);
    setErrors(found);
    if (hasErrors(found)) return;

    setSending(true);
    const result = await onSave(form);
    setSending(false);
    setErrors(result);

    if (!hasErrors(result) && !editing) setForm(EMPTY_EVENT_FORM);
  }

  return (
    <form className="team-form" onSubmit={handleSubmit} noValidate aria-labelledby="event-form-title">
      <h2 className="tile__title" id="event-form-title">{editing ? `Editar ${editing.title}` : "Criar evento"}</h2>
      <TextField id="event-title" label="Título" maxLength={LIMITS.title} value={form.title} error={errors.title} onChange={(event) => set("title")(event.target.value)} />
      <div className="n-field">
        <label className="n-field__label" htmlFor="event-description">Descrição</label>
        <textarea id="event-description" className="n-input" rows={4} maxLength={LIMITS.description} value={form.description} onChange={(event) => set("description")(event.target.value)} />
        <p className="n-field__error">{errors.description}</p>
      </div>
      <SelectField id="event-category" label="Categoria" value={form.category} error={errors.category} onChange={set("category")}>
        <option value="">Escolha</option>
        {Object.entries(CATEGORY_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
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
      <SelectField id="event-age-rating" label="Classificação etária" value={form.ageRating} error={errors.ageRating} onChange={set("ageRating")}>
        <option value="">Escolha</option>
        {Object.entries(AGE_RATING_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </SelectField>
      <TextField id="event-image-url" label="Imagem (https, opcional)" type="url" maxLength={LIMITS.imageUrl} value={form.imageUrl} error={errors.imageUrl} onChange={(event) => set("imageUrl")(event.target.value)} />
      {errors.general && <p className="n-field__error" role="alert">{errors.general}</p>}
      <Button type="submit" block disabled={sending}>{editing ? "Salvar alterações" : "Criar em rascunho"}</Button>
      {editing && <Button type="button" variant="ghost" block onClick={onCancel}>Cancelar edição</Button>}
    </form>
  );
}

/**
 * Aba Eventos com a API real (REQ9, `?api=real`): lista, filtro por situação, criação em rascunho e edição.
 * Lotes e publicação do evento escolhido (REQ10) ficam em `EventBatchesSection`.
 */
export function ProducerEventsPage() {
  const { session } = useProducerSession();
  const [events, setEvents] = useState<ProducerEvent[]>([]);
  const [filter, setFilter] = useState<EventStatus | "">("");
  const [editing, setEditing] = useState<ProducerEvent>();
  const [managing, setManaging] = useState<ProducerEvent>();
  const [loadError, setLoadError] = useState<string>();
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    getProducerEvents(session.slug, filter || undefined, controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (result.ok) {
        setEvents(result.data);
        setLoadError(undefined);
      } else {
        setLoadError(result.notifications[0]?.message ?? "Não foi possível carregar os eventos.");
      }
    }).catch(() => {
      if (!controller.signal.aborted) setLoadError("Não foi possível carregar os eventos.");
    });

    return () => controller.abort();
  }, [session.slug, filter, reload]);

  async function save(form: EventForm): Promise<EventFormErrors> {
    const request = toEventRequest(form);
    const result = editing ? await updateProducerEvent(session.slug, editing.id, request) : await createProducerEvent(session.slug, request);

    if (!result.ok) return mapEventNotifications(result.notifications);

    setEditing(undefined);
    setReload((value) => value + 1);
    return {};
  }

  return (
    <>
      <title>Eventos e lotes · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Eventos e lotes</h1>
        <p className="n-lead">Monte seus eventos em rascunho, com calma. Eles só aparecem para o comprador depois de publicados.</p>
      </header>

      {loadError && <p className="n-field__error" role="alert">{loadError}</p>}

      <div className="team">
        <section className="tile" aria-labelledby="event-list-title">
          <h2 className="tile__title" id="event-list-title">{events.length === 1 ? "1 evento" : `${events.length} eventos`}</h2>
          <SelectField id="event-filter" label="Situação" value={filter} onChange={(value) => setFilter(value as EventStatus | "")}>
            <option value="">Todas</option>
            {Object.entries(STATUS_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </SelectField>
          {events.length === 0 ? (
            <p className="tile__note">Nenhum evento ainda. Crie o primeiro ao lado.</p>
          ) : (
            <ul className="team-list">
              {events.map((event) => (
                <li key={event.id}>
                  <div className="team-list__who">
                    <strong>{event.title}</strong>
                    <span>{DATE_FORMAT.format(new Date(event.startsAt))} · {event.venueName}, {event.city}-{event.state} · /eventos/{event.slug}</span>
                  </div>
                  <Badge live={event.status === "PUBLISHED"}>{STATUS_LABELS[event.status]}</Badge>
                  <button type="button" className="team-list__remove" aria-label={`Editar ${event.title}`} onClick={() => setEditing(event)}>Editar</button>
                  <button type="button" className="team-list__remove" aria-label={`Lotes de ${event.title}`} onClick={() => setManaging(event)}>Lotes</button>
                </li>
              ))}
            </ul>
          )}
          <p className="tile__note">O endereço do evento é gerado na criação e não muda, para não quebrar links já divulgados.</p>
        </section>

        <section className="tile">
          <EventFormSection editing={editing} onSave={save} onCancel={() => setEditing(undefined)} />
        </section>
      </div>

      {managing && (
        <EventBatchesSection
          key={managing.id}
          slug={session.slug}
          event={managing}
          onPublished={(published) => {
            setManaging(published);
            setReload((value) => value + 1);
          }}
        />
      )}
    </>
  );
}
