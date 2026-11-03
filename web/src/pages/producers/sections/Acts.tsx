import { useRef, type ReactNode } from "react";
import { Badge, Ledger, Meter, Reveal, Scene, Window, useActiveAct, type LedgerRow } from "../../../design-system";

const BATCHES = [
  { name: "1º lote", status: "Encerrado", live: false, price: "R$ 90,00", sold: 200, total: 200, window: "até 20/jan" },
  { name: "2º lote", status: "Em venda", live: true, price: "R$ 120,00", sold: 142, total: 300, window: "até 20/fev" },
  { name: "3º lote", status: "Agendado", live: false, price: "R$ 150,00", sold: 0, total: 300, window: "abre em 21/fev" },
];

const LEDGER: LedgerRow[] = [
  { title: "Venda de ingresso", detail: "Hub · 1º lote", amount: "+ 80,00", balance: "4.390,00" },
  { title: "Taxa de intermediação", detail: "Até R$ 100,00: fixa de R$ 1,00", amount: "− 1,00", balance: "4.389,00" },
  { title: "Venda de ingresso", detail: "Link de afiliado · 2º lote", amount: "+ 120,00", balance: "4.509,00" },
  { title: "Taxa de intermediação", detail: "Acima de R$ 100,00: 1%", amount: "− 1,20", balance: "4.507,80" },
  { title: "Comissão de afiliado", detail: "Plano do evento: 5%", amount: "− 6,00", balance: "4.501,80" },
];

type ActProps = {
  id: string;
  active: boolean;
  numeral: string;
  title: string;
  lead: string;
  children: ReactNode;
};

function Act({ id, active, numeral, title, lead, children }: ActProps) {
  return (
    <article className={active ? "act is-active" : "act"} data-act={id}>
      <Reveal className="act__copy">
        <p className="act__numeral" aria-hidden="true">{numeral}</p>
        <h3 className="n-display n-display--sm">{title}</h3>
        <p className="n-lead">{lead}</p>
      </Reveal>
      <Reveal className="act__panel">{children}</Reveal>
    </article>
  );
}

function bar(left: string, right: string) {
  return (
    <>
      <span>{left}</span>
      <span className="act__bar-end">{right}</span>
    </>
  );
}

export function Acts() {
  const rootRef = useRef<HTMLDivElement>(null);
  const activeAct = useActiveAct(rootRef);

  return (
    <Scene light="palco" className="acts" id="operacao" data-current-act={activeAct} aria-labelledby="acts-title">
      <div className="n-backdrop"><div className="acts__glow" /></div>
      <div className="n-container" ref={rootRef}>
        <Reveal as="header" className="acts__intro">
          <h2 className="n-display n-display--md" id="acts-title">Do primeiro lote ao último centavo.</h2>
        </Reveal>

        <Act
          id="1"
          active={activeAct === "1"}
          numeral="I"
          title="Lotes que abrem e fecham na hora marcada"
          lead="Crie quantos eventos quiser e, em cada um, os lotes que precisar, cada um com preço, quantidade e período de venda."
        >
          <Window bar={bar("Noite de Estreia", "Lotes")}>
            <ul className="batches">
              {BATCHES.map((batch) => (
                <li key={batch.name}>
                  <div className="batches__head">
                    <strong>{batch.name}</strong>
                    <Badge live={batch.live}>{batch.status}</Badge>
                  </div>
                  <Meter value={batch.sold} max={batch.total} label={`${batch.sold} de ${batch.total} ingressos vendidos`} />
                  <p className="batches__meta">
                    <span className="n-figure">{batch.price}</span>
                    <span className="n-figure">{batch.sold} de {batch.total}</span>
                    <span>{batch.window}</span>
                  </p>
                </li>
              ))}
            </ul>
          </Window>
        </Act>

        <Act
          id="2"
          active={activeAct === "2"}
          numeral="II"
          title="Três caminhos até o seu público"
          lead="O hub reúne os eventos de todos os produtores da Ntix. Afiliados independentes divulgam com links rastreáveis, se você permitir. E a ticketeria própria, se contratada, fala com quem já é seu público."
        >
          <Window bar={bar("Noite de Estreia", "Canais de venda")}>
            <ul className="channels">
              <li>
                <span className="channels__icon" aria-hidden="true">1</span>
                <div><strong>Hub Ntix</strong><p>Vitrine com os eventos de todos os produtores. Vale para todos.</p></div>
              </li>
              <li>
                <span className="channels__icon" aria-hidden="true">2</span>
                <div><strong>Ticketeria própria</strong><p>Serviço opcional: domínio e identidade da produtora, só com os seus eventos.</p></div>
              </li>
              <li>
                <span className="channels__icon" aria-hidden="true">3</span>
                <div>
                  <strong>Links de afiliados</strong>
                  <p>Cada venda é atribuída ao afiliado que a originou.</p>
                  <code className="channels__link">noite-de-estreia?ref=a7f3c2</code>
                </div>
              </li>
            </ul>
          </Window>
        </Act>

        <Act
          id="3"
          active={activeAct === "3"}
          numeral="III"
          title="Uma carteira que explica cada movimento"
          lead="Venda, taxa e comissão aparecem em lançamentos separados, com a regra aplicada e o saldo depois de cada um. Se o valor mudou, você sabe por quê."
        >
          <Window bar={bar("Carteira do produtor", "Exemplo")}>
            <Ledger label="Exemplo de extrato da carteira do produtor" rows={LEDGER} />
          </Window>
          <p className="brand-lab__caption">Valores e regras são exemplos. Sua taxa é definida no plano negociado com a Ntix.</p>
        </Act>
      </div>
    </Scene>
  );
}
