# RFC 0011: Cobertura dos testes do front no SonarQube Cloud

- **Status**: Aceita
- **Data**: 2026-11-21
- **Requisito**: [REQ14 - Organização da Rota de Eventos do Produtor](../../backlog/REQ14-organizacao-da-rota-de-eventos-do-produtor.md), PR #28

## Contexto

O SonarQube Cloud analisa o repositório inteiro, inclusive `web/`, mas a esteira só enviava a cobertura do .NET (`coverage.opencover.xml`). Todo TypeScript novo contava como 0% de cobertura, mesmo testado pelo Vitest na máquina. No PR do REQ14, o C# novo ficou entre 95% e 100%, e o código novo como um todo ficou em 63%, abaixo dos 80% do Quality Gate. Os testes do front também não rodavam na esteira.

## Decisão

- `@vitest/coverage-v8` `5.0.5` (15/10/2026, mesma versão do `vitest` instalado, que ele exige como peer), em `devDependencies` do `web/package.json`, fixado sem `^`. `npm audit` sem vulnerabilidades na instalação.
- `web/vite.config.ts` gera `web/coverage/lcov.info` com `vitest run --coverage`, com caminhos relativos à raiz do repositório (`projectRoot: ".."`) e só os fontes (`src/**/*.{ts,tsx}`, sem os testes, o setup e o `main.tsx`).
- O job do Sonar (`.github/workflows/static-analysis.yml`) instala o Node, roda os testes do front com cobertura e passa `sonar.javascript.lcov.reportPaths=web/coverage/lcov.info` ao scanner. Os testes do front passam a rodar na esteira.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Não adicionar dependência (excluir `web/**` da cobertura do Sonar) | Afrouxa o Quality Gate: o front deixaria de ser cobrado. Decisão do usuário, que escolheu enviar a cobertura |
| `@vitest/coverage-istanbul` | Instrumenta o código antes de rodar, mais lento; o v8 usa a cobertura nativa do Node e é o provedor padrão do Vitest |
| Gerar a cobertura em outro job e passar por artefato | Mais um job e um artefato para manter; o Sonar já roda num job com tudo de que precisa |

## Justificativa

É o provedor oficial do Vitest, mantido junto com ele e na mesma versão, licença MIT, sem custo. Só entra no desenvolvimento e na esteira: não vai para o bundle do front.

## Consequências

- O job do Sonar fica alguns segundos mais lento (`npm ci` e os testes do front).
- `web/coverage/` é gerado localmente e fica fora do Git (`.gitignore`).
- Atualizar o `vitest` exige atualizar o `@vitest/coverage-v8` na mesma versão.

## Como reverter

Remover o pacote do `package.json`, o bloco `coverage` do `vite.config.ts` e os passos do Node e o parâmetro `sonar.javascript.lcov.reportPaths` do job do Sonar.
