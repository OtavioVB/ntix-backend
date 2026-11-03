import { Accordion, Badge, Reveal, Scene, type AccordionItem } from "../../../design-system";

const STEPS = [
  { title: "Cadastre o básico", text: "Dados da produtora e o acesso do administrador. É só o que pedimos para começar.", status: "Cadastro iniciado" },
  { title: "Conheça a plataforma", text: "Explore uma experiência reduzida antes de assumir qualquer compromisso.", status: "Cadastro iniciado" },
  { title: "Contrate e envie os dados", text: "Contrate o serviço, assine os termos e envie as informações jurídicas da sua produção.", status: "Em aprovação" },
  { title: "Aprovação da Ntix", text: "Nossa equipe analisa o cadastro. Com a aprovação, sua produtora fica ativa e pronta para vender.", status: "Ativo", live: true },
];

const QUESTIONS: AccordionItem[] = [
  {
    question: "Quanto custa usar a Ntix?",
    answer:
      "A plataforma cobra uma taxa de intermediação sobre as vendas, que pode ser fixa, percentual ou variar por faixa de valor. Ela é negociada com cada produtor e registrada em um plano com início e fim. Dependendo do contrato, também pode haver mensalidade e taxa de entrada.",
  },
  {
    question: "O que é a ticketeria própria?",
    answer: "É uma bilheteria com o nome e as cores da sua produtora, só com os seus eventos. É um serviço opcional, contratado à parte: sem ela, você vende no hub da Ntix e por afiliados. O valor de exemplo é de R$ 300,00 por mês.",
  },
  { question: "Preciso ter uma empresa?", answer: "Não. Pessoa física também pode se cadastrar, com recursos limitados em relação a uma empresa." },
  {
    question: "Posso conhecer a plataforma antes de contratar?",
    answer: "Sim. Com o cadastro inicial você já acessa uma experiência reduzida. Contrato, termos e documentos só entram quando você decide seguir em frente.",
  },
  {
    question: "Onde meus ingressos são vendidos?",
    answer: "Você não precisa ter loja nenhuma: seus eventos aparecem no hub da Ntix e, se você permitir no evento, afiliados podem divulgá-los. A ticketeria própria é opcional. O canal de cada venda fica registrado no pedido.",
  },
  { question: "Vocês antecipam recebíveis?", answer: "Não neste momento. A antecipação não faz parte da plataforma hoje." },
];

export function Journey() {
  return (
    <Scene light="casa" lightsUp className="journey" id="como-comecar" aria-labelledby="journey-title">
      <div className="n-container">
        <Reveal as="h2" className="n-display n-display--md journey__title" id="journey-title">
          Quatro passos até abrir as vendas.
        </Reveal>
        <ol className="journey__steps">
          {STEPS.map((step, index) => (
            <Reveal as="li" delay={index * 80} key={step.title}>
              <span className="journey__number n-figure">{String(index + 1).padStart(2, "0")}</span>
              <h3 className="n-title">{step.title}</h3>
              <p>{step.text}</p>
              <Badge live={step.live}>{step.status}</Badge>
            </Reveal>
          ))}
        </ol>

        <div className="faq" id="duvidas">
          <Reveal as="h2" className="n-display n-display--sm faq__title">Antes de você perguntar</Reveal>
          <Reveal>
            <Accordion items={QUESTIONS} defaultOpen={0} />
          </Reveal>
        </div>
      </div>
    </Scene>
  );
}
