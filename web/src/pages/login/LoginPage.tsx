import "./login.css";

import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { Button, Scene, TextField, ThemeToggle, Wordmark } from "../../design-system";
import { isRealApi } from "../../api/mode";
import { useProducerSession } from "../../producer/session";
import { panelPath, signupPath } from "../../producer/paths";
import { signInProducer, type SignInErrors } from "./signIn";

/**
 * Acesso de usuários do produtor (P-39). Com `?api=real`, entra pela API (REQ5); em demonstração, confere só se o
 * e-mail é de alguém da equipe deste navegador.
 */
export function LoginPage() {
  const { session, update, reset } = useProducerSession();
  const navigate = useNavigate();
  const real = isRealApi();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<SignInErrors>({});
  const [submitting, setSubmitting] = useState(false);

  if (session.loggedIn) return <Navigate to={panelPath()} replace />;

  const hasDemoAccount = Boolean(session.adminEmail);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) return;

    if (!real) {
      const typed = email.trim().toLowerCase();
      const member = session.team.find((candidate) => candidate.email.toLowerCase() === typed);
      if (!member || !password) {
        setErrors({ general: "E-mail ou senha incorretos." });
        return;
      }
      update({ loggedIn: true });
      navigate(panelPath());
      return;
    }

    setSubmitting(true);
    const result = await signInProducer(email, password);
    setSubmitting(false);

    if (result.status === "rejected") {
      setErrors(result.errors);
      return;
    }

    // A senha não fica em lugar nenhum: some do formulário assim que a sessão abre.
    setPassword("");
    update(result.session);
    navigate(panelPath());
  }

  return (
    <Scene light="palco" as="main" className="login">
      <title>Entrar · Ntix</title>
      <div className="login__card">
        <div className="login__top">
          <Wordmark href={signupPath()} label="Ntix, página inicial" />
          <ThemeToggle />
        </div>
        <h1 className="n-display n-display--sm">Bem-vindo de volta.</h1>
        <form className="login__form" onSubmit={handleSubmit} noValidate aria-busy={submitting || undefined}>
          <TextField id="login-email" label="E-mail" type="email" inputMode="email" autoComplete="username" value={email} error={errors.email} onChange={(event) => setEmail(event.target.value)} />
          <TextField id="login-password" label="Senha" type="password" autoComplete="current-password" value={password} error={errors.password} onChange={(event) => setPassword(event.target.value)} />
          <p className="login__error" role="alert">{errors.general}</p>
          <Button type="submit" size="lg" block arrow disabled={submitting}>{submitting ? "Entrando…" : "Entrar"}</Button>
        </form>
        <p className="login__alt">Ainda não vende com a Ntix? <Link to={signupPath()}>Crie seu cadastro</Link></p>
        {!real && (
          <p className="signup__note">
            Demonstração: sem API, a senha não é verificada de verdade.
            {hasDemoAccount && (
              <>
                {" "}Conta deste navegador: {session.adminEmail}.{" "}
                <button type="button" className="login__reset" onClick={reset}>Apagar demonstração</button>
              </>
            )}
          </p>
        )}
      </div>
    </Scene>
  );
}
