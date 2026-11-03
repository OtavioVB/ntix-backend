import basicSsl from "@vitejs/plugin-basic-ssl";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  // HTTPS com certificado local autoassinado: o navegador do celular só libera a câmera (portaria) em conexão segura.
  plugins: [react(), basicSsl({ name: "ntix-dev" })],
  // host: aberto na rede local, para testar no celular pelo IP deste computador.
  server: { port: 5173, strictPort: true, host: true },
  test: {
    environment: "jsdom",
    setupFiles: "./src/test-setup.ts",
  },
});
