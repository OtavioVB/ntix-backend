import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { DEFAULT_STORE_LOOK, type StoreLook } from "../features/store/StoreBuilder";
import { clearSessionTokens, readRefreshToken, subscribeAuth } from "../api/auth";
import { isRealApi } from "../api/mode";
import type { PaymentMethod, ProducerIntegration } from "../api/payments";
import type { ProducerSessionResponse } from "../api/sessions";
import type { PlatformData } from "../platform/types";
import { isValidDocument } from "./legal";

export type ProducerType = "COMPANY" | "INDIVIDUAL";
export type ProducerStatus = "PENDING" | "PENDING_APPROVAL" | "ACTIVE";

/** NONE: não contratada. SELECTED: incluída na contratação em andamento. ACTIVE: no ar. */
export type TicketeriaStatus = "NONE" | "SELECTED" | "ACTIVE";

export type TeamMember = { id: string; name: string; email: string; isAdmin: boolean; invited: boolean };

/** Nunca guarda senha: ela segue para a API no cadastro e não fica em lugar nenhum do navegador. */
export type ProducerSession = {
  name: string;
  producerType: ProducerType;
  status: ProducerStatus;
  loggedIn: boolean;
  adminEmail: string;
  team: TeamMember[];
  ticketeria: TicketeriaStatus;
  contractAccepted: boolean;
  termsAccepted: boolean;
  legalDocument: string;
  legalName: string;
  storeLook: StoreLook;
  platform: PlatformData | null;
  /** Slug da produtora na API (`/api/v1/producers/{slug}`), preenchido pelo login e pela renovação (REQ5). */
  slug: string;
  /** Integrações de pagamento da demonstração (REQ4). Credencial secreta guardada só pelo final, como a API devolve. */
  paymentIntegrations: ProducerIntegration[];
  /** Rotas da demonstração: por método, os ids das integrações em ordem de prioridade. */
  paymentRoutes: Record<PaymentMethod, string[]>;
};

const INITIAL_SESSION: ProducerSession = {
  name: "",
  producerType: "COMPANY",
  status: "PENDING",
  loggedIn: false,
  adminEmail: "",
  team: [],
  ticketeria: "NONE",
  contractAccepted: false,
  termsAccepted: false,
  legalDocument: "",
  legalName: "",
  storeLook: DEFAULT_STORE_LOOK,
  platform: null,
  slug: "",
  paymentIntegrations: [],
  paymentRoutes: { PIX: [], BOLETO: [], CREDIT_CARD: [] },
};

export type ApprovalStep = { id: "contract" | "terms" | "legal"; title: string; done: boolean };

/** Premissa P-12: o que leva o produtor de PENDING para PENDING_APPROVAL. */
export function approvalSteps(session: ProducerSession): ApprovalStep[] {
  return [
    { id: "contract", title: "Contratar o serviço", done: session.contractAccepted },
    { id: "terms", title: "Assinar os termos de uso", done: session.termsAccepted },
    {
      id: "legal",
      title: "Enviar as informações jurídicas",
      done: isValidDocument(session.producerType, session.legalDocument) && session.legalName.trim().length >= 2,
    },
  ];
}

/** Integração pendente: ainda não existem endpoints de produtor. Quando existirem, cadastro, login
    e envio para aprovação vão para a API com os headers de docs/rules/auditable-info.md. Por ora a
    sessão fica neste navegador, para a demonstração sobreviver a um recarregamento. */
export function submitForApproval(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 650));
}

const STORAGE_KEY = "ntix.demo.session.v1";

function loadStored(): ProducerSession | null {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return stored ? { ...INITIAL_SESSION, ...(JSON.parse(stored) as Partial<ProducerSession>) } : null;
  } catch {
    return null;
  }
}

// Os tokens da API real não entram aqui: ficam em api/auth.ts (access token em memória, refresh token à parte).
function store(session: ProducerSession) {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  } catch {
    // Sem armazenamento a demonstração segue funcionando, só não sobrevive a um recarregamento.
  }
}

type SessionContext = {
  session: ProducerSession;
  update: (changes: Partial<ProducerSession>) => void;
  /** Apaga a demonstração deste navegador. */
  reset: () => void;
};

const Context = createContext<SessionContext | null>(null);

type ProviderProps = {
  children: ReactNode;
  /** Sessão fixa, sem ler nem gravar no navegador. Para testes. */
  initial?: Partial<ProducerSession>;
};

/**
 * Sessão aberta pelo login real (REQ5): a produtora e o administrador vêm da API, não da demonstração. O e-mail é o
 * digitado no login, porque a resposta não o devolve.
 */
export function sessionFromLogin(response: ProducerSessionResponse, email: string): Partial<ProducerSession> {
  return {
    ...sessionFromRefresh(response),
    loggedIn: true,
    adminEmail: email,
    team: [{ id: response.user.id, name: response.user.name, email, isAdmin: response.user.userType === "ADMINISTRATOR", invited: false }],
  };
}

/** A renovação traz o slug e o status atuais da produtora (RN8). */
function sessionFromRefresh(response: ProducerSessionResponse): Partial<ProducerSession> {
  return { name: response.producer.name, slug: response.producer.slug, status: response.producer.status };
}

/** No modo real, sem refresh token guardado não há como reabrir a sessão: o painel volta para o login. */
function withoutOrphanLogin(session: ProducerSession): ProducerSession {
  return session.loggedIn && isRealApi() && !readRefreshToken() ? { ...session, loggedIn: false } : session;
}

export function ProducerSessionProvider({ children, initial }: ProviderProps) {
  const [session, setSession] = useState<ProducerSession>(() => (initial ? { ...INITIAL_SESSION, ...initial } : withoutOrphanLogin(loadStored() ?? INITIAL_SESSION)));

  useEffect(() => {
    if (!initial) store(session);
  }, [session, initial]);

  useEffect(
    () =>
      subscribeAuth((event) =>
        setSession((current) => (event.type === "expired" ? { ...current, loggedIn: false } : { ...current, ...sessionFromRefresh(event.session) })),
      ),
    [],
  );

  const value = useMemo<SessionContext>(
    () => ({
      session,
      update: (changes) => setSession((current) => ({ ...current, ...changes })),
      reset: () => {
        try {
          window.localStorage.removeItem(STORAGE_KEY);
        } catch {
          // Nada a apagar.
        }
        clearSessionTokens();
        setSession(INITIAL_SESSION);
      },
    }),
    [session],
  );
  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useProducerSession(): SessionContext {
  const context = useContext(Context);
  if (!context) throw new Error("useProducerSession precisa de um ProducerSessionProvider");
  return context;
}
