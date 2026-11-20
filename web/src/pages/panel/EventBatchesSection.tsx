import { useEffect, useState, type FormEvent } from "react";
import { Badge, Button, TextField } from "../../design-system";
import { createTicketBatch, getTicketBatches, publishProducerEvent, updateTicketBatch, type ProducerEvent, type TicketBatch } from "../../api/events";
import { formatCents } from "../../platform/money";
import {
  BATCH_LIMITS,
  EMPTY_BATCH_FORM,
  SALE_STATE_LABELS,
  hasBatchErrors,
  mapBatchNotifications,
  toBatchRequest,
  validateBatch,
  withStatus,
  type BatchForm,
  type BatchFormErrors,
  type BatchFormField,
} from "./batches";

const LOAD_ERROR = "Não foi possível carregar os lotes.";

/**
 * Lotes do evento escolhido e a publicação (REQ10, `?api=real`). Evento publicado continua aceitando lotes até começar.
 */
export function EventBatchesSection({ slug, event, onPublished }: Readonly<{ slug: string; event: ProducerEvent; onPublished: (event: ProducerEvent) => void }>) {
  const [batches, setBatches] = useState<TicketBatch[]>([]);
  const [form, setForm] = useState<BatchForm>(EMPTY_BATCH_FORM);
  const [errors, setErrors] = useState<BatchFormErrors>({});
  const [sending, setSending] = useState(false);
  const [message, setMessage] = useState<string>();
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    getTicketBatches(slug, event.id, controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (result.ok) setBatches(result.data);
      else setMessage(result.notifications[0]?.message ?? LOAD_ERROR);
    }).catch(() => {
      if (!controller.signal.aborted) setMessage(LOAD_ERROR);
    });

    return () => controller.abort();
  }, [slug, event.id, reload]);

  const set = (field: BatchFormField) => (value: string) => setForm((current) => ({ ...current, [field]: value }));

  async function handleSubmit(submit: FormEvent) {
    submit.preventDefault();

    const found = validateBatch(form, event.endsAt);
    setErrors(found);
    if (hasBatchErrors(found)) return;

    setSending(true);
    const result = await createTicketBatch(slug, event.id, toBatchRequest(form));
    setSending(false);

    if (!result.ok) {
      setErrors(mapBatchNotifications(result.notifications));
      return;
    }

    setForm(EMPTY_BATCH_FORM);
    setReload((value) => value + 1);
  }

  async function toggle(batch: TicketBatch) {
    const result = await updateTicketBatch(slug, event.id, batch.id, withStatus(batch, batch.status === "ACTIVE" ? "INACTIVE" : "ACTIVE"));
    setMessage(result.ok ? undefined : result.notifications[0]?.message);
    if (result.ok) setReload((value) => value + 1);
  }

  async function publish() {
    const result = await publishProducerEvent(slug, event.id);

    if (result.ok) {
      setMessage(undefined);
      onPublished(result.data);
    } else {
      setMessage(result.notifications[0]?.message ?? "Não foi possível publicar o evento.");
    }
  }

  return (
    <section className="tile" aria-labelledby="batches-title">
      <h2 className="tile__title" id="batches-title">Lotes de {event.title}</h2>

      {batches.length === 0 ? (
        <p className="tile__note">Nenhum lote ainda. O evento só pode ser publicado com ao menos um lote ativo.</p>
      ) : (
        <ul className="team-list">
          {batches.map((batch) => (
            <li key={batch.id}>
              <div className="team-list__who">
                <strong>{batch.name}</strong>
                <span className="n-figure">{formatCents(batch.priceCents)} · {batch.availableQuantity} de {batch.quantity} disponíveis · {batch.soldQuantity} vendidos</span>
              </div>
              <Badge live={batch.saleState === "ON_SALE"}>{SALE_STATE_LABELS[batch.saleState]}</Badge>
              <button type="button" className="team-list__remove" aria-label={`${batch.status === "ACTIVE" ? "Pausar" : "Reativar"} ${batch.name}`} onClick={() => toggle(batch)}>
                {batch.status === "ACTIVE" ? "Pausar" : "Reativar"}
              </button>
            </li>
          ))}
        </ul>
      )}

      {message && <p className="n-field__error" role="alert">{message}</p>}

      {event.status === "DRAFT" && <Button type="button" block onClick={publish}>Publicar evento</Button>}

      <form className="team-form" onSubmit={handleSubmit} noValidate aria-label="Novo lote">
        <TextField id="batch-name" label="Nome do lote" maxLength={BATCH_LIMITS.name} value={form.name} error={errors.name} onChange={(input) => set("name")(input.target.value)} />
        <TextField id="batch-note" label="Observação (opcional)" maxLength={BATCH_LIMITS.note} value={form.note} error={errors.note} onChange={(input) => set("note")(input.target.value)} />
        <TextField id="batch-price" label="Preço (R$)" inputMode="decimal" placeholder="120,00" value={form.price} error={errors.price} onChange={(input) => set("price")(input.target.value)} />
        <TextField id="batch-quantity" label="Quantidade" inputMode="numeric" value={form.quantity} error={errors.quantity} onChange={(input) => set("quantity")(input.target.value)} />
        <TextField id="batch-sales-starts-at" label="Venda começa em" type="datetime-local" value={form.salesStartsAt} error={errors.salesStartsAt} onChange={(input) => set("salesStartsAt")(input.target.value)} />
        <TextField id="batch-sales-ends-at" label="Venda termina em" type="datetime-local" value={form.salesEndsAt} error={errors.salesEndsAt} onChange={(input) => set("salesEndsAt")(input.target.value)} />
        {errors.general && <p className="n-field__error" role="alert">{errors.general}</p>}
        <Button type="submit" variant="ghost" block disabled={sending}>Adicionar lote</Button>
      </form>
    </section>
  );
}
