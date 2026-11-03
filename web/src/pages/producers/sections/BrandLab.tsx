import { useState } from "react";
import { Badge, Reveal, Scene } from "../../../design-system";
import { DEFAULT_STORE_LOOK, StoreControls, StorePreview } from "../../../features/store/StoreBuilder";
import { TICKETERIA_MONTHLY_CENTS } from "../../../platform/demoData";
import { formatCents } from "../../../platform/money";

type BrandLabProps = {
  name: string;
  onNameChange: (name: string) => void;
};

/** Ticketeria própria: serviço opcional, contratado à parte. O hub vale para todo produtor. */
export function BrandLab({ name, onNameChange }: BrandLabProps) {
  const [look, setLook] = useState(DEFAULT_STORE_LOOK);

  return (
    <Scene light="penumbra" className="brand-lab" id="ticketeria" aria-labelledby="brand-title">
      <div className="n-container brand-lab__grid">
        <Reveal className="brand-lab__controls">
          <Badge>Serviço opcional · {formatCents(TICKETERIA_MONTHLY_CENTS)} por mês · valor de exemplo</Badge>
          <h2 className="n-display n-display--md" id="brand-title">Quer ir além do hub? Tenha sua ticketeria.</h2>
          <p className="n-lead">Todo produtor vende no hub da Ntix. Quem contrata a ticketeria também ganha uma bilheteria própria, com o seu nome e as suas cores. Experimente:</p>
          <StoreControls name={name} onNameChange={onNameChange} look={look} onLookChange={setLook} />
        </Reveal>

        <Reveal delay={100} className="brand-lab__preview">
          <StorePreview name={name} look={look} />
          <p className="brand-lab__caption">Evento e preços são exemplos ilustrativos.</p>
        </Reveal>
      </div>
    </Scene>
  );
}
