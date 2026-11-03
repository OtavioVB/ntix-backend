import type { CSSProperties, ReactNode } from "react";

export function Badge({ live, children }: { live?: boolean; children: ReactNode }) {
  return <span className={live ? "n-badge n-badge--live" : "n-badge"}>{children}</span>;
}

/** Enquadra telas do produto. */
export function Window({ bar, children }: { bar: ReactNode; children: ReactNode }) {
  return (
    <div className="n-window">
      <div className="n-window__bar">{bar}</div>
      {children}
    </div>
  );
}

/** Progresso de um único valor. Mostre sempre o número ao lado: a barra não basta. */
export function Meter({ value, max, label }: { value: number; max: number; label: string }) {
  return (
    <div className="n-meter" role="img" aria-label={label}>
      <span style={{ width: `${(value / max) * 100}%` }} />
    </div>
  );
}

type SwatchProps = { color: string; pressed: boolean; onClick: () => void; children: ReactNode };

export function Swatch({ color, pressed, onClick, children }: SwatchProps) {
  return (
    <button
      type="button"
      className="n-swatch"
      style={{ "--_swatch": color } as CSSProperties}
      aria-pressed={pressed}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

export type LedgerRow ={ title: string; detail: string; amount: string; balance: string };

/** Extrato: cada lançamento com a origem e o saldo depois dele. */
export function Ledger({ label, rows }: { label: string; rows: readonly LedgerRow[] }) {
  return (
    <div className="n-ledger-scroll" tabIndex={0} role="region" aria-label={label}>
      <table className="n-ledger">
        <thead>
          <tr>
            <th scope="col">Lançamento</th>
            <th scope="col" className="is-number">Valor</th>
            <th scope="col" className="is-number">Saldo</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              <td>
                {row.title}
                <small>{row.detail}</small>
              </td>
              <td className="is-number">{row.amount}</td>
              <td className="is-number">{row.balance}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export type AccordionItem = { question: string; answer: string };

export function Accordion({ items, defaultOpen }: { items: readonly AccordionItem[]; defaultOpen?: number }) {
  return (
    <div className="n-accordion">
      {items.map((item, index) => (
        <details key={item.question} open={index === defaultOpen}>
          <summary>{item.question}</summary>
          <p>{item.answer}</p>
        </details>
      ))}
    </div>
  );
}
