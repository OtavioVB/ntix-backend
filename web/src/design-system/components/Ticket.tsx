import type { ReactNode } from "react";

type TicketProps = {
  stub: ReactNode;
  /** Destaca o canhoto: use quando a ação do ingresso foi concluída. */
  torn?: boolean;
  children: ReactNode;
};

/** O componente-assinatura: canhoto e corpo separados por uma linha de picote. */
export function Ticket({ stub, torn = false, children }: TicketProps) {
  return (
    <div className="n-ticket" data-torn={torn}>
      <div className="n-ticket__stub">{stub}</div>
      <div className="n-ticket__main">{children}</div>
    </div>
  );
}

export function Stamp({ children }: { children: ReactNode }) {
  return <span className="n-stamp">{children}</span>;
}
