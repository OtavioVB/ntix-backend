import { useCallback, useMemo } from "react";
import { useCustomer } from "../customer/session";
import { confirmPayment, soldByBatch, type CustomerOrder } from "../customer/orders";
import { recordHubSale } from "../platform/hubSale";
import { useProducerSession } from "../producer/session";
import { buildCatalog, producerToHubEvent, type HubEvent } from "./catalog";

/** Todos os eventos à venda no hub (P-04) e o que este cliente já comprou deles. */
export function useHub() {
  const { session } = useProducerSession();
  const { customer } = useCustomer();
  const now = useMemo(() => Date.now(), []);

  const events = useMemo<HubEvent[]>(() => {
    const own = session.status === "ACTIVE" && session.platform
      ? session.platform.events
          .filter((event) => event.status === "ON_SALE" && event.batches.length > 0)
          .map((event) => producerToHubEvent(event, session.platform!, session.name || "Produtora"))
      : [];
    return [...buildCatalog(now), ...own];
  }, [now, session.status, session.platform, session.name]);

  // Venda de evento de produtor já entra nas vendas dele: não conta duas vezes.
  const extra = useMemo(() => soldByBatch(customer.orders.filter((order) => !order.event.fromProducer)), [customer.orders]);

  const findBySlug = useCallback((slug?: string) => events.find((event) => event.slug === slug), [events]);

  return { events, extra, now, findBySlug };
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
