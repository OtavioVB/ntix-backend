import type { IconName } from "../../design-system";
import { panelPath } from "../../producer/paths";

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
      { path: panelPath(), label: "Visão geral", icon: "overview", lockedInRehearsal: false },
      { path: panelPath("eventos"), label: "Eventos e lotes", icon: "calendar", lockedInRehearsal: false },
      { path: panelPath("vendas"), label: "Vendas e ingressos", icon: "ticket", lockedInRehearsal: true },
      { path: panelPath("portaria"), label: "Portaria", icon: "scan", lockedInRehearsal: true },
      { path: panelPath("clientes"), label: "Clientes", icon: "users", lockedInRehearsal: true },
    ],
  },
  {
    label: "Alcance",
    areas: [
      { path: panelPath("ticketeria"), label: "Ticketeria", icon: "store", lockedInRehearsal: false },
      { path: panelPath("afiliados"), label: "Afiliados", icon: "share", lockedInRehearsal: true },
    ],
  },
  {
    label: "Financeiro",
    areas: [
      { path: panelPath("carteira"), label: "Carteira", icon: "wallet", lockedInRehearsal: true },
      { path: panelPath("cobrancas"), label: "Cobranças", icon: "receipt", lockedInRehearsal: true },
      { path: panelPath("plano"), label: "Plano de intermediação", icon: "percent", lockedInRehearsal: true },
    ],
  },
  {
    label: "Conta",
    areas: [{ path: panelPath("equipe"), label: "Equipe", icon: "team", lockedInRehearsal: false }],
  },
];
