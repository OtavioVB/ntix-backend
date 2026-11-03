import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button, ButtonLink, Segmented, Stamp, TextField, Ticket, Wordmark } from "../../../design-system";
import { useProducerSession, type ProducerType } from "../../../producer/session";
import {
  ADMIN_FIELDS,
  PRODUCER_FIELDS,
  formatPhone,
  submitProducerSignup,
  validateField,
  validateFields,
  type SignupErrors,
  type SignupField,
  type SignupValues,
} from "../signup";
import { panelPath } from "../../../producer/paths";
import type { ApiMode } from "../../../api/mode";

const PRODUCER_TYPES = [
  { value: "COMPANY", label: "Empresa (PJ)" },
  { value: "INDIVIDUAL", label: "Pessoa física" },
] as const;

type Step = "producer" | "admin" | "submitting" | "done";

type SignupTicketProps = {
  /** Nome digitado no teste de marca: quem já disse o nome não digita de novo. */
  suggestedName: string;
  onComplete: () => void;
};

export function SignupTicket({ suggestedName, onComplete }: SignupTicketProps) {
  const { update } = useProducerSession();
  const [step, setStep] = useState<Step>("producer");
  const [producerType, setProducerType] = useState<ProducerType>("COMPANY");
  const [typedName, setTypedName] = useState<string>();
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [adminName, setAdminName] = useState("");
  const [adminPassword, setAdminPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [errors, setErrors] = useState<SignupErrors>({});
  const [formError, setFormError] = useState<string>();
  const [createdMode, setCreatedMode] = useState<ApiMode>("demo");
  const formRef = useRef<HTMLFormElement>(null);
  const doneRef = useRef<HTMLDivElement>(null);

  const name = typedName ?? suggestedName;
  const values: SignupValues = { producerType, name, email, phone, adminName, adminPassword };
  const isCompany = producerType === "COMPANY";

  useEffect(() => {
    if (step === "done") doneRef.current?.focus();
    if (step === "admin") document.getElementById(adminName ? "admin-password" : "admin-name")?.focus();
    // Só na troca de etapa: o foco não pode pular enquanto a pessoa digita.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [step]);

  function revalidate(field: SignupField, next: Partial<SignupValues>) {
    setErrors((current) => (current[field] ? { ...current, [field]: validateField(field, { ...values, ...next }) } : current));
  }

  function validateOnBlur(field: SignupField) {
    if (values[field]) setErrors((current) => ({ ...current, [field]: validateField(field, values) }));
  }

  function checkFields(fields: readonly SignupField[]): boolean {
    const found = validateFields(fields, values);
    setErrors(found);
    const firstInvalid = fields.find((field) => found[field]);
    if (firstInvalid) (formRef.current?.elements.namedItem(firstInvalid) as HTMLInputElement | null)?.focus();
    return !firstInvalid;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (step === "producer") {
      if (!checkFields(PRODUCER_FIELDS)) return;
      if (!adminName && !isCompany) setAdminName(name.trim());
      setStep("admin");
      return;
    }
    if (step !== "admin" || !checkFields(ADMIN_FIELDS)) return;

    setStep("submitting");
    setFormError(undefined);
    const result = await submitProducerSignup(values);

    if (result.status === "rejected") {
      setErrors(result.errors);
      setFormError(result.message);
      // Erro de um dado da produtora: volta ao passo em que o campo está.
      const producerError = PRODUCER_FIELDS.find((field) => result.errors[field]);
      setStep(producerError ? "producer" : "admin");
      return;
    }

    setCreatedMode(result.mode);
    // Na API real o administrador nasce PENDING e só acessa depois de verificar o e-mail: não há sessão a abrir.
    if (result.mode === "demo") {
      update({
        name: name.trim(),
        producerType,
        status: "PENDING",
        loggedIn: true,
        adminEmail: email.trim(),
        team: [{ id: "admin", name: adminName.trim(), email: email.trim(), isAdmin: true, invited: false }],
      });
    }
    setAdminPassword("");
    setStep("done");
    onComplete();
  }

  const onProducerStep = step === "producer";
  const stub = (
    <>
      <div className="ticket-stub">
        <div>
          <p className="n-label n-label--accent">Credencial de produtor</p>
          <h2 className="ticket-stub__title">{onProducerStep ? "Comece seu cadastro" : step === "done" ? "Cadastro feito" : "Quem vai administrar"}</h2>
        </div>
        <Wordmark className="ticket-stub__mark" decorative />
      </div>
      <p className="n-label ticket-stub__meta">{step === "done" ? "Bem-vindo à Ntix" : `Passo ${onProducerStep ? 1 : 2} de 2 · Sem cartão`}</p>
    </>
  );

  return (
    <Ticket stub={stub} torn={step === "done"}>
      {step === "done" ? (
        createdMode === "real" ? (
          <div className="signup-done" ref={doneRef} tabIndex={-1}>
            <Stamp>Cadastro iniciado</Stamp>
            <p className="n-title signup-done__title">Tudo certo, {name.trim()}. Falta só confirmar o seu e-mail.</p>
            <p className="signup__intro">Enviamos um link de verificação para {email.trim()}. Depois de confirmar, entre com esse e-mail e a senha que você criou.</p>
          </div>
        ) : (
          <div className="signup-done" ref={doneRef} tabIndex={-1}>
            <Stamp>Cadastro iniciado</Stamp>
            <p className="n-title signup-done__title">Tudo certo, {name.trim()}. Sua plataforma já está aberta para ensaio.</p>
            <ButtonLink to={panelPath()} state={{ openCurtain: true }} size="lg" block arrow>Entrar na plataforma</ButtonLink>
            <p className="signup__note">Demonstração: nenhum dado foi enviado para a API. Para usar a API de verdade, abra o cadastro com ?api=real.</p>
          </div>
        )
      ) : (
        <form className="signup" ref={formRef} onSubmit={handleSubmit} noValidate>
          {onProducerStep ? (
            <>
              <Segmented
                name="producerType"
                legend="Quem vai produzir os eventos?"
                options={PRODUCER_TYPES}
                value={producerType}
                onChange={(next) => {
                  setProducerType(next);
                  revalidate("name", { producerType: next });
                }}
              />
              <TextField
                id="signup-name"
                name="name"
                label={isCompany ? "Nome da produtora" : "Seu nome"}
                type="text"
                autoComplete={isCompany ? "organization" : "name"}
                required
                maxLength={120}
                value={name}
                error={errors.name}
                onChange={(event) => {
                  setTypedName(event.target.value);
                  revalidate("name", { name: event.target.value });
                }}
                onBlur={() => validateOnBlur("name")}
              />
              <TextField
                id="signup-email"
                name="email"
                label="E-mail da produtora"
                type="email"
                inputMode="email"
                autoComplete="email"
                required
                maxLength={254}
                placeholder="voce@suaprodutora.com"
                value={email}
                error={errors.email}
                onChange={(event) => {
                  setEmail(event.target.value);
                  revalidate("email", { email: event.target.value });
                }}
                onBlur={() => validateOnBlur("email")}
              />
              <TextField
                id="signup-phone"
                name="phone"
                label="Telefone"
                className="n-figure"
                type="tel"
                inputMode="tel"
                autoComplete="tel-national"
                required
                maxLength={15}
                placeholder="(11) 98765-4321"
                value={phone}
                error={errors.phone}
                onChange={(event) => {
                  const formatted = formatPhone(event.target.value);
                  setPhone(formatted);
                  revalidate("phone", { phone: formatted });
                }}
                onBlur={() => validateOnBlur("phone")}
              />
              <Button type="submit" size="lg" block arrow>Continuar</Button>
            </>
          ) : (
            <>
              <p className="signup__intro">É com este acesso que você vai entrar na plataforma todos os dias.</p>
              <TextField
                id="admin-name"
                name="adminName"
                label="Nome do administrador"
                type="text"
                autoComplete="name"
                required
                maxLength={120}
                value={adminName}
                error={errors.adminName}
                onChange={(event) => {
                  setAdminName(event.target.value);
                  revalidate("adminName", { adminName: event.target.value });
                }}
                onBlur={() => validateOnBlur("adminName")}
              />
              <div className="password-field">
                <TextField
                  id="admin-password"
                  name="adminPassword"
                  label="Senha"
                  type={showPassword ? "text" : "password"}
                  autoComplete="new-password"
                  required
                  value={adminPassword}
                  error={errors.adminPassword}
                  onChange={(event) => {
                    setAdminPassword(event.target.value);
                    revalidate("adminPassword", { adminPassword: event.target.value });
                  }}
                  onBlur={() => validateOnBlur("adminPassword")}
                />
                <button type="button" className="password-field__toggle" aria-pressed={showPassword} onClick={() => setShowPassword((shown) => !shown)}>
                  {showPassword ? "Ocultar" : "Mostrar"}
                </button>
              </div>
              <Button type="submit" size="lg" block arrow aria-busy={step === "submitting"}>
                {step === "submitting" ? "Criando cadastro..." : "Criar meu cadastro"}
              </Button>
              <button type="button" className="signup__back" onClick={() => setStep("producer")}>Voltar para os dados da produtora</button>
            </>
          )}
          {formError && <p className="signup__error" role="alert">{formError}</p>}
          <p className="signup__note">Contrato e termos de uso só entram na etapa de contratação, depois que você conhecer a plataforma.</p>
        </form>
      )}
    </Ticket>
  );
}
