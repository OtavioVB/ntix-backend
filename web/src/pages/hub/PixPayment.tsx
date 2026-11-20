import { QRCodeSVG } from "qrcode.react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../design-system";
import { getOrder, getOrderPaymentMethods, postOrderPayment, type ApiPayment } from "../../api/orders";
import { formatCents } from "../../platform/money";

/** O checkout pergunta ao servidor se o pedido foi pago a cada 3 s (REQ7). */
export const PAYMENT_POLL_MS = 3000;

const COPIED_MESSAGE_MS = 2500;
const DATA_IMAGE_PREFIX = "data:image/";

type Phase =
  | { kind: "loading-methods" }
  | { kind: "no-method" }
  | { kind: "ready" }
  | { kind: "creating" }
  | { kind: "waiting"; payment: ApiPayment }
  | { kind: "paid" };

type Props = {
  orderCode: string;
  totalCents: number;
  /** A reserva do pedido venceu: o checkout volta ao evento. */
  onOrderExpired: () => void;
  /** Só para os testes: intervalo da consulta do pedido. */
  pollMs?: number;
};

/** Contador do Pix, calculado no navegador a partir do `expiresAt` do servidor. */
function PixCountdown({ expiresAt }: { expiresAt: number }) {
  const [left, setLeft] = useState(() => expiresAt - Date.now());

  useEffect(() => {
    const timer = setInterval(() => setLeft(expiresAt - Date.now()), 1000);
    return () => clearInterval(timer);
  }, [expiresAt]);

  const seconds = Math.max(0, Math.ceil(left / 1000));
  const text = `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
  return <p className="checkout-timer">Pague em <b className="n-figure" role="timer">{text}</b></p>;
}

/** Imagem só quando o servidor mandou uma imagem em data URL; qualquer outra coisa vira QR gerado do copia e cola. */
function PixQrCode({ payment }: { payment: ApiPayment }) {
  const pix = payment.pix!;

  return pix.qrCodeImage.startsWith(DATA_IMAGE_PREFIX)
    ? <img className="pix-qr" src={pix.qrCodeImage} alt="QR Code do Pix" width={192} height={192} />
    : <QRCodeSVG value={pix.copyPasteCode} size={192} marginSize={2} title="QR Code do Pix" />;
}

/**
 * Pagamento do pedido com Pix na API real (REQ7): confere os métodos do pedido, gera o Pix, mostra QR Code e copia e cola com o
 * contador do servidor e consulta o pedido até ele ser pago (REQ8) ou a reserva vencer.
 */
export function PixPayment({ orderCode, totalCents, onOrderExpired, pollMs = PAYMENT_POLL_MS }: Props) {
  const [phase, setPhase] = useState<Phase>({ kind: "loading-methods" });
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  // O checkout passa uma função nova a cada renderização: a referência evita reiniciar a consulta do pedido por isso.
  const orderExpired = useRef(onOrderExpired);

  orderExpired.current = onOrderExpired;

  useEffect(() => {
    const controller = new AbortController();

    void getOrderPaymentMethods(orderCode, controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (!result.ok) {
        setError(result.notifications[0]?.message ?? null);
        setPhase({ kind: "ready" });
        return;
      }
      setPhase(result.data.methods.some((item) => item.method === "PIX") ? { kind: "ready" } : { kind: "no-method" });
    });

    return () => controller.abort();
  }, [orderCode]);

  // Enquanto espera, o servidor diz se o pedido foi pago, se o Pix venceu ou se a reserva acabou.
  const waitingFor = phase.kind === "waiting" ? phase.payment.id : null;

  useEffect(() => {
    if (waitingFor === null) return;
    const controller = new AbortController();

    const timer = setInterval(() => {
      void getOrder(orderCode, controller.signal).then((result) => {
        if (controller.signal.aborted || !result.ok) return;
        if (result.data.status === "PAID") setPhase({ kind: "paid" });
        else if (result.data.status === "EXPIRED") orderExpired.current();
        else if (result.data.payment?.status === "EXPIRED") {
          setError("O Pix venceu. Gere um novo para pagar.");
          setPhase({ kind: "ready" });
        }
      });
    }, pollMs);

    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [waitingFor, orderCode, pollMs]);

  useEffect(() => () => {
    if (copiedTimer.current) clearTimeout(copiedTimer.current);
  }, []);

  const generate = useCallback(async () => {
    setError(null);
    setPhase({ kind: "creating" });

    const result = await postOrderPayment(orderCode, "PIX");

    if (result.ok) {
      setPhase(result.data.pix ? { kind: "waiting", payment: result.data } : { kind: "ready" });
      return;
    }

    const code = result.notifications[0]?.code;

    if (code === "ORDER_EXPIRED") return orderExpired.current();
    if (code === "ORDER_ALREADY_PAID") return setPhase({ kind: "paid" });

    setError(result.notifications.map((notification) => notification.message).join(" "));
    setPhase({ kind: "ready" });
  }, [orderCode]);

  async function copy(code: string) {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
    } catch {
      setCopied(false);
      setError("Não foi possível copiar. Selecione o código e copie manualmente.");
      return;
    }
    if (copiedTimer.current) clearTimeout(copiedTimer.current);
    copiedTimer.current = setTimeout(() => setCopied(false), COPIED_MESSAGE_MS);
  }

  if (phase.kind === "loading-methods") return <p className="signup__note" role="status">Verificando as formas de pagamento…</p>;

  if (phase.kind === "no-method") {
    return <p className="auth-form__error" role="alert">Este evento não tem forma de pagamento disponível agora. Tente de novo em instantes.</p>;
  }

  if (phase.kind === "paid") {
    return (
      <div className="payment-pix">
        <p role="status">Pagamento confirmado! Seus ingressos estão na sua conta.</p>
        <Link className="n-btn n-btn--primary n-btn--lg" to="/conta/pedidos">Ver meus ingressos</Link>
      </div>
    );
  }

  if (phase.kind === "waiting") {
    const pix = phase.payment.pix!;

    return (
      <div className="payment-pix">
        <PixQrCode payment={phase.payment} />
        <p>Abra o aplicativo do seu banco, escolha pagar com Pix e leia o QR Code, ou use o Pix copia e cola.</p>
        <label className="n-field__label" htmlFor="pix-copy-paste">Pix copia e cola</label>
        <textarea id="pix-copy-paste" className="n-input n-figure" readOnly rows={3} value={pix.copyPasteCode} />
        <Button block size="lg" onClick={() => void copy(pix.copyPasteCode)}>{copied ? "Código copiado!" : "Copiar código Pix"}</Button>
        <PixCountdown expiresAt={Date.parse(phase.payment.expiresAt)} />
        <p className="signup__note" role="status">Aguardando o pagamento de {formatCents(phase.payment.amountCents)}. Esta tela confirma sozinha quando o banco avisar.</p>
        <p className="auth-form__error" role="alert">{error}</p>
      </div>
    );
  }

  return (
    <div className="payment-pix">
      <p>Geramos um código para você pagar no aplicativo do seu banco. Seus ingressos saem assim que o pagamento for confirmado.</p>
      <p className="auth-form__error" role="alert">{error}</p>
      <Button block size="lg" arrow aria-busy={phase.kind === "creating"} disabled={phase.kind === "creating"} onClick={() => void generate()}>
        {phase.kind === "creating" ? "Gerando o Pix…" : `Gerar Pix de ${formatCents(totalCents)}`}
      </Button>
    </div>
  );
}
