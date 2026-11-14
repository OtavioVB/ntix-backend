import { useEffect, useState, type FormEvent } from "react";
import { Badge, Button, TextField } from "../../design-system";
import { isRealApi } from "../../api/mode";
import {
  createPaymentIntegration,
  getPaymentIntegrations,
  getPaymentProviders,
  getPaymentRoutes,
  PAYMENT_METHODS,
  replacePaymentRoute,
  updatePaymentIntegration,
  type IntegrationStatus,
  type PaymentMethod,
  type PaymentProvider,
  type ProducerIntegration,
} from "../../api/payments";
import { useProducerSession } from "../../producer/session";
import {
  buildDemoIntegration,
  CREDENTIAL_LABELS,
  DEMO_PROVIDERS,
  filledCredentials,
  hasErrors,
  integrationsForMethod,
  mapIntegrationNotifications,
  METHOD_LABELS,
  move,
  normalizeName,
  validateIntegration,
  type IntegrationForm,
  type IntegrationFormErrors,
} from "./integrations";

type Routes = Record<PaymentMethod, string[]>;

const EMPTY_ROUTES: Routes = { PIX: [], BOLETO: [], CREDIT_CARD: [] };
const NO_ERRORS: IntegrationFormErrors = { credentials: {} };

/**
 * Integrações de pagamento (REQ4): em demonstração, tudo fica neste navegador; com `?api=real`, a tela chama a API
 * com o access token da sessão (REQ5). Nos dois modos, credencial secreta só aparece pelo final.
 */
function usePaymentIntegrations() {
  const { session, update } = useProducerSession();
  const real = isRealApi();
  const [providers, setProviders] = useState<PaymentProvider[]>(real ? [] : DEMO_PROVIDERS);
  const [remote, setRemote] = useState<{ integrations: ProducerIntegration[]; routes: Routes }>({ integrations: [], routes: EMPTY_ROUTES });
  const [loadError, setLoadError] = useState<string>();

  useEffect(() => {
    if (!real) return;
    const controller = new AbortController();

    Promise.all([
      getPaymentProviders(controller.signal),
      getPaymentIntegrations(session.slug, session.accessToken, controller.signal),
      getPaymentRoutes(session.slug, session.accessToken, controller.signal),
    ]).then(([providersResult, integrationsResult, routesResult]) => {
      if (controller.signal.aborted) return;
      const failed = [providersResult, integrationsResult, routesResult].find((result) => !result.ok);
      if (failed && !failed.ok) {
        setLoadError(failed.notifications[0]?.message);
        return;
      }
      if (providersResult.ok) setProviders(providersResult.data);
      if (integrationsResult.ok && routesResult.ok) {
        setRemote({
          integrations: integrationsResult.data,
          routes: Object.fromEntries(PAYMENT_METHODS.map((method) => [method, routesResult.data[method].map((item) => item.integrationId)])) as Routes,
        });
      }
    });

    return () => controller.abort();
  }, [real, session.slug, session.accessToken]);

  const integrations = real ? remote.integrations : session.paymentIntegrations;
  const routes = real ? remote.routes : session.paymentRoutes;

  async function create(form: IntegrationForm): Promise<IntegrationFormErrors> {
    const provider = providers.find((candidate) => candidate.code === form.providerCode);
    const errors = validateIntegration(form, provider, integrations);
    if (hasErrors(errors) || !provider) return errors;

    if (!real) {
      update({ paymentIntegrations: [...integrations, buildDemoIntegration(form, provider)] });
      return NO_ERRORS;
    }

    const result = await createPaymentIntegration(session.slug, { providerCode: provider.code, name: normalizeName(form.name), credentials: filledCredentials(form, provider) }, session.accessToken);
    if (!result.ok) return mapIntegrationNotifications(result.notifications);
    setRemote((current) => ({ ...current, integrations: [...current.integrations, result.data] }));
    return NO_ERRORS;
  }

  async function setStatus(integration: ProducerIntegration, status: IntegrationStatus): Promise<string | undefined> {
    if (!real) {
      update({ paymentIntegrations: integrations.map((item) => (item.id === integration.id ? { ...item, status } : item)) });
      return undefined;
    }

    const result = await updatePaymentIntegration(session.slug, integration.id, { status }, session.accessToken);
    if (!result.ok) return result.notifications[0]?.message;
    setRemote((current) => ({ ...current, integrations: current.integrations.map((item) => (item.id === integration.id ? result.data : item)) }));
    return undefined;
  }

  async function saveRoute(method: PaymentMethod, integrationIds: string[]): Promise<string | undefined> {
    if (!real) {
      update({ paymentRoutes: { ...routes, [method]: integrationIds } });
      return undefined;
    }

    const result = await replacePaymentRoute(session.slug, method, { integrationIds }, session.accessToken);
    if (!result.ok) return result.notifications[0]?.message;
    setRemote((current) => ({ ...current, routes: { ...current.routes, [method]: result.data.map((item) => item.integrationId) } }));
    return undefined;
  }

  return { real, providers, integrations, routes, loadError, create, setStatus, saveRoute };
}

