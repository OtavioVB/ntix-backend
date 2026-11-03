import { Suspense, lazy } from "react";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import { CustomerProvider } from "./customer/session";
import { LoginPage } from "./pages/login/LoginPage";
import { LOGIN_PATH, isProducerHost } from "./producer/paths";
import { ProducerSessionProvider } from "./producer/session";

const HubRoutes = lazy(() => import("./pages/hub/HubRoutes"));
const PanelRoutes = lazy(() => import("./pages/panel/PanelRoutes"));
const ProducersPage = lazy(() => import("./pages/producers/ProducersPage").then((module) => ({ default: module.ProducersPage })));

export function App() {
  return (
    <BrowserRouter>
      <ProducerSessionProvider>
        <CustomerProvider>
          <Suspense fallback={null}>
            {/* produtores.ntix.com.br: cadastro na raiz e painel sem prefixo. Nos demais endereços, o hub e a área do produtor juntos. */}
            {isProducerHost() ? (
              <Routes>
                <Route path="/" element={<ProducersPage />} />
                <Route path={LOGIN_PATH} element={<LoginPage />} />
                <Route path="/*" element={<PanelRoutes />} />
              </Routes>
            ) : (
              <Routes>
                <Route path="/produtores" element={<ProducersPage />} />
                <Route path={LOGIN_PATH} element={<LoginPage />} />
                <Route path="/painel/*" element={<PanelRoutes />} />
                <Route path="/*" element={<HubRoutes />} />
              </Routes>
            )}
          </Suspense>
        </CustomerProvider>
      </ProducerSessionProvider>
    </BrowserRouter>
  );
}
