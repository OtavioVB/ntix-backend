import type { ReactElement } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { overviewSegment, panelPath } from "../../producer/paths";
import { useProducerSession } from "../../producer/session";
import { ApprovalPage } from "./ApprovalPage";
import { isRealApi } from "../../api/mode";
import { EventsPage } from "./EventsPage";
import { ProducerEventsPage } from "./ProducerEventsPage";
import { IntegrationsPage } from "./IntegrationsPage";
import { LOCKED_DOMAINS, LockedPage } from "./LockedPage";
import { OverviewPage } from "./OverviewPage";
import { PanelLayout } from "./PanelLayout";
import { TeamPage } from "./TeamPage";
import { TicketeriaPage } from "./TicketeriaPage";
import { DashboardPage } from "./production/DashboardPage";
import { EventDetailPage, EventFormPage, EventsListPage } from "./production/EventsPages";
import { EntryPage } from "./production/EntryPage";
import { ChargesPage, PlanPage, WalletPage } from "./production/FinancePages";
import { AffiliatesPage, CustomersPage, SalesPage } from "./production/SalesPages";

/** Antes da aprovação, o ensaio. Depois, a plataforma de verdade. */
function ByStatus({ rehearsal, production }: { rehearsal: ReactElement; production: ReactElement }) {
  const { session } = useProducerSession();
  return session.status === "ACTIVE" ? production : rehearsal;
}

const toEvents = <Navigate to={panelPath("eventos")} replace />;
const overview = <ByStatus rehearsal={<OverviewPage />} production={<DashboardPage />} />;
const overviewPath = overviewSegment();

export default function PanelRoutes() {
  return (
    <Routes>
      <Route element={<PanelLayout />}>
        {/* No subdomínio do produtor, a raiz é o cadastro: a visão geral ganha caminho próprio. */}
        {overviewPath ? <Route path={overviewPath} element={overview} /> : <Route index element={overview} />}
        {/* Com ?api=real, a aba fala com a API (REQ9) em qualquer situação da produtora: o rascunho não depende da aprovação. */}
        <Route path="eventos" element={isRealApi() ? <ProducerEventsPage /> : <ByStatus rehearsal={<EventsPage />} production={<EventsListPage />} />} />
        <Route path="eventos/novo" element={<ByStatus rehearsal={toEvents} production={<EventFormPage />} />} />
        <Route path="eventos/:eventId" element={<ByStatus rehearsal={toEvents} production={<EventDetailPage />} />} />
        <Route path="vendas" element={<ByStatus rehearsal={<LockedPage domain={LOCKED_DOMAINS.vendas} />} production={<SalesPage />} />} />
        <Route path="portaria" element={<ByStatus rehearsal={<LockedPage domain={LOCKED_DOMAINS.portaria} />} production={<EntryPage />} />} />
        <Route path="clientes" element={<ByStatus rehearsal={<LockedPage domain={LOCKED_DOMAINS.clientes} />} production={<CustomersPage />} />} />
        <Route path="afiliados" element={<ByStatus rehearsal={<LockedPage domain={LOCKED_DOMAINS.afiliados} />} production={<AffiliatesPage />} />} />
        <Route path="carteira" element={<ByStatus rehearsal={<LockedPage domain={LOCKED_DOMAINS.carteira} />} production={<WalletPage />} />} />
        <Route path="cobrancas" element={<ByStatus rehearsal={<LockedPage domain={LOCKED_DOMAINS.cobrancas} />} production={<ChargesPage />} />} />
        <Route path="plano" element={<ByStatus rehearsal={<LockedPage domain={LOCKED_DOMAINS.plano} />} production={<PlanPage />} />} />
        <Route path="ticketeria" element={<TicketeriaPage />} />
        <Route path="equipe" element={<TeamPage />} />
        <Route path="integracoes" element={<IntegrationsPage />} />
        <Route path="aprovacao" element={<ApprovalPage />} />
        <Route path="*" element={<Navigate to={panelPath()} replace />} />
      </Route>
    </Routes>
  );
}
