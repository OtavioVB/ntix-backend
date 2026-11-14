import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { DEFAULT_STORE_LOOK, type StoreLook } from "../features/store/StoreBuilder";
import type { PaymentMethod, ProducerIntegration } from "../api/payments";
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
  /** Slug da produtora na API (`/api/v1/producers/{slug}`), preenchido pelo login (REQ5). */
  slug: string;
  /**
   * Access token do login (REQ5). Fica só em memória: nunca vai para o localStorage, nem na demonstração.
   * Enquanto o login real não existir, o modo `?api=real` do painel recebe 401 e pede para entrar de novo.
   */
  accessToken?: string;
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

function store(session: ProducerSession) {
  try {
    // O access token não sobrevive ao recarregamento: fica fora do que é gravado no navegador.
    const persisted: Partial<ProducerSession> = { ...session };
    delete persisted.accessToken;
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(persisted));
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

export function ProducerSessionProvider({ children, initial }: ProviderProps) {
  const [session, setSession] = useState<ProducerSession>(() => (initial ? { ...INITIAL_SESSION, ...initial } : (loadStored() ?? INITIAL_SESSION)));

  useEffect(() => {
    if (!initial) store(session);
  }, [session, initial]);

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