function ConnectForm({ providers, onCreate }: { providers: PaymentProvider[]; onCreate: (form: IntegrationForm) => Promise<IntegrationFormErrors> }) {
  const [providerCode, setProviderCode] = useState(providers[0]?.code ?? "");
  const [name, setName] = useState("");
  const [credentials, setCredentials] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<IntegrationFormErrors>(NO_ERRORS);
  const [created, setCreated] = useState<string>();
  const [sending, setSending] = useState(false);

  useEffect(() => {
    if (!providerCode && providers[0]) setProviderCode(providers[0].code);
  }, [providers, providerCode]);

  const provider = providers.find((candidate) => candidate.code === providerCode);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setSending(true);
    const found = await onCreate({ providerCode, name, credentials });
    setSending(false);
    setErrors(found);
    if (hasErrors(found)) return;
    setCreated(normalizeName(name));
    setName("");
    // A credencial digitada sai da memória da tela assim que a integração é criada.
    setCredentials({});
  }

  return (
    <form className="team-form" onSubmit={handleSubmit} noValidate aria-labelledby="integration-add-title">
      <div className="n-field">
        <label className="n-field__label" htmlFor="integration-provider">Fornecedor</label>
        <select id="integration-provider" className="n-input" value={providerCode} onChange={(event) => setProviderCode(event.target.value)}>
          {providers.map((option) => <option key={option.code} value={option.code}>{option.name}</option>)}
        </select>
      </div>
      <TextField id="integration-name" label="Nome da integração" placeholder="AbacatePay - conta principal" autoComplete="off" maxLength={64} value={name} error={errors.name} onChange={(event) => setName(event.target.value)} />
      {provider?.credentialKeys.map((key) => (
        <TextField
          key={key.key}
          id={`integration-credential-${key.key}`}
          label={`${CREDENTIAL_LABELS[key.key] ?? key.key}${key.isRequired ? "" : " (opcional)"}`}
          type={key.isSecret ? "password" : "text"}
          autoComplete="off"
          spellCheck={false}
          maxLength={2048}
          value={credentials[key.key] ?? ""}
          error={errors.credentials[key.key]}
          onChange={(event) => setCredentials((current) => ({ ...current, [key.key]: event.target.value }))}
        />
      ))}
      {errors.general && <p className="n-field__error" role="alert">{errors.general}</p>}
      <Button type="submit" block disabled={sending}>Conectar fornecedor</Button>
      <p className="team-form__status" role="status">{created && `${created} foi conectada e já pode entrar nas rotas.`}</p>
    </form>
  );
}

function RouteEditor({ method, integrationIds, candidates, integrations, onSave }: {
  method: PaymentMethod;
  integrationIds: string[];
  candidates: ProducerIntegration[];
  integrations: ProducerIntegration[];
  onSave: (method: PaymentMethod, integrationIds: string[]) => Promise<string | undefined>;
}) {
  const [toAdd, setToAdd] = useState("");
  const [error, setError] = useState<string>();
  const label = METHOD_LABELS[method];
  const available = candidates.filter((integration) => !integrationIds.includes(integration.id));
  const titleId = `route-${method}-title`;

  async function save(next: string[]) {
    setError(await onSave(method, next));
  }

  return (
    <section className="tile" aria-labelledby={titleId}>
      <h3 className="tile__title" id={titleId}>{label}</h3>
      {integrationIds.length === 0 ? (
        <p className="tile__note">Não configurado: o {label.toLowerCase()} não aparece no checkout.</p>
      ) : (
        <ol className="team-list" aria-label={`Prioridades do ${label.toLowerCase()}`}>
          {integrationIds.map((id, index) => {
            const integration = integrations.find((item) => item.id === id);
            const integrationName = integration?.name ?? "Integração removida";
            return (
              <li key={id}>
                <span className="team-list__avatar" aria-hidden="true">{index + 1}</span>
                <div className="team-list__who">
                  <strong>{integrationName}</strong>
                  <span>{index === 0 ? "Principal" : "Usada se as anteriores estiverem indisponíveis"}</span>
                </div>
                {integration?.status === "INACTIVE" && <Badge>Inativa</Badge>}
                <button type="button" className="team-list__remove" aria-label={`Subir ${integrationName} no ${label.toLowerCase()}`} disabled={index === 0} onClick={() => save(move(integrationIds, index, -1))}>Subir</button>
                <button type="button" className="team-list__remove" aria-label={`Descer ${integrationName} no ${label.toLowerCase()}`} disabled={index === integrationIds.length - 1} onClick={() => save(move(integrationIds, index, 1))}>Descer</button>
                <button type="button" className="team-list__remove" aria-label={`Tirar ${integrationName} do ${label.toLowerCase()}`} onClick={() => save(integrationIds.filter((other) => other !== id))}>Tirar</button>
              </li>
            );
          })}
        </ol>
      )}
      {available.length > 0 && (
        <div className="route-add">
          <label className="n-field__label" htmlFor={`route-${method}-add`}>Adicionar ao {label.toLowerCase()}</label>
          <select id={`route-${method}-add`} className="n-input" value={toAdd} onChange={(event) => setToAdd(event.target.value)}>
            <option value="">Escolha uma integração</option>
            {available.map((integration) => <option key={integration.id} value={integration.id}>{integration.name}</option>)}
          </select>
          <Button type="button" variant="ghost" disabled={!toAdd} onClick={() => { save([...integrationIds, toAdd]); setToAdd(""); }}>Adicionar</Button>
        </div>
      )}
      {error && <p className="n-field__error" role="alert">{error}</p>}
    </section>
  );
}

