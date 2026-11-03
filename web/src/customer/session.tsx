import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { DEFAULT_LOOKS, normalizeAvatar, type AvatarConfig } from "../features/avatar/avatarOptions";
import { demoOrders, type CustomerOrder } from "./orders";

/** Conta única do comprador (P-32). Sem senha: ela iria para a API e não fica no navegador. */
export type CustomerAccount = { name: string; email: string; document: string };

export type CartState = { eventId: string; slug: string; selection: Record<string, number>; expiresAt: number };

export type CustomerState = {
  account: CustomerAccount | null;
  loggedIn: boolean;
  /** Nulo até a pessoa montar o avatar: a compra oferece criar. */
  avatar: AvatarConfig | null;
  orders: CustomerOrder[];
  cart: CartState | null;
  /** Quando verdadeiro, as compras desta pessoa não aparecem no aviso público. */
};

const INITIAL: CustomerState = { account: null, loggedIn: false, avatar: null, orders: [], cart: null };

/** Tempo de reserva dos ingressos no carrinho: decisão de tela, sem regra de negócio ainda. */
export const CART_MINUTES = 10;

const STORAGE_KEY = "ntix.demo.customer.v1";

function loadStored(): CustomerState | null {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return stored ? { ...INITIAL, ...(JSON.parse(stored) as Partial<CustomerState>) } : null;
  } catch {
    return null;
  }
}

function store(state: CustomerState) {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    // Sem armazenamento a demonstração segue funcionando, só não sobrevive a um recarregamento.
  }
}

/** Conta pronta para conhecer a área do cliente sem precisar comprar antes. */
export function demoAccountState(now: number): Partial<CustomerState> {
  return {
    account: { name: "Cliente de Demonstração", email: "cliente@exemplo.test", document: "529.982.247-25" },
    loggedIn: true,
    avatar: {
      ...DEFAULT_LOOKS.FEMININE,
      hairStyle: "waves",
      hairColor: "#e85a9b",
      glasses: "heart",
      paint: "glitter",
      jewelry: "hoops",
    },
    orders: demoOrders(now),
  };
}

type CustomerContext = {
  customer: CustomerState;
  update: (changes: Partial<CustomerState>) => void;
  updateOrder: (code: string, change: (order: CustomerOrder) => CustomerOrder) => void;
  avatarOrDefault: AvatarConfig;
};

const Context = createContext<CustomerContext | null>(null);

type ProviderProps = {
  children: ReactNode;
  /** Estado fixo, sem ler nem gravar no navegador. Para testes. */
  initial?: Partial<CustomerState>;
};

export function CustomerProvider({ children, initial }: ProviderProps) {
  const [customer, setCustomer] = useState<CustomerState>(() => (initial ? { ...INITIAL, ...initial } : (loadStored() ?? INITIAL)));

  useEffect(() => {
    if (!initial) store(customer);
  }, [customer, initial]);

  const value = useMemo<CustomerContext>(
    () => ({
      customer,
      update: (changes) => setCustomer((current) => ({ ...current, ...changes })),
      updateOrder: (code, change) =>
        setCustomer((current) => ({ ...current, orders: current.orders.map((order) => (order.code === code ? change(order) : order)) })),
      avatarOrDefault: normalizeAvatar(customer.avatar as unknown as Record<string, unknown> | null),
    }),
    [customer],
  );
  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useCustomer(): CustomerContext {
  const context = useContext(Context);
  if (!context) throw new Error("useCustomer precisa de um CustomerProvider");
  return context;
}
