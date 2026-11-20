import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate, useParams } from "react-router-dom";
import { Button, Segmented, TextField } from "../../design-system";
import { createOrder, itemsFromSelection, orderTotal, ticketCount } from "../../customer/orders";
import {
  DECLINED_TEST_CARD,
  MAX_INSTALLMENTS,
  cardBrand,
  formatCardNumber,
  formatExpiry,
  isValidCardNumber,
  isValidCvv,
  isValidExpiry,
  onlyDigits,
} from "../../customer/payments";
import { placeServerOrder } from "../../customer/serverOrder";
import { useCustomer, type CartOrder, type CartState } from "../../customer/session";
import { isRealApi } from "../../api/mode";
import type { HubEvent } from "../../hub/catalog";
import { eventDay } from "../../hub/rules";
import { useHub, usePurchase } from "../../hub/useHub";
import { formatCents } from "../../platform/money";
import { AuthForm } from "./AuthForm";
import { EventImage } from "./parts";

const METHODS = [
  { value: "PIX", label: "Pix" },
  { value: "CARD", label: "Cartão de crédito" },
] as const;

function Countdown({ expiresAt, onExpire }: { expiresAt: number; onExpire: () => void }) {
  const [left, setLeft] = useState(() => expiresAt - Date.now());

  useEffect(() => {
    const timer = setInterval(() => {
      const value = expiresAt - Date.now();
      setLeft(value);
      if (value <= 0) onExpire();
    }, 1000);
    return () => clearInterval(timer);
  }, [expiresAt, onExpire]);

  const seconds = Math.max(0, Math.ceil(left / 1000));
  const text = `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
  return <p className="checkout-timer">Reservado por <b className="n-figure" role="timer">{text}</b></p>;
}

type CardErrors = { number?: string; holder?: string; expiry?: string; cvv?: string; declined?: string };

function PaymentForm({ event, items, orderCode, totalCents, onFinish }: { event: HubEvent; items: ReturnType<typeof itemsFromSelection>; orderCode?: string; totalCents: number; onFinish: () => void }) {
  const { addOrder } = usePurchase();
  const navigate = useNavigate();
  const [method, setMethod] = useState<"PIX" | "CARD">("PIX");
  const [number, setNumber] = useState("");
  const [holder, setHolder] = useState("");
  const [expiry, setExpiry] = useState("");
  const [cvv, setCvv] = useState("");
  const [installments, setInstallments] = useState(1);
  const [errors, setErrors] = useState<CardErrors>({});
  const [busy, setBusy] = useState(false);
  const total = totalCents;

  function payWithPix() {
    const order = createOrder(event, items, { method: "PIX", status: "PENDING" }, Date.now(), orderCode);
    onFinish();
    addOrder(order);
    navigate(`/conta/pedidos/${order.code}`);
  }

  async function payWithCard(submit: FormEvent) {
    submit.preventDefault();
    if (busy) return;
    const now = Date.now();
    const found: CardErrors = {};
    if (!isValidCardNumber(number)) found.number = "Confira o número do cartão.";
    if (holder.trim().length < 2) found.holder = "Informe o nome impresso no cartão.";
    if (!isValidExpiry(expiry, now)) found.expiry = "Confira a validade.";
    if (!isValidCvv(cvv)) found.cvv = "Confira o código.";
    setErrors(found);
    if (Object.keys(found).length) return;

    setBusy(true);
    // Integração pendente: é aqui que entra o provedor de pagamento. Sem ele, a resposta é simulada.
    await new Promise((resolve) => setTimeout(resolve, 700));
    const digits = onlyDigits(number);
    if (digits === DECLINED_TEST_CARD) {
      setErrors({ declined: "O pagamento foi recusado pela operadora. Confira os dados ou tente outro cartão." });
      setBusy(false);
      return;
    }
    const order = createOrder(event, items, { method: "CARD", status: "PAID", paidAt: now, brand: cardBrand(digits), last4: digits.slice(-4), installments }, now, orderCode);
    onFinish();
    addOrder(order);
    navigate(`/conta/pedidos/${order.code}/confirmado`, { replace: true });
  }

  return (
    <div className="payment-form">
      <Segmented name="payment-method" legend="Como você quer pagar?" options={METHODS} value={method} onChange={setMethod} />
      {method === "PIX" ? (
        <div className="payment-pix">
          <p>Geramos um código para você pagar no aplicativo do seu banco. Seus ingressos saem assim que o pagamento for confirmado.</p>
          <Button block size="lg" arrow onClick={payWithPix}>Gerar Pix de {formatCents(total)}</Button>
        </div>
      ) : (
        <form className="payment-card" onSubmit={payWithCard} noValidate>
          <TextField id="card-number" label="Número do cartão" inputMode="numeric" autoComplete="cc-number" className="n-figure" value={number} error={errors.number} onChange={(input) => setNumber(formatCardNumber(input.target.value))} />
          <TextField id="card-holder" label="Nome impresso no cartão" autoComplete="cc-name" value={holder} error={errors.holder} onChange={(input) => setHolder(input.target.value)} />
          <div className="payment-card__row">
            <TextField id="card-expiry" label="Validade" inputMode="numeric" autoComplete="cc-exp" placeholder="MM/AA" maxLength={5} className="n-figure" value={expiry} error={errors.expiry} onChange={(input) => setExpiry(formatExpiry(input.target.value))} />
            <TextField id="card-cvv" label="Código de segurança" inputMode="numeric" autoComplete="cc-csc" maxLength={4} className="n-figure" value={cvv} error={errors.cvv} onChange={(input) => setCvv(onlyDigits(input.target.value))} />
          </div>
          <div className="n-field">
            <label className="n-field__label" htmlFor="card-installments">Parcelas</label>
            <select id="card-installments" className="n-input" value={installments} onChange={(input) => setInstallments(Number(input.target.value))}>
              {Array.from({ length: MAX_INSTALLMENTS }, (_, index) => index + 1).map((value) => (
                <option key={value} value={value}>{value}x de {formatCents(Math.ceil(total / value))} sem juros</option>
              ))}
            </select>
          </div>
          <p className="auth-form__error" role="alert">{errors.declined}</p>
          <Button type="submit" block size="lg" arrow aria-busy={busy}>{busy ? "Processando..." : `Pagar ${formatCents(total)}`}</Button>
        </form>
      )}
      <p className="signup__note">
        Demonstração: nenhuma cobrança é feita e os dados do cartão não são guardados. Para testar, use 4111 1111 1111 1111. Com 4000 0000 0000 0002, o pagamento é recusado.
      </p>
    </div>
  );
}

type ServerOrderState =
  | { status: "idle" }
  | { status: "creating" }
  | { status: "ready"; order: CartOrder }
  | { status: "failed"; message: string };

/**
 * Pedido na API real (REQ6): com a pessoa conectada, pede os ingressos uma vez por carrinho e guarda o pedido no carrinho,
 * para recarregar o checkout reaproveitar a reserva. Na demonstração não faz nada.
 */
function useServerOrder(
  event: HubEvent | undefined,
  items: ReturnType<typeof itemsFromSelection>,
  cart: CartState | null,
  enabled: boolean,
  onCreated: (order: CartOrder) => void,
  onUnavailable: (message: string) => void,
): ServerOrderState {
  const [failure, setFailure] = useState<string | null>(null);
  const requested = useRef(false);
  const reusable = cart?.order && cart.order.expiresAt > Date.now() ? cart.order : null;

  useEffect(() => {
    if (!enabled || !event || reusable || requested.current) return;
    requested.current = true;

    void placeServerOrder(event.slug, items).then((outcome) => {
      if (outcome.kind === "created") onCreated(outcome);
      else if (outcome.kind === "unavailable") onUnavailable(outcome.message);
      else setFailure(outcome.message);
    });
  }, [enabled, event, items, reusable, onCreated, onUnavailable]);

  if (!enabled) return { status: "idle" };
  if (reusable) return { status: "ready", order: reusable };
  return failure === null ? { status: "creating" } : { status: "failed", message: failure };
}

export function CheckoutPage() {
  const { slug } = useParams();
  const { findBySlug, loading } = useHub();
  const { customer, update } = useCustomer();
  const navigate = useNavigate();
  const event = findBySlug(slug);
  const cart = customer.cart;
  const finishing = useRef(false);
  const items = useMemo(() => (event && cart ? itemsFromSelection(event, cart.selection) : []), [event, cart]);
  const signedIn = customer.loggedIn && customer.account;
  const validCart = Boolean(event && cart && cart.eventId === event.id && items.length > 0);

  function leave(state: { cartExpired: true } | { orderRefused: string }) {
    finishing.current = true;
    update({ cart: null });
    navigate(`/eventos/${slug}`, { replace: true, state });
  }

  const serverOrder = useServerOrder(
    event,
    items,
    cart,
    isRealApi() && Boolean(signedIn) && validCart,
    (order) => update({ cart: cart ? { ...cart, order } : null }),
    (message) => leave({ orderRefused: message }),
  );

  // Vitrine real ainda carregando (recarregar o checkout): não manda para a home antes de a API responder.
  if (!event && loading) {
    return (
      <div className="checkout">
        <title>Finalizar compra · Ntix</title>
        <div className="n-container"><p className="n-lead" role="status">Carregando o evento…</p></div>
      </div>
    );
  }

  if (!event) return <Navigate to="/" replace />;
  // Ao concluir a compra ou expirar a reserva o carrinho esvazia: sem isto, este redirecionamento venceria o destino certo.
  if (finishing.current) return null;
  if (!cart || !validCart) return <Navigate to={`/eventos/${event.slug}`} replace />;

  const day = eventDay(event.date);
  // Na API real, o prazo e o total são os do pedido no servidor (RN7, RN9).
  const expiresAt = serverOrder.status === "ready" ? serverOrder.order.expiresAt : cart.expiresAt;
  const totalCents = serverOrder.status === "ready" ? serverOrder.order.totalCents : orderTotal(items);

  return (
    <div className="checkout">
      <title>Finalizar compra · Ntix</title>
      <div className="n-container checkout__grid">
        <div className="checkout__main">
          <Link className="back-link" to={`/eventos/${event.slug}`}>Voltar ao evento</Link>
          <h1 className="n-display n-display--md">Falta pouco.</h1>
          <ol className="checkout-steps" aria-label="Etapas da compra">
            <li className="is-done">Ingressos</li>
            <li className={signedIn ? "is-done" : "is-current"} aria-current={signedIn ? undefined : "step"}>Sua conta</li>
            <li className={signedIn ? "is-current" : undefined} aria-current={signedIn ? "step" : undefined}>Pagamento</li>
          </ol>
          <section className="checkout-panel">
            {signedIn ? (
              <>
                <p className="checkout-who">Comprando como <strong>{customer.account!.name}</strong></p>
                {serverOrder.status === "creating" && <p className="signup__note" role="status">Reservando seus ingressos…</p>}
                {serverOrder.status === "failed" && (
                  <p className="auth-form__error" role="alert">{serverOrder.message} <Link to={`/eventos/${event.slug}`}>Voltar ao evento</Link></p>
                )}
                {(serverOrder.status === "idle" || serverOrder.status === "ready") && (
                  <PaymentForm
                    event={event}
                    items={items}
                    orderCode={serverOrder.status === "ready" ? serverOrder.order.code : undefined}
                    totalCents={totalCents}
                    onFinish={() => { finishing.current = true; }}
                  />
                )}
              </>
            ) : (
              <AuthForm />
            )}
          </section>
        </div>

        <aside className="checkout-summary" aria-label="Resumo do pedido">
          <div className="checkout-summary__image"><EventImage event={event} eager /></div>
          <div className="checkout-summary__body">
            <p className="n-label n-label--accent">{day.day} {day.month}</p>
            <h2 className="checkout-summary__title">{event.title}</h2>
            <p className="checkout-summary__place">{event.venue} · {event.city}</p>
            <ul className="purchase-box__items">
              {items.map((item) => (
                <li key={item.batchId}><span>{item.quantity} × {item.name}</span><b className="n-figure">{formatCents(item.quantity * item.unitPriceCents)}</b></li>
              ))}
            </ul>
            <div className="purchase-box__total"><span>Total · {ticketCount(items)} {ticketCount(items) === 1 ? "ingresso" : "ingressos"}</span><strong>{formatCents(totalCents)}</strong></div>
            {(serverOrder.status === "idle" || serverOrder.status === "ready") && <Countdown key={expiresAt} expiresAt={expiresAt} onExpire={() => leave({ cartExpired: true })} />}
          </div>
        </aside>
      </div>
    </div>
  );
}
