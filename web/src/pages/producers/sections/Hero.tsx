import { Audience, Beam, FollowSpot, Reveal, Scene } from "../../../design-system";
import { SignupTicket } from "./SignupTicket";

type HeroProps = {
  suggestedName: string;
  onSignupComplete: () => void;
};

export function Hero({ suggestedName, onSignupComplete }: HeroProps) {
  return (
    <Scene light="palco" fixed spotlight className="hero" id="inicio" aria-labelledby="hero-title">
      <div className="n-backdrop">
        <Audience className="hero__audience" />
        <div className="hero__scrim" />
        <Beam x="18%" width="44vw" tilt={14} swing={-6} power={22} sway={17} />
        <Beam x="52%" width="38vw" tilt={-5} swing={7} power={15} sway={13} />
        <Beam x="86%" width="40vw" tilt={-12} swing={5} power={26} sway={19} />
        <FollowSpot />
      </div>

      <div className="n-container hero__grid">
        <div className="hero__copy">
          <Reveal immediate as="p" className="n-label n-label--accent">Para produtoras de eventos</Reveal>
          <Reveal immediate as="h1" delay={60} className="n-display n-display--lg hero__title" id="hero-title">
            Seu evento.
            <br />
            <span className="hero__title-accent">Sua bilheteria.</span>
          </Reveal>
          <Reveal immediate as="p" delay={120} className="n-lead hero__lead">
            Venda ingressos no hub de eventos da Ntix, conte com afiliados para divulgar e acompanhe cada movimento em
            uma carteira que explica de onde veio o dinheiro. Se quiser, tenha também a sua ticketeria própria.
          </Reveal>
          <Reveal immediate as="ul" delay={180} className="hero__facts">
            <li><strong>Hub de eventos</strong> para vender desde a aprovação</li>
            <li><strong>Pessoa física ou jurídica</strong> podem se cadastrar</li>
            <li><strong>Taxa negociada</strong> com você, em plano com início e fim</li>
          </Reveal>
        </div>

        <Reveal immediate delay={220} className="hero__ticket" id="cadastro">
          <SignupTicket suggestedName={suggestedName} onComplete={onSignupComplete} />
        </Reveal>
      </div>
    </Scene>
  );
}
