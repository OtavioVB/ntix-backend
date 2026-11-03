import { Navigate, Route, Routes } from "react-router-dom";
import { AccountLayout, AvatarPage, CustomerLoginPage, OrderDetailPage, OrdersPage, ProfilePage, TicketDetailPage, TicketsPage } from "../account/AccountPages";
import { CheckoutPage } from "./CheckoutPage";
import { EventPage } from "./EventPage";
import { HubHomePage } from "./HubHomePage";
import { HubLayout } from "./HubLayout";
import { OrderConfirmedPage } from "./OrderConfirmedPage";

/** Tudo o que o cliente final vê: vitrine, evento, compra e a conta dele. */
export default function HubRoutes() {
  return (
    <Routes>
      <Route element={<HubLayout />}>
        <Route index element={<HubHomePage />} />
        <Route path="eventos/:slug" element={<EventPage />} />
        <Route path="eventos/:slug/checkout" element={<CheckoutPage />} />
        <Route path="conta/entrar" element={<CustomerLoginPage />} />
        <Route path="conta/pedidos/:code/confirmado" element={<OrderConfirmedPage />} />
        <Route path="conta" element={<AccountLayout />}>
          <Route index element={<Navigate to="ingressos" replace />} />
          <Route path="ingressos" element={<TicketsPage />} />
          <Route path="ingressos/:code" element={<TicketDetailPage />} />
          <Route path="pedidos" element={<OrdersPage />} />
          <Route path="pedidos/:code" element={<OrderDetailPage />} />
          <Route path="avatar" element={<AvatarPage />} />
          <Route path="dados" element={<ProfilePage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
