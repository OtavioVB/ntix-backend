import type { ReactNode } from "react";
import { Badge, ButtonLink, Curtain, Icon, Ledger } from "../../design-system";
import { useProducerSession } from "../../producer/session";

function PreviewTable({ headers, rows }: { headers: string[]; rows: string[][] }) {
  return (
    <table className="n-ledger">
      <thead>
        <tr>{headers.map((header) => <th key={header}>{header}</th>)}</tr>
      </thead>
      <tbody>
        {rows.map((row, index) => (
          <tr key={index}>{row.map((cell, cellIndex) => <td key={cellIndex}>{cell}</td>)}</tr>
        ))}
      </tbody>
    </table>
  );
}

export type LockedDomain = {
  title: string;
  pitch: string;
  unlocks: string[];
  preview: ReactNode;
};

export const LOCKED_DOMAINS: Record<string, LockedDomain> = {
  vendas: {
    title: "Vendas e ingressos",
    pitch: "Todos os pedidos dos seus eventos, em um lugar só.",
    unlocks: ["Pedidos por evento, lote e canal de venda", "Ingressos individuais, cada um com código único", "Vendas atribuídas a cada afiliado"],
    preview: (
      <PreviewTable
        headers={["Pedido", "Evento", "Canal", "Valor"]}
        rows={[
          ["#10482", "Noite de Estreia", "Hub Ntix", "R$ 240,00"],
          ["#10481", "Noite de Estreia", "Hub Ntix", "R$ 120,00"],
          ["#10480", "Noite de Estreia", "Afiliado", "R$ 360,00"],
          ["#10479", "Noite de Estreia", "Hub Ntix", "R$ 120,00"],
          ["#10478", "Noite de Estreia", "Hub Ntix", "R$ 240,00"],
          ["#10477", "Noite de Estreia", "Hub Ntix", "R$ 120,00"],
        ]}
      />
    ),
  },
  portaria: {
    title: "Portaria",
    pitch: "Valide os ingressos na entrada com a câmera do celular.",
    unlocks: ["Leitura do QR Code pela câmera, sem aplicativo", "Cada ingresso entra uma vez só", "Quantas pessoas já entraram, em tempo real"],
    preview: (
      <PreviewTable
        headers={["Ingresso", "Leitura", "Hora"]}
        rows={[
          ["NT-10482-01", "Entrada liberada", "20:41"],
          ["NT-10481-02", "Entrada liberada", "20:40"],
          ["NT-10377-01", "Já utilizado", "20:40"],
          ["NT-10481-01", "Entrada liberada", "20:39"],
          ["NT-10480-03", "Entrada liberada", "20:39"],
          ["NT-10479-01", "Entrada liberada", "20:38"],
        ]}
      />
    ),
  },
  clientes: {
    title: "Clientes",
    pitch: "Conheça quem compra com você.",
    unlocks: ["Todos os compradores da sua produtora", "Histórico de compras de cada um", "Quem comprou mais de uma vez"],
    preview: (
      <PreviewTable
        headers={["Cliente", "Pedidos", "Última compra"]}
        rows={[
          ["Cliente 1", "3", "01/nov"],
          ["Cliente 2", "1", "31/out"],
          ["Cliente 3", "2", "30/out"],
          ["Cliente 4", "1", "30/out"],
          ["Cliente 5", "4", "29/out"],
          ["Cliente 6", "1", "28/out"],
        ]}
      />
    ),
  },
  afiliados: {
    title: "Afiliados",
    pitch: "Uma força de vendas que só ganha quando você vende.",
    unlocks: ["Escolha quais eventos aceitam afiliados", "Planos de comissão fixa, percentual ou mista", "Vendas e comissões de cada afiliado"],
    preview: (
      <PreviewTable
        headers={["Afiliado", "Ingressos", "Comissão"]}
        rows={[
          ["Afiliado 1", "31", "R$ 186,00"],
          ["Afiliado 2", "17", "R$ 102,00"],
          ["Afiliado 3", "10", "R$ 60,00"],
        ]}
      />
    ),
  },
  carteira: {
    title: "Carteira",
    pitch: "Cada centavo que entra e sai, com a explicação ao lado.",
    unlocks: ["Saldo atualizado depois de cada lançamento", "Venda, taxa e comissão separadas, com a regra aplicada", "Solicitação de saque"],
    preview: (
      <Ledger
        label="Exemplo de extrato"
        rows={[
          { title: "Venda de ingresso", detail: "Hub · 1º lote", amount: "+ 90,00", balance: "4.400,00" },
          { title: "Taxa de intermediação", detail: "Até R$ 100,00: fixa de R$ 1,00", amount: "− 1,00", balance: "4.399,00" },
          { title: "Venda de ingresso", detail: "Link de afiliado · 2º lote", amount: "+ 120,00", balance: "4.519,00" },
          { title: "Taxa de intermediação", detail: "Acima de R$ 100,00: 1%", amount: "− 1,20", balance: "4.517,80" },
          { title: "Comissão de afiliado", detail: "Plano do evento: 5%", amount: "− 6,00", balance: "4.511,80" },
        ]}
      />
    ),
  },
  cobrancas: {
    title: "Cobranças",
    pitch: "Tudo o que a Ntix cobra de você, sem surpresa.",
    unlocks: ["Cobranças recorrentes, pontuais e de taxas", "Prazo máximo de pagamento de cada uma", "O que está pago e o que está em aberto"],
    preview: (
      <PreviewTable
        headers={["Descrição", "Tipo", "Prazo", "Situação"]}
        rows={[
          ["Mensalidade", "Recorrente", "10/nov", "Em aberto"],
          ["Entrada do whitelabel", "Pontual", "05/out", "Paga"],
          ["Repasse de taxas", "Intermediação", "10/out", "Paga"],
        ]}
      />
    ),
  },
  plano: {
    title: "Plano de intermediação",
    pitch: "A sua taxa, negociada com você, com início e fim.",
    unlocks: ["As faixas de valor e o tipo de cada uma", "A vigência do plano atual", "O histórico de planos e renovações"],
    preview: (
      <PreviewTable
        headers={["Faixa", "Tipo", "Taxa"]}
        rows={[
          ["Até R$ 100,00", "Fixa", "R$ 1,00"],
          ["Acima de R$ 100,00", "Percentual", "1%"],
        ]}
      />
    ),
  },
};

export function LockedPage({ domain }: { domain: LockedDomain }) {
  const { session } = useProducerSession();
  const inReview = session.status === "PENDING_APPROVAL";

  return (
    <>
      <title>{`${domain.title} · Ntix`}</title>
      <Curtain preview={domain.preview}>
        <div className="locked-page">
          <Badge><Icon name="lock" /> Liberado após aprovação</Badge>
          <h1 className="n-display n-display--sm">{domain.title}</h1>
          <p className="n-lead">{domain.pitch}</p>
          <ul className="unlock-list">
            {domain.unlocks.map((unlock) => (
              <li key={unlock}><Icon name="check" /> {unlock}</li>
            ))}
          </ul>
          {inReview ? (
            <p className="inline-note">Seu cadastro está em análise. Esta área abre assim que a Ntix aprovar sua produtora.</p>
          ) : (
            <ButtonLink to="/painel/aprovacao" arrow block>Ver o que falta para liberar</ButtonLink>
          )}
        </div>
      </Curtain>
    </>
  );
}
