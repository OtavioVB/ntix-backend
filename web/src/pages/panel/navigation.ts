import type { IconName } from "../../design-system";

export type PanelArea = {
  path: string;
  label: string;
  icon: IconName;
  /** Bloqueada até a aprovação: a pessoa vê que existe, mas fica atrás da cortina. */
  lockedInRehearsal: boolean;
};

export const PANEL_GROUPS: { label: string; areas: PanelArea[] }[] = [
  {
    label: "Operação",
    areas: [
      { path: "/painel", label: "Visão geral", icon: "overview", lockedInRehearsal: false },
      { path: "/painel/eventos", label: "Eventos e lotes", icon: "calendar", lockedInRehearsal: false },
      { path: "/painel/vendas", label: "Vendas e ingressos", icon: "ticket", lockedInRehearsal: true },
      { path: "/painel/portaria", label: "Portaria", icon: "scan", lockedInRehearsal: true },
      { path: "/painel/clientes", label: "Clientes", icon: "users", lockedInRehearsal: true },
    ],
  },
  {
    label: "Alcance",
    areas: [
      { path: "/painel/ticketeria", label: "Ticketeria", icon: "store", lockedInRehearsal: false },
      { path: "/painel/afiliados", label: "Afiliados", icon: "share", lockedInRehearsal: true },
    ],
  },
  {
    label: "Financeiro",
    areas: [
      { path: "/painel/carteira", label: "Carteira", icon: "wallet", lockedInRehearsal: true },
      { path: "/painel/cobrancas", label: "Cobranças", icon: "receipt", lockedInRehearsal: true },
      { path: "/painel/plano", label: "Plano de intermediação", icon: "percent", lockedInRehearsal: true },
    ],
  },
  {
    label: "Conta",
    areas: [{ path: "/painel/equipe", label: "Equipe", icon: "team", lockedInRehearsal: false }],
  },
];
