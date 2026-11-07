import "@fontsource-variable/archivo/wdth.css";
import "@fontsource/ibm-plex-mono/latin-400.css";
import "@fontsource/ibm-plex-mono/latin-500.css";
import "@fontsource/ibm-plex-mono/latin-600.css";
import "./design-system/styles.css";

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { initTheme } from "./design-system";
import { loadWhitelabel } from "./whitelabel";

initTheme();

/* A marca da implantação entra antes da primeira pintura; sem ela, abre com o padrão Ntix. */
loadWhitelabel().finally(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
});
