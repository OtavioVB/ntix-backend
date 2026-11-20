import { useState, type FormEvent } from "react";
import { Button, Segmented, TextField } from "../../design-system";
import { demoAccountState, useCustomer } from "../../customer/session";
import { formatDocument, isValidDocument } from "../../producer/legal";
import { isRealApi } from "../../api/mode";
import { signInCustomer, signUpCustomer } from "../../customer/signIn";

const MODES = [
  { value: "signup", label: "Criar conta" },
  { value: "login", label: "Já tenho conta" },
] as const;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

type Errors = { name?: string; email?: string; document?: string; password?: string; login?: string };

/** Conta do comprador: e-mail ou CPF e senha (P-40). A senha não fica guardada; sem API, nem é verificada. */
export function AuthForm({ onDone }: { onDone?: () => void }) {
  const { customer, update } = useCustomer();
  const [mode, setMode] = useState<"signup" | "login">("signup");
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [document, setDocument] = useState("");
  const [password, setPassword] = useState("");
  const [identifier, setIdentifier] = useState("");
  const [errors, setErrors] = useState<Errors>({});

  const real = isRealApi();
  const [sending, setSending] = useState(false);

  async function handleSignup(event: FormEvent) {
    event.preventDefault();
    const found: Errors = {};
    if (name.trim().length < 2) found.name = "Informe seu nome.";
    if (!EMAIL_PATTERN.test(email.trim())) found.email = "Informe um e-mail válido.";
    if (!isValidDocument("INDIVIDUAL", document)) found.document = "Confira o CPF informado.";
    if (password.length < 8) found.password = "A senha precisa ter pelo menos 8 caracteres.";
    setErrors(found);
    if (Object.keys(found).length) return;

    if (real) {
      setSending(true);
      const result = await signUpCustomer(name, email, document, password);
      setSending(false);
      if (!result.ok) {
        setErrors({ [result.field ?? "password"]: result.message });
        return;
      }
      update({ account: result.account, loggedIn: true });
    } else {
      update({ account: { name: name.trim(), email: email.trim(), document }, loggedIn: true });
    }
    setPassword("");
    onDone?.();
  }

  async function handleLogin(event: FormEvent) {
    event.preventDefault();

    // API real (REQ12): e-mail ou CPF e senha conferidos no servidor; a conta da tela vem de "meus dados".
    if (real) {
      if (!identifier.trim() || !password) {
        setErrors({ login: "Informe o e-mail ou CPF e a senha." });
        return;
      }
      setSending(true);
      const result = await signInCustomer(identifier, password);
      setSending(false);
      if (!result.ok) {
        setErrors({ login: result.message });
        return;
      }
      update({ account: result.account, loggedIn: true });
      setPassword("");
      onDone?.();
      return;
    }

    const typed = identifier.trim().toLowerCase();
    const account = customer.account;
    const matches = account && (account.email.toLowerCase() === typed || account.document.replace(/\D/g, "") === typed.replace(/\D/g, ""));
    if (!matches || !password) {
      setErrors({ login: "E-mail, CPF ou senha incorretos." });
      return;
    }
    update({ loggedIn: true });
    setPassword("");
    onDone?.();
  }

  function useDemoAccount() {
    const demo = demoAccountState(Date.now());
    update({ ...demo, orders: [...customer.orders, ...(demo.orders ?? [])] });
    onDone?.();
  }

  return (
    <div className="auth-form">
      <Segmented name="auth-mode" legend="Como você quer continuar?" options={MODES} value={mode} onChange={(next) => { setMode(next); setErrors({}); }} />
      {mode === "signup" ? (
        <form className="auth-form__fields" onSubmit={handleSignup} noValidate>
          <TextField id="customer-name" label="Nome completo" autoComplete="name" maxLength={120} value={name} error={errors.name} onChange={(event) => setName(event.target.value)} />
          <TextField id="customer-email" label="E-mail" type="email" inputMode="email" autoComplete="email" maxLength={254} value={email} error={errors.email} onChange={(event) => setEmail(event.target.value)} />
          <TextField id="customer-document" label="CPF" inputMode="numeric" className="n-figure" autoComplete="off" maxLength={14} value={document} error={errors.document} onChange={(event) => setDocument(formatDocument("INDIVIDUAL", event.target.value))} />
          <TextField id="customer-password" label="Senha" type="password" autoComplete="new-password" value={password} error={errors.password} onChange={(event) => setPassword(event.target.value)} />
          <Button type="submit" block arrow disabled={sending}>Criar conta e continuar</Button>
        </form>
      ) : (
        <form className="auth-form__fields" onSubmit={handleLogin} noValidate>
          <TextField id="customer-identifier" label="E-mail ou CPF" autoComplete="username" value={identifier} onChange={(event) => setIdentifier(event.target.value)} />
          <TextField id="customer-login-password" label="Senha" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} />
          <p className="auth-form__error" role="alert">{errors.login}</p>
          <Button type="submit" block arrow disabled={sending}>Entrar</Button>
        </form>
      )}
      <p className="signup__note">
        Sua conta vale em todo o hub e nas ticketerias dos produtores. Demonstração: sem API, a senha não é verificada de verdade.{" "}
        <button type="button" className="auth-form__demo" onClick={useDemoAccount}>Entrar com a conta de demonstração</button>
      </p>
    </div>
  );
}
