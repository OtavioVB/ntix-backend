import { useState, type FormEvent } from "react";
import { Badge, Button, Meter, TextField, Window } from "../../design-system";
import { createTicketBatch, updateTicketBatch, type ProducerEvent, type TicketBatch } from "../../api/events";
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

const PERIOD_FORMAT = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });

function period(batch: TicketBatch): string {
  return `${PERIOD_FORMAT.format(new Date(batch.salesStartsAt))} a ${PERIOD_FORMAT.format(new Date(batch.salesEndsAt))}`;
}

/** Lotes do evento com o detalhe de vendas de cada um (REQ14, RN32): a experiência fica na página do evento, não no card. */
function BatchTable({ batches, onToggle }: Readonly<{ batches: TicketBatch[]; onToggle: (batch: TicketBatch) => void }>) {
  return (
    <Window bar={<span>Lotes</span>}>
      <ul className="batch-table">
        {batches.map((batch) => (
          <li key={batch.id}>
            <div className="batch-table__name">
              <strong>{batch.name}</strong>
              <Badge live={batch.saleState === "ON_SALE"}>{SALE_STATE_LABELS[batch.saleState]}</Badge>
              <button type="button" className="team-list__remove" aria-label={`${batch.status === "ACTIVE" ? "Pausar" : "Reativar"} ${batch.name}`} onClick={() => onToggle(batch)}>
                {batch.status === "ACTIVE" ? "Pausar" : "Reativar"}
              </button>
            </div>
            <Meter value={batch.soldQuantity} max={Math.max(1, batch.quantity)} label={`${batch.soldQuantity} de ${batch.quantity} ingressos vendidos`} />
            <dl className="batch-table__data">
              <div><dt>Preço</dt><dd className="n-figure">{formatCents(batch.priceCents)}</dd></div>
              <div><dt>Vendidos</dt><dd className="n-figure">{batch.soldQuantity} de {batch.quantity}</dd></div>
              <div><dt>Reservados</dt><dd className="n-figure">{batch.reservedQuantity}</dd></div>
              <div><dt>Disponíveis</dt><dd className="n-figure">{batch.availableQuantity}</dd></div>
              <div><dt>Vendas brutas</dt><dd className="n-figure">{formatCents(batch.grossCents)}</dd></div>
              <div><dt>Pedidos</dt><dd className="n-figure">{batch.paidOrders}</dd></div>
              <div><dt>Período</dt><dd>{period(batch)}</dd></div>
            </dl>
          </li>
        ))}
      </ul>
    </Window>
  );
}

/**
 * Lotes do evento escolhido (REQ10), na página do evento: tabela com vendas por lote, pausar e reativar, e o formulário de lote.
 * A página é dona da lista e da publicação; aqui só se avisa que algo mudou.
 */
export function EventBatchesSection({ slug, event, batches, canAddBatch, onChanged }: Readonly<{ slug: string; event: ProducerEvent; batches: TicketBatch[]; canAddBatch: boolean; onChanged: () => void }>) {
  const [form, setForm] = useState<BatchForm>(EMPTY_BATCH_FORM);
  const [errors, setErrors] = useState<BatchFormErrors>({});
  const [sending, setSending] = useState(false);
  const [message, setMessage] = useState<string>();

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
    onChanged();
  }

  async function toggle(batch: TicketBatch) {
    const result = await updateTicketBatch(slug, event.id, batch.id, withStatus(batch, batch.status === "ACTIVE" ? "INACTIVE" : "ACTIVE"));
    setMessage(result.ok ? undefined : result.notifications[0]?.message);
    if (result.ok) onChanged();
  }

  return (
    <section className="event-section" aria-labelledby="batches-title">
      <h2 className="tile__title" id="batches-title">Lotes</h2>

      {batches.length === 0 ? (
        <p className="tile__note">Nenhum lote ainda. O evento só pode ser publicado com ao menos um lote ativo.</p>
      ) : (
        <BatchTable batches={batches} onToggle={toggle} />
      )}

      {message && <p className="n-field__error" role="alert">{message}</p>}

      {canAddBatch && (
        <form className="tile form-tile form-tile--grid" onSubmit={handleSubmit} noValidate aria-labelledby="batch-form-title">
          <h3 className="tile__title" id="batch-form-title">Adicionar lote</h3>
          <TextField id="batch-name" label="Nome do lote" maxLength={BATCH_LIMITS.name} placeholder="1º lote" value={form.name} error={errors.name} onChange={(input) => set("name")(input.target.value)} />
          <TextField id="batch-note" label="Observação (opcional)" maxLength={BATCH_LIMITS.note} value={form.note} error={errors.note} onChange={(input) => set("note")(input.target.value)} />
          <TextField id="batch-price" label="Preço (R$)" inputMode="decimal" placeholder="120,00" className="n-figure" value={form.price} error={errors.price} onChange={(input) => set("price")(input.target.value)} />
          <TextField id="batch-quantity" label="Quantidade" inputMode="numeric" className="n-figure" value={form.quantity} error={errors.quantity} onChange={(input) => set("quantity")(input.target.value)} />
          <TextField id="batch-sales-starts-at" label="Venda começa em" type="datetime-local" value={form.salesStartsAt} error={errors.salesStartsAt} onChange={(input) => set("salesStartsAt")(input.target.value)} />
          <TextField id="batch-sales-ends-at" label="Venda termina em" type="datetime-local" value={form.salesEndsAt} error={errors.salesEndsAt} onChange={(input) => set("salesEndsAt")(input.target.value)} />
          {errors.general && <p className="n-field__error" role="alert">{errors.general}</p>}
          <Button type="submit" variant="ghost" disabled={sending}>Adicionar lote</Button>
        </form>
      )}
    </section>
  );
}
