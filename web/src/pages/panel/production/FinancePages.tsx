import { useMemo, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Badge, Button, Ledger, TextField, type LedgerRow } from "../../../design-system";
import { requestWithdrawal } from "../../../platform/actions";
import { DAY_MS } from "../../../platform/demoData";
import { formatCents, formatSigned, parseCents } from "../../../platform/money";
import { currentBalance, currentFeePlan, fullDate } from "../../../platform/selectors";
import type { ChargeType, LedgerType } from "../../../platform/types";
import { usePlatform } from "./usePlatform";

const LEDGER_FILTERS: { value: "" | LedgerType; label: string }[] = [
  { value: "", label: "Todos" },
  { value: "SALE", label: "Vendas" },
  { value: "FEE", label: "Taxas" },
  { value: "COMMISSION", label: "Comissões" },
  { value: "WITHDRAWAL", label: "Saques" },
];

const PAGE_SIZE = 25;

export function WalletPage() {
  const { data, save } = usePlatform();
  const [type, setType] = useState<"" | LedgerType>("");
  const [limit, setLimit] = useState(PAGE_SIZE);
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<string>();
  const [confirmation, setConfirmation] = useState<string>();

  const balance = currentBalance(data);
  const entries = useMemo(() => [...data.ledger].reverse().filter((entry) => !type || entry.type === type), [data.ledger, type]);
  const rows: LedgerRow[] = entries.slice(0, limit).map((entry) => ({
    title: `${entry.description} · ${fullDate(entry.at)}`,
    detail: entry.detail,
    amount: formatSigned(entry.amountCents),
    balance: (entry.balanceCents / 100).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
  }));

  function handleWithdraw(event: FormEvent) {
    event.preventDefault();
    setConfirmation(undefined);
    const cents = parseCents(amount);
    if (cents === null) {
      setError("Informe um valor, como 1.000,00.");
      return;
    }
    const result = requestWithdrawal(data, cents, Date.now());
    if ("error" in result) {
      setError(result.error);
      return;
    }
    save(result.data);
    setError(undefined);
    setAmount("");
    setConfirmation(`Saque de ${formatCents(cents)} registrado no extrato.`);
  }

  return (
    <>
      <title>Carteira · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Carteira</h1>
        <p className="n-lead">Cada lançamento mostra a origem, a regra aplicada e o saldo depois dele.</p>
      </header>

      <div className="wallet">
        <div className="stack">
          <section className="tile" aria-labelledby="wallet-balance-title">
            <h2 className="tile__title" id="wallet-balance-title">Saldo</h2>
            <p className="side-figure">{formatCents(balance)}</p>
          </section>
          <form className="tile form-tile" onSubmit={handleWithdraw} noValidate aria-labelledby="withdraw-title">
            <h2 className="tile__title" id="withdraw-title">Solicitar saque</h2>
            <TextField id="withdraw-amount" label="Valor (R$)" inputMode="decimal" className="n-figure" value={amount} error={error} onChange={(event) => setAmount(event.target.value)} />
            <Button type="submit" block>Solicitar saque</Button>
            <p className="team-form__status" role="status">{confirmation}</p>
            <p className="tile__note">Valor mínimo, prazos e conta de destino ainda estão em definição. Demonstração: nenhum dinheiro é movimentado.</p>
          </form>
        </div>

        <section className="tile" aria-labelledby="statement-title">
          <div className="tile__head">
            <h2 className="tile__title" id="statement-title">Extrato</h2>
            <div className="n-field">
              <label className="n-visually-hidden" htmlFor="ledger-type">Tipo de lançamento</label>
              <select id="ledger-type" className="n-input" value={type} onChange={(event) => { setType(event.target.value as "" | LedgerType); setLimit(PAGE_SIZE); }}>
                {LEDGER_FILTERS.map((filter) => <option key={filter.value} value={filter.value}>{filter.label}</option>)}
              </select>
            </div>
          </div>
          {rows.length === 0 ? (
            <p className="tile__note">Nenhum lançamento {type ? "deste tipo" : "ainda"}.</p>
          ) : (
            <Ledger label="Extrato da carteira" rows={rows} />
          )}
          {entries.length > limit && (
            <button type="button" className="load-more" onClick={() => setLimit((current) => current + PAGE_SIZE)}>
              Mostrar mais ({entries.length - limit} restantes)
            </button>
          )}
        </section>
      </div>
    </>
  );
}

const CHARGE_LABELS: Record<ChargeType, string> = { RECURRING: "Recorrente", ONE_TIME: "Pontual", INTERMEDIATION_FEE: "Repasse de taxas" };

