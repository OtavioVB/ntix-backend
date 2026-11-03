import { useState, type FormEvent } from "react";
import { Badge, Button, TextField } from "../../design-system";
import { useProducerSession } from "../../producer/session";

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

/** Equipe do produtor (P-15). Liberada desde o ensaio: é o único acesso que precisa existir já. */
export function TeamPage() {
  const { session, update } = useProducerSession();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [errors, setErrors] = useState<{ name?: string; email?: string }>({});
  const [lastInvited, setLastInvited] = useState<string>();

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const typedEmail = email.trim();
    const found: typeof errors = {};
    if (name.trim().length < 2) found.name = "Informe o nome da pessoa.";
    if (!EMAIL_PATTERN.test(typedEmail)) found.email = "Informe um e-mail válido.";
    else if (session.team.some((member) => member.email.toLowerCase() === typedEmail.toLowerCase())) found.email = "Esta pessoa já faz parte da equipe.";
    setErrors(found);
    if (found.name || found.email) return;

    const member = { id: `member-${Date.now()}`, name: name.trim(), email: typedEmail, isAdmin: false, invited: true };
    update({ team: [...session.team, member] });
    setLastInvited(member.name);
    setName("");
    setEmail("");
  }

  return (
    <>
      <title>Equipe · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Equipe</h1>
        <p className="n-lead">Quem acessa o painel da sua produtora, cada pessoa com o próprio e-mail e senha.</p>
      </header>

      <div className="team">
        <section className="tile" aria-labelledby="team-list-title">
          <h2 className="tile__title" id="team-list-title">{session.team.length === 1 ? "1 pessoa" : `${session.team.length} pessoas`} com acesso</h2>
          <ul className="team-list">
            {session.team.map((member) => (
              <li key={member.id}>
                <span className="team-list__avatar" aria-hidden="true">{member.name.charAt(0).toUpperCase()}</span>
                <div className="team-list__who">
                  <strong>{member.name}</strong>
                  <span>{member.email}</span>
                </div>
                {member.isAdmin && <Badge>Administrador</Badge>}
                {member.invited && <Badge>Convite enviado</Badge>}
                {!member.isAdmin && (
                  <button
                    type="button"
                    className="team-list__remove"
                    aria-label={`Remover ${member.name} da equipe`}
                    onClick={() => update({ team: session.team.filter((other) => other.id !== member.id) })}
                  >
                    Remover
                  </button>
                )}
              </li>
            ))}
          </ul>
        </section>

        <section className="tile" aria-labelledby="team-add-title">
          <h2 className="tile__title" id="team-add-title">Convidar alguém</h2>
          <form className="team-form" onSubmit={handleSubmit} noValidate>
            <TextField id="member-name" label="Nome" autoComplete="off" maxLength={120} value={name} error={errors.name} onChange={(event) => setName(event.target.value)} />
            <TextField id="member-email" label="E-mail" type="email" inputMode="email" autoComplete="off" maxLength={254} value={email} error={errors.email} onChange={(event) => setEmail(event.target.value)} />
            <Button type="submit" block>Enviar convite</Button>
            <p className="team-form__status" role="status">{lastInvited && `${lastInvited} foi adicionada à equipe.`}</p>
          </form>
          <p className="tile__note">Por enquanto todos têm as mesmas permissões, e a pessoa cria a própria senha pelo convite. Demonstração: sem API, nenhum convite sai de fato.</p>
        </section>
      </div>
    </>
  );
}
