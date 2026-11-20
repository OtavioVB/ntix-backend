-- Barreira de estoque no banco (REQ6, RN8): a última linha de defesa contra venda acima da quantidade do lote.
-- A aplicação já trava os lotes (SELECT ... FOR UPDATE, em ordem de id) antes de somar e gravar. Esta barreira vale mesmo para quem
-- esquecer a trava ou gravar por outro caminho: ao confirmar a transação, cada item gravado trava o lote e confere, com uma consulta
-- nova (feita depois da trava), se vendidos + reservados passam da quantidade. Passando, a confirmação falha com check_violation
-- (constraint ck_order_item_batch_stock) e nada é gravado.
-- Idempotente: pode ser executado mais de uma vez.

CREATE OR REPLACE FUNCTION orders.assert_batch_stock() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    batch_quantity INTEGER;
    used_quantity  BIGINT;
BEGIN
    -- Serializa com qualquer outra transação que reserve o mesmo lote. FOR NO KEY UPDATE, e não FOR UPDATE: a chave estrangeira do item já
    -- segura FOR KEY SHARE no lote, que conflita com FOR UPDATE e geraria deadlock entre duas transações; com NO KEY UPDATE elas esperam
    -- uma à outra em fila. A soma abaixo é outra consulta, feita já com a trava, então
    -- enxerga o que a transação anterior confirmou (no READ COMMITTED cada consulta tem a própria visão).
    SELECT quantity INTO batch_quantity
    FROM events.ticket_batch
    WHERE id_ticket_batch = NEW.id_ticket_batch
    FOR NO KEY UPDATE;

    -- Mesma regra da soma de vendidos e reservados: pedido pago, ou aguardando pagamento com a reserva ainda valendo.
    SELECT COALESCE(SUM(item.quantity), 0) INTO used_quantity
    FROM orders.order_item item
    JOIN orders.orders o ON o.id_order = item.id_order
    WHERE item.id_ticket_batch = NEW.id_ticket_batch
      AND (o.status = 'PAID' OR (o.status = 'PENDING_PAYMENT' AND o.expires_at > now()));

    IF used_quantity > batch_quantity THEN
        RAISE EXCEPTION 'O lote % teria % ingressos vendidos ou reservados para % de quantidade.', NEW.id_ticket_batch, used_quantity, batch_quantity
            USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_order_item_batch_stock';
    END IF;

    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS tr_order_item_batch_stock ON orders.order_item;

-- Adiada para o COMMIT: confere o pedido inteiro (todos os itens e o pedido já gravado), não item a item no meio da gravação.
CREATE CONSTRAINT TRIGGER tr_order_item_batch_stock
    AFTER INSERT ON orders.order_item
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION orders.assert_batch_stock();
