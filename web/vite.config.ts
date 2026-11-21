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
    // `vitest run --coverage`: o lcov vai para o SonarQube Cloud na esteira (RFC 0011).
    coverage: {
      provider: "v8",
      // Caminhos relativos à raiz do repositório, de onde o Sonar resolve os arquivos.
      reporter: [["lcov", { projectRoot: ".." }], "text-summary"],
      reportsDirectory: "coverage",
      include: ["src/**/*.{ts,tsx}"],
      exclude: ["src/**/*.test.{ts,tsx}", "src/test-setup.ts", "src/main.tsx"],
    },
  },
});
