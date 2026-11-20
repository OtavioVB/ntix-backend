import { useCallback, useEffect, useMemo, useState } from "react";
import { isRealApi } from "../api/mode";
import { useCustomer } from "../customer/session";
import { confirmPayment, soldByBatch, type CustomerOrder } from "../customer/orders";
import { recordHubSale } from "../platform/hubSale";
import { useProducerSession } from "../producer/session";
import { buildCatalog, producerToHubEvent, type HubEvent } from "./catalog";
import { loadPublicCatalog } from "./publicCatalog";

/**
 * Vitrine real (REQ11, `?api=real`): os eventos publicados vêm da API. Enquanto carrega, a lista fica vazia; se a API
 * falhar, também, e `loadError` avisa a tela.
 */
function usePublicCatalog(enabled: boolean) {
  const [events, setEvents] = useState<HubEvent[]>([]);
  const [loading, setLoading] = useState(enabled);
  const [loadError, setLoadError] = useState(false);

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();

    loadPublicCatalog(undefined, controller.signal)
      .then((loaded) => {
        if (controller.signal.aborted) return;
        setEvents(loaded ?? []);
        setLoadError(loaded === null);
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, [enabled]);

  return { events, loading, loadError };
}

/** Todos os eventos à venda no hub (P-04) e o que este cliente já comprou deles. */
export function useHub() {
  const { session } = useProducerSession();
  const { customer } = useCustomer();
  const now = useMemo(() => Date.now(), []);
  const real = isRealApi();
  const publicCatalog = usePublicCatalog(real);

  const demoEvents = useMemo<HubEvent[]>(() => {
    const own = session.status === "ACTIVE" && session.platform
      ? session.platform.events
          .filter((event) => event.status === "ON_SALE" && event.batches.length > 0)
          .map((event) => producerToHubEvent(event, session.platform!, session.name || "Produtora"))
      : [];
    return [...buildCatalog(now), ...own];
  }, [now, session.status, session.platform, session.name]);

  const events = real ? publicCatalog.events : demoEvents;

  // Venda de evento de produtor já entra nas vendas dele: não conta duas vezes. Na vitrine real, a API já desconta.
  const extra = useMemo(() => (real ? {} : soldByBatch(customer.orders.filter((order) => !order.event.fromProducer))), [real, customer.orders]);

  const findBySlug = useCallback((slug?: string) => events.find((event) => event.slug === slug), [events]);

  return { events, extra, now, findBySlug, loading: real && publicCatalog.loading, loadError: real && publicCatalog.loadError };
}

/** Registra um pedido pago: ingressos do cliente e, se o evento for de um produtor da sessão, a venda dele. */
export function usePurchase() {
  const { customer, update, updateOrder } = useCustomer();
  const { session, update: updateProducer } = useProducerSession();

  const creditProducer = useCallback(
    (order: CustomerOrder, at: number) => {
      if (!order.event.fromProducer || !session.platform || !customer.account) return;
      updateProducer({
        platform: recordHubSale(session.platform, {
          orderCode: order.code,
          eventId: order.event.id,
          eventName: order.event.title,
          customer: { email: customer.account.email, name: customer.account.name },
          items: order.items,
          at,
        }),
      });
    },
    [customer.account, session.platform, updateProducer],
  );

  const addOrder = useCallback(
    (order: CustomerOrder) => {
      update({ orders: [order, ...customer.orders], cart: null });
      if (order.payment.status === "PAID") {
        creditProducer(order, order.createdAt);
      }
    },
    [creditProducer, customer.orders, update],
  );

  const payPending = useCallback(
    (order: CustomerOrder) => {
      const now = Date.now();
      updateOrder(order.code, (current) => confirmPayment(current, now));
      creditProducer(order, now);
    },
    [creditProducer, updateOrder],
  );

  return { addOrder, payPending };
}
