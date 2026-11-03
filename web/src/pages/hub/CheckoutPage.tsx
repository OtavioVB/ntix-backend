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
import { useCustomer } from "../../customer/session";
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

function PaymentForm({ event, items, onFinish }: { event: HubEvent; items: ReturnType<typeof itemsFromSelection>; onFinish: () => void }) {
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
  const total = orderTotal(items);

  function payWithPix() {
    const order = createOrder(event, items, { method: "PIX", status: "PENDING" }, Date.now());
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
    const order = createOrder(event, items, { method: "CARD", status: "PAID", paidAt: now, brand: cardBrand(digits), last4: digits.slice(-4), installments }, now);
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

export function CheckoutPage() {
  const { slug } = useParams();
  const { findBySlug } = useHub();
  const { customer, update } = useCustomer();
  const navigate = useNavigate();
  const event = findBySlug(slug);
  const cart = customer.cart;
  const finishing = useRef(false);
  const items = useMemo(() => (event && cart ? itemsFromSelection(event, cart.selection) : []), [event, cart]);

  function expire() {
    finishing.current = true;
    update({ cart: null });
    navigate(`/eventos/${slug}`, { replace: true, state: { cartExpired: true } });
  }

  if (!event) return <Navigate to="/" replace />;
  // Ao concluir a compra ou expirar a reserva o carrinho esvazia: sem isto, este redirecionamento venceria o destino certo.
  if (finishing.current) return null;
  if (!cart || cart.eventId !== event.id || items.length === 0) return <Navigate to={`/eventos/${event.slug}`} replace />;

  const signedIn = customer.loggedIn && customer.account;
  const day = eventDay(event.date);

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
                <PaymentForm event={event} items={items} onFinish={() => { finishing.current = true; }} />
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
            <div className="purchase-box__total"><span>Total · {ticketCount(items)} {ticketCount(items) === 1 ? "ingresso" : "ingressos"}</span><strong>{formatCents(orderTotal(items))}</strong></div>
            <Countdown expiresAt={cart.expiresAt} onExpire={expire} />
          </div>
        </aside>
      </div>
    </div>
  );
}