export function ChargesPage() {
  const { data, save } = usePlatform();
  const sorted = [...data.charges].sort((a, b) => b.dueDate - a.dueDate);
  const open = data.charges.filter((charge) => !charge.paidAt).reduce((sum, charge) => sum + charge.amountCents, 0);

  return (
    <>
      <title>Cobranças · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Cobranças</h1>
        <p className="n-lead">Tudo o que a Ntix cobra de você, com prazo e situação. A taxa de intermediação é descontada direto na carteira, venda a venda.</p>
      </header>

      <dl className="stat-row">
        <div className="tile"><dt>Em aberto</dt><dd>{formatCents(open)}</dd></div>
        <div className="tile"><dt>Cobranças</dt><dd>{data.charges.length}</dd></div>
      </dl>

      {sorted.length === 0 ? (
        <section className="tile empty-state">
          <h2 className="n-title">Nenhuma cobrança</h2>
          <p>Sem ticketeria contratada, você não tem mensalidade: só a taxa de intermediação de cada venda. <Link to="/painel/ticketeria">Conheça a ticketeria</Link></p>
        </section>
      ) : (
        <section className="tile" aria-label="Lista de cobranças">
          <div className="n-ledger-scroll">
            <table className="n-ledger">
              <thead>
                <tr><th scope="col">Descrição</th><th scope="col">Tipo</th><th scope="col">Prazo</th><th scope="col">Situação</th><th scope="col" className="is-number">Valor</th><th scope="col"><span className="n-visually-hidden">Ação</span></th></tr>
              </thead>
              <tbody>
                {sorted.map((charge) => (
                  <tr key={charge.id}>
                    <td>{charge.description}</td>
                    <td>{CHARGE_LABELS[charge.type]}</td>
                    <td>{fullDate(charge.dueDate)}</td>
                    <td><Badge live={!charge.paidAt}>{charge.paidAt ? `Paga em ${fullDate(charge.paidAt)}` : "Em aberto"}</Badge></td>
                    <td className="is-number">{formatCents(charge.amountCents)}</td>
                    <td>
                      {!charge.paidAt && (
                        <button
                          type="button"
                          className="table-link"
                          onClick={() => save({ ...data, charges: data.charges.map((other) => (other.id === charge.id ? { ...other, paidAt: Date.now() } : other)) })}
                        >
                          Simular pagamento
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </>
  );
}

export function PlanPage() {
  const { data } = usePlatform();
  const now = useMemo(() => Date.now(), []);
  const plan = currentFeePlan(data, now);
  const totalDays = Math.max(1, Math.round((plan.endsAt - plan.startsAt) / DAY_MS));
  const daysLeft = Math.max(0, Math.ceil((plan.endsAt - now) / DAY_MS));
  const paid = data.orders.reduce((sum, order) => sum + order.feeCents, 0);
  const gross = data.orders.reduce((sum, order) => sum + order.totalCents, 0);

  return (
    <>
      <title>Plano de intermediação · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Plano de intermediação</h1>
        <p className="n-lead">A sua taxa, negociada com você e registrada com início e fim. A faixa em que o valor se enquadra vale para o valor inteiro.</p>
      </header>

      <dl className="stat-row">
        <div className="tile"><dt>Termina em</dt><dd>{daysLeft} <small>dias</small></dd></div>
        <div className="tile"><dt>Taxas pagas até aqui</dt><dd>{formatCents(paid)}</dd></div>
        <div className="tile"><dt>Taxa efetiva</dt><dd>{gross ? ((paid / gross) * 100).toLocaleString("pt-BR", { maximumFractionDigits: 2 }) : "0"}%</dd></div>
      </dl>

      <section className="tile" aria-labelledby="plan-title">
        <div className="tile__head">
          <h2 className="tile__title" id="plan-title">Plano atual</h2>
          <Badge live>Vigente</Badge>
        </div>
        <p className="inline-note">De {fullDate(plan.startsAt)} a {fullDate(plan.endsAt)} · {totalDays} dias</p>
        <div className="n-ledger-scroll">
          <table className="n-ledger">
            <thead>
              <tr><th scope="col">Faixa</th><th scope="col">Tipo</th><th scope="col" className="is-number">Taxa</th></tr>
            </thead>
            <tbody>
              {plan.tiers.map((tier, index) => {
                const previous = plan.tiers[index - 1];
                return (
                  <tr key={index}>
                    <td>{tier.upToCents === null ? `Acima de ${formatCents(previous?.upToCents ?? 0)}` : `Até ${formatCents(tier.upToCents)}`}</td>
                    <td>{tier.type === "FIXED" ? "Fixa" : "Percentual"}</td>
                    <td className="is-number">{tier.type === "FIXED" ? formatCents(tier.value) : `${tier.value}%`}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <p className="tile__note">Quando o plano termina, um novo é gerado como renovação. Para negociar outras condições, fale com a equipe Ntix.</p>
      </section>
    </>
  );
}
