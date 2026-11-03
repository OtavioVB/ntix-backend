import { useRef, useState } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { Button, Checkbox, Icon, Stamp, TextField, Ticket } from "../../design-system";
import { TICKETERIA_MONTHLY_CENTS, generateDemoData } from "../../platform/demoData";
import { formatCents } from "../../platform/money";
import { formatDocument, isValidDocument } from "../../producer/legal";
import { approvalSteps, submitForApproval, useProducerSession } from "../../producer/session";

function StepStatus({ done }: { done: boolean }) {
  return (
    <span className={done ? "approval-step__status is-done" : "approval-step__status"}>
      {done ? <><Icon name="check" /> Pronto</> : "Pendente"}
    </span>
  );
}

export function ApprovalPage() {
  const { session, update } = useProducerSession();
  const navigate = useNavigate();
  const [submitting, setSubmitting] = useState(false);
  const [missing, setMissing] = useState<string[]>([]);
  const [documentError, setDocumentError] = useState<string>();
  const summaryRef = useRef<HTMLParagraphElement>(null);

  if (session.status === "ACTIVE") return <Navigate to="/painel" replace />;

  const steps = approvalSteps(session);
  const doneCount = steps.filter((step) => step.done).length;
  const stepDone = (id: string) => steps.find((step) => step.id === id)?.done ?? false;
  const isCompany = session.producerType === "COMPANY";
  const submitted = session.status === "PENDING_APPROVAL";
  const withTicketeria = session.ticketeria === "SELECTED";

  async function handleSubmit() {
    const pending = steps.filter((step) => !step.done).map((step) => step.title);
    setMissing(pending);
    if (pending.length) {
      summaryRef.current?.focus();
      return;
    }
    setSubmitting(true);
    await submitForApproval();
    setSubmitting(false);
    update({ status: "PENDING_APPROVAL" });
  }

  /** A aprovação é feita pela Ntix (P-07). Sem backoffice, a demonstração simula esse passo. */
  function simulateApproval() {
    update({
      status: "ACTIVE",
      ticketeria: withTicketeria ? "ACTIVE" : "NONE",
      platform: generateDemoData(Date.now(), withTicketeria),
    });
    navigate("/painel", { state: { openCurtain: true } });
  }

  const stub = (
    <>
      <p className="n-label n-label--accent">Sua estreia</p>
      <p className="approval-count">{submitted ? "Enviado" : `${doneCount} de ${steps.length}`}</p>
    </>
  );

  return (
    <>
      <title>Enviar para aprovação · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">{submitted ? "Tudo enviado. Agora é com a gente." : "Três passos para abrir as cortinas."}</h1>
        <p className="n-lead">
          {submitted
            ? "A equipe Ntix vai analisar seu cadastro. Enquanto isso, o ensaio continua aberto."
            : "Conclua as etapas e envie para a equipe Ntix. Com a aprovação, sua produtora fica ativa e toda a plataforma é liberada."}
        </p>
      </header>

      <div className="approval">
        <fieldset className="approval__steps" disabled={submitted}>
          <legend className="n-visually-hidden">Etapas para enviar o cadastro</legend>

          <section className="approval-step" aria-labelledby="step-contract">
            <div className="approval-step__head">
              <h2 className="n-title" id="step-contract">1. Contrate o serviço</h2>
              <StepStatus done={stepDone("contract")} />
            </div>
            <p>A taxa de intermediação é negociada com cada produtor e fica registrada em um plano com início e fim. Dependendo do contrato, também pode haver mensalidade e taxa de entrada.</p>
            <Checkbox checked={session.contractAccepted} onChange={(event) => update({ contractAccepted: event.target.checked })}>
              Quero contratar a Ntix para vender ingressos dos meus eventos.
            </Checkbox>
            <div className="approval-addon">
              <Icon name="store" />
              {withTicketeria ? (
                <p>
                  <strong>Ticketeria própria incluída:</strong> {formatCents(TICKETERIA_MONTHLY_CENTS)} por mês (valor de exemplo).{" "}
                  {!submitted && <button type="button" className="signup__back" onClick={() => update({ ticketeria: "NONE" })}>Remover</button>}
                </p>
              ) : (
                <p>
                  <strong>Ticketeria própria:</strong> não incluída. {!submitted && <Link to="/painel/ticketeria">Ver como ficaria</Link>}
                </p>
              )}
            </div>
          </section>

          <section className="approval-step" aria-labelledby="step-terms">
            <div className="approval-step__head">
              <h2 className="n-title" id="step-terms">2. Assine os termos de uso</h2>
              <StepStatus done={stepDone("terms")} />
            </div>
            <p>O texto final dos termos ainda não foi definido. Na versão real, ele aparece aqui e cada aceite fica registrado com a versão assinada.</p>
            <Checkbox checked={session.termsAccepted} onChange={(event) => update({ termsAccepted: event.target.checked })}>
              Li e aceito os termos de uso.
            </Checkbox>
          </section>

          <section className="approval-step" aria-labelledby="step-legal">
            <div className="approval-step__head">
              <h2 className="n-title" id="step-legal">3. Envie as informações jurídicas</h2>
              <StepStatus done={stepDone("legal")} />
            </div>
            <div className="approval-step__fields">
              <TextField
                id="legal-document"
                label={isCompany ? "CNPJ" : "CPF"}
                className="n-figure"
                inputMode="numeric"
                autoComplete="off"
                maxLength={isCompany ? 18 : 14}
                value={session.legalDocument}
                error={documentError}
                onChange={(event) => {
                  const legalDocument = formatDocument(session.producerType, event.target.value);
                  update({ legalDocument });
                  if (isValidDocument(session.producerType, legalDocument)) setDocumentError(undefined);
                }}
                onBlur={() => {
                  if (session.legalDocument && !isValidDocument(session.producerType, session.legalDocument)) {
                    setDocumentError(`Confira o ${isCompany ? "CNPJ" : "CPF"} informado.`);
                  }
                }}
              />
              <TextField
                id="legal-name"
                label={isCompany ? "Razão social" : "Nome completo"}
                autoComplete={isCompany ? "organization" : "name"}
                maxLength={150}
                value={session.legalName}
                onChange={(event) => update({ legalName: event.target.value })}
              />
            </div>
          </section>
        </fieldset>

        <aside className="approval__summary" data-scene="palco">
          <Ticket stub={stub} torn={submitted}>
            {submitted ? (
              <div className="approval-done">
                <Stamp>Em aprovação</Stamp>
                <p>A equipe Ntix vai analisar seu cadastro. Com a aprovação, sua produtora fica ativa e as cortinas abrem.</p>
                <p className="signup__note">Demonstração: a integração com a API e o backoffice ainda não existem, então nada foi enviado e nenhuma pessoa vai aprovar de verdade.</p>
                <Button variant="ghost" block onClick={simulateApproval}>Simular aprovação da Ntix</Button>
              </div>
            ) : (
              <>
                <ul className="approval-check">
                  {steps.map((step) => (
                    <li key={step.id} className={step.done ? "is-done" : undefined}>
                      <Icon name={step.done ? "check" : "lock"} /> {step.title}
                    </li>
                  ))}
                </ul>
                <Button block arrow aria-busy={submitting} onClick={handleSubmit}>
                  {submitting ? "Enviando..." : "Enviar para aprovação"}
                </Button>
                <p className="approval-missing" ref={summaryRef} tabIndex={-1} role="status">
                  {missing.length > 0 && `Ainda falta: ${missing.join(", ").toLowerCase()}.`}
                </p>
              </>
            )}
          </Ticket>
        </aside>
      </div>
    </>
  );
}
