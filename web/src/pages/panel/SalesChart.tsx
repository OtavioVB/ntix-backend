import { useState } from "react";
import type { DailySales } from "../../api/events";
import { formatCents } from "../../platform/money";

const WIDTH = 640;
const HEIGHT = 180;
const PADDING_TOP = 12;
const PADDING_BOTTOM = 4;
const BAR_GAP = 2;

const DAY_FORMAT = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "short", timeZone: "UTC" });

function dayLabel(date: string): string {
  return DAY_FORMAT.format(new Date(`${date}T00:00:00Z`)).replace(".", "");
}

function describe(day: DailySales): string {
  const tickets = day.soldQuantity === 1 ? "1 ingresso" : `${day.soldQuantity} ingressos`;
  return `${dayLabel(day.date)}: ${tickets}, ${formatCents(day.grossCents)}`;
}

/**
 * Vendas por dia (REQ14): uma série só, barras finas na cor da marca, ancoradas na base e com topo arredondado.
 * Passar o mouse ou o foco numa barra mostra o dia; a tabela fica ao lado para quem não lê o gráfico.
 */
export function SalesChart({ days }: Readonly<{ days: DailySales[] }>) {
  const [active, setActive] = useState<number>();

  const max = Math.max(1, ...days.map((day) => day.soldQuantity));
  const slot = WIDTH / Math.max(1, days.length);
  const barWidth = Math.max(1, slot - BAR_GAP);
  const plotHeight = HEIGHT - PADDING_TOP - PADDING_BOTTOM;
  const totalTickets = days.reduce((sum, day) => sum + day.soldQuantity, 0);
  const totalGross = days.reduce((sum, day) => sum + day.grossCents, 0);
  const shown = active === undefined ? undefined : days[active];
  const first = days.at(0);
  const last = days.at(-1);

  return (
    <figure className="sales-chart">
      <figcaption className="sales-chart__summary n-figure">
        {shown ? describe(shown) : `${totalTickets} ingressos e ${formatCents(totalGross)} no período`}
      </figcaption>
      <svg
        className="sales-chart__plot"
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`Ingressos vendidos por dia: ${totalTickets} no período, no máximo ${max} em um dia.`}
        onMouseLeave={() => setActive(undefined)}
      >
        <line className="sales-chart__baseline" x1={0} x2={WIDTH} y1={HEIGHT - PADDING_BOTTOM} y2={HEIGHT - PADDING_BOTTOM} />
        {days.map((day, index) => {
          const height = day.soldQuantity === 0 ? 0 : Math.max(2, (day.soldQuantity / max) * plotHeight);
          const x = index * slot + BAR_GAP / 2;
          const radius = Math.min(4, barWidth / 2, height);

          return (
            <g key={day.date} onMouseEnter={() => setActive(index)}>
              {/* Área de toque maior que a barra, da altura do gráfico. */}
              <rect className="sales-chart__hit" x={index * slot} y={0} width={slot} height={HEIGHT} />
              {height > 0 && (
                <path
                  className={index === active ? "sales-chart__bar sales-chart__bar--active" : "sales-chart__bar"}
                  d={`M${x},${HEIGHT - PADDING_BOTTOM} v${-(height - radius)} q0,${-radius} ${radius},${-radius} h${barWidth - 2 * radius} q${radius},0 ${radius},${radius} v${height - radius} z`}
                />
              )}
              <title>{describe(day)}</title>
            </g>
          );
        })}
      </svg>
      {first && last && (
        <div className="sales-chart__axis" aria-hidden="true">
          <span>{dayLabel(first.date)}</span>
          <span>{dayLabel(last.date)}</span>
        </div>
      )}
      <details className="sales-chart__table">
        <summary>Ver em tabela</summary>
        <table>
          <thead>
            <tr><th scope="col">Dia</th><th scope="col">Ingressos</th><th scope="col">Vendas brutas</th><th scope="col">Pedidos</th></tr>
          </thead>
          <tbody>
            {days.map((day) => (
              <tr key={day.date}>
                <th scope="row">{dayLabel(day.date)}</th>
                <td className="n-figure">{day.soldQuantity}</td>
                <td className="n-figure">{formatCents(day.grossCents)}</td>
                <td className="n-figure">{day.paidOrders}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  );
}
