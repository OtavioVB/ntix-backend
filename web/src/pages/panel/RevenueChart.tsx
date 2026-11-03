import { useState, type CSSProperties, type KeyboardEvent } from "react";

export type ChartPoint = { label: string; tickets: number; revenue: number };

function money(value: number): string {
  return value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function niceMax(value: number): number {
  const step = value > 20_000 ? 5_000 : value > 4_000 ? 1_000 : 500;
  return Math.max(step, Math.ceil(value / step) * step);
}

function axisLabel(value: number): string {
  return value >= 1000 ? `R$ ${(value / 1000).toLocaleString("pt-BR")} mil` : `R$ ${value}`;
}

type RevenueChartProps = {
  days: ChartPoint[];
  peakLabel?: string;
};

/** Receita ao longo do tempo: colunas de uma série só, com leitura por ponteiro, teclado e tabela. */
export function RevenueChart({ days, peakLabel = "Melhor dia" }: RevenueChartProps) {
  const [active, setActive] = useState<number | null>(null);
  const peak = days.reduce((best, day, index) => (day.revenue > days[best].revenue ? index : best), 0);
  const max = niceMax(days[peak].revenue);
  const ticks = [max, max / 2, 0];
  const shown = active !== null && active < days.length ? active : peak;
  const position = ((shown + 0.5) / days.length) * 100;

  function onKeyDown(event: KeyboardEvent) {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();
    const step = event.key === "ArrowRight" ? 1 : -1;
    setActive(Math.min(days.length - 1, Math.max(0, (active ?? days.length) + step)));
  }

  return (
    <figure className="revenue-chart">
      <div
        className="revenue-chart__frame"
        tabIndex={0}
        role="group"
        aria-label={`Receita de ${days[0].label} a ${days[days.length - 1].label}. Use as setas para ler cada período.`}
        onKeyDown={onKeyDown}
        onBlur={() => setActive(null)}
        onPointerLeave={() => setActive(null)}
      >
        <div className="revenue-chart__axis" aria-hidden="true">
          {ticks.map((tick) => (
            <span key={tick}>{axisLabel(tick)}</span>
          ))}
        </div>
        <div className="revenue-chart__plot">
          {ticks.map((tick) => (
            <i className="revenue-chart__grid" key={tick} style={{ bottom: `${(tick / max) * 100}%` }} />
          ))}
          <div className="revenue-chart__bars">
            {days.map((day, index) => (
              <div
                className={index === active ? "revenue-chart__col is-active" : "revenue-chart__col"}
                key={day.label}
                onPointerEnter={() => setActive(index)}
              >
                <span style={{ height: `${(day.revenue / max) * 100}%` }} />
              </div>
            ))}
          </div>
          <div
            className="revenue-chart__readout"
            role="status"
            data-side={position > 60 ? "left" : "right"}
            style={{ "--_x": `${position}%`, "--_y": `${(days[shown].revenue / max) * 100}%` } as CSSProperties}
          >
            <strong>{money(days[shown].revenue)}</strong>
            <span>{active === null ? `${peakLabel} · ${days[shown].label}` : `${days[shown].label} · ${days[shown].tickets} ingressos`}</span>
          </div>
        </div>
        <div className="revenue-chart__dates" aria-hidden="true">
          <span>{days[0].label}</span>
          <span>{days[Math.floor(days.length / 2)].label}</span>
          <span>{days[days.length - 1].label}</span>
        </div>
      </div>

      <details className="revenue-chart__table">
        <summary>Ver como tabela</summary>
        <div className="n-ledger-scroll">
          <table className="n-ledger">
            <thead>
              <tr>
                <th scope="col">Período</th>
                <th scope="col" className="is-number">Ingressos</th>
                <th scope="col" className="is-number">Receita</th>
              </tr>
            </thead>
            <tbody>
              {days.map((day) => (
                <tr key={day.label}>
                  <td>{day.label}</td>
                  <td className="is-number">{day.tickets}</td>
                  <td className="is-number">{money(day.revenue)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
