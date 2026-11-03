import "./login.css";

import { useState, type FormEvent } from "react";
import { Link, Navigate, useNavigate } from "react-router-dom";
import { Button, Scene, TextField, ThemeToggle, Wordmark } from "../../design-system";
import { useProducerSession } from "../../producer/session";

/** Acesso de usuários do produtor (P-39). Autenticação pendente: sem API, a senha não é verificada. */
export function LoginPage() {
  const { session, update, reset } = useProducerSession();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string>();

  if (session.loggedIn) return <Navigate to="/painel" replace />;

  const hasDemoAccount = Boolean(session.adminEmail);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const typed = email.trim().toLowerCase();
    const member = session.team.find((candidate) => candidate.email.toLowerCase() === typed);
    if (!member || !password) {
      setError("E-mail ou senha incorretos.");
      return;
    }
    update({ loggedIn: true });
    navigate("/painel");
  }

  return (
    <Scene light="palco" as="main" className="login">
      <title>Entrar · Ntix</title>
      <div className="login__card">
        <div className="login__top">
          <Wordmark href="/produtores" label="Ntix, página inicial" />
          <ThemeToggle />
        </div>
        <h1 className="n-display n-display--sm">Bem-vindo de volta.</h1>
        <form className="login__form" onSubmit={handleSubmit} noValidate>
          <TextField id="login-email" label="E-mail" type="email" inputMode="email" autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} />
          <TextField id="login-password" label="Senha" type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} />
          <p className="login__error" role="alert">{error}</p>
          <Button type="submit" size="lg" block arrow>Entrar</Button>
        </form>
        <p className="login__alt">Ainda não vende com a Ntix? <Link to="/produtores">Crie seu cadastro</Link></p>
        <p className="signup__note">
          Demonstração: sem API, a senha não é verificada de verdade.
          {hasDemoAccount && (
            <>
              {" "}Conta deste navegador: {session.adminEmail}.{" "}
              <button type="button" className="login__reset" onClick={reset}>Apagar demonstração</button>
            </>
          )}
        </p>
      </div>
    </Scene>
  );
}