/** Aba Integrações do painel: fornecedores de pagamento da produtora e por qual conta cobrar cada método. */
export function IntegrationsPage() {
  const { real, providers, integrations, routes, loadError, create, setStatus, saveRoute } = usePaymentIntegrations();
  const [statusError, setStatusError] = useState<string>();

  return (
    <>
      <title>Integrações · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Integrações</h1>
        <p className="n-lead">Conecte as suas contas nos fornecedores de pagamento e escolha por qual cobrar cada método. O dinheiro dos ingressos cai direto na sua conta.</p>
      </header>

      {loadError && <p className="n-field__error" role="alert">{loadError}</p>}

      <div className="team">
        <section className="tile" aria-labelledby="integration-list-title">
          <h2 className="tile__title" id="integration-list-title">{integrations.length === 1 ? "1 integração" : `${integrations.length} integrações`}</h2>
          {integrations.length === 0 ? (
            <p className="tile__note">Nenhum fornecedor conectado ainda. Sem integração, nenhum método de pagamento aparece no checkout.</p>
          ) : (
            <ul className="team-list">
              {integrations.map((integration) => (
                <li key={integration.id}>
                  <div className="team-list__who">
                    <strong>{integration.name}</strong>
                    <span>
                      {providers.find((provider) => provider.code === integration.providerCode)?.name ?? integration.providerCode}
                      {integration.credentials.map((credential) => ` · ${CREDENTIAL_LABELS[credential.key] ?? credential.key} ${credential.isSecret ? `••••${credential.valueHint ?? ""}` : credential.value}`).join("")}
                    </span>
                  </div>
                  <Badge>{integration.status === "ACTIVE" ? "Ativa" : "Inativa"}</Badge>
                  <button
                    type="button"
                    className="team-list__remove"
                    aria-label={`${integration.status === "ACTIVE" ? "Inativar" : "Reativar"} ${integration.name}`}
                    onClick={async () => setStatusError(await setStatus(integration, integration.status === "ACTIVE" ? "INACTIVE" : "ACTIVE"))}
                  >
                    {integration.status === "ACTIVE" ? "Inativar" : "Reativar"}
                  </button>
                </li>
              ))}
            </ul>
          )}
          {statusError && <p className="n-field__error" role="alert">{statusError}</p>}
          <p className="tile__note">Integração inativa sai das cobranças novas, mas continua atendendo os pagamentos que já processou.</p>
        </section>

        <section className="tile" aria-labelledby="integration-add-title">
          <h2 className="tile__title" id="integration-add-title">Conectar fornecedor</h2>
          {providers.length > 0 && <ConnectForm providers={providers} onCreate={create} />}
          <p className="tile__note">
            {real
              ? "As credenciais são guardadas cifradas e nunca voltam inteiras para a tela."
              : "Demonstração: nada vai para a API, e a credencial secreta não é guardada, só o final dela."}
          </p>
        </section>
      </div>

      <section className="routes" aria-labelledby="routes-title">
        <h2 className="n-display n-display--sm" id="routes-title">Por qual integração cobrar</h2>
        <p className="n-lead">A primeira de cada método é a principal; as demais entram se ela estiver indisponível.</p>
        <div className="routes__grid">
          {PAYMENT_METHODS.map((method) => (
            <RouteEditor
              key={method}
              method={method}
              integrationIds={routes[method]}
              candidates={integrationsForMethod(method, integrations, providers)}
              integrations={integrations}
              onSave={saveRoute}
            />
          ))}
        </div>
      </section>
    </>
  );
}
