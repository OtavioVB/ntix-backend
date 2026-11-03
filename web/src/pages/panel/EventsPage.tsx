import { Link } from "react-router-dom";
import { Badge, Icon, Meter, Window } from "../../design-system";
import { SAMPLE_EVENT, money } from "./sampleData";

export function EventsPage() {
  return (
    <>
      <title>Eventos e lotes · Ntix</title>
      <header className="page-head page-head--action">
        <div>
          <h1 className="n-display n-display--md">Eventos e lotes</h1>
          <p className="n-lead">Cada evento tem quantos lotes precisar, com preço, quantidade e período de venda próprios.</p>
        </div>
        <Link className="n-btn n-btn--ghost" to="/painel/aprovacao">
          <Icon name="lock" /> Criar evento
        </Link>
      </header>
      <p className="inline-note">No ensaio você explora um evento de exemplo. Criar os seus eventos é liberado depois da aprovação.</p>

      <Window
        bar={
          <>
            <span>{SAMPLE_EVENT.name} · {SAMPLE_EVENT.when} · {SAMPLE_EVENT.place}</span>
            <span className="window-end">Exemplo</span>
          </>
        }
      >
        <ul className="batch-table">
          {SAMPLE_EVENT.batches.map((batch) => (
            <li key={batch.name}>
              <div className="batch-table__name">
                <strong>{batch.name}</strong>
                <Badge live={batch.live}>{batch.status}</Badge>
              </div>
              <Meter value={batch.sold} max={batch.total} label={`${batch.sold} de ${batch.total} ingressos vendidos`} />
              <dl className="batch-table__data">
                <div><dt>Preço</dt><dd className="n-figure">{money(batch.price)}</dd></div>
                <div><dt>Vendidos</dt><dd className="n-figure">{batch.sold} de {batch.total}</dd></div>
                <div><dt>Período</dt><dd>{batch.window}</dd></div>
              </dl>
            </li>
          ))}
        </ul>
      </Window>
    </>
  );
}
