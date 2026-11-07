# Whitelabel: arquivos default

Pontos de partida para criar o arquivo de uma produtora (REQ3). Não são servidos pela API: só os arquivos de `devops/whitelabel/` são.

| Arquivo | Cor principal (`colors.primary.500`) |
|---|---|
| `amarelo.json` | `#e8b923` |
| `laranja.json` | `#c99b14`, a paleta da Ntix |
| `azul.json` | `#2563eb` |
| `verde.json` | `#16a34a` |
| `roxo.json` | `#7c3aed` |
| `branco.json` | `#f4f4f2`, marca monocromática |
| `rosa.json` | `#db2777` |

## Criar o arquivo de uma produtora

1. Copie o default da cor para `devops/whitelabel/{slug}.json`, com o slug da produtora (ex.: `palco-sul.json`).
2. Troque `brand.name` e, se houver, `brand.logoUrl` (https ou caminho do front).
3. Ajuste o que a produtora pedir. Os campos e formatos aceitos estão no REQ3 (RN4); o que faltar fica no padrão Ntix.

Cada default tem a mesma estrutura do `ntix.json`: só a escala `colors.primary` e `colors.action` mudam. As escalas foram geradas a partir da cor 500, clareando até o 400 e escurecendo a partir do 600, nas mesmas proporções em todas as cores. Os testes integrados (`WhitelabelReaderTests`) garantem que todos passam na validação do endpoint.

`branco` usa uma ação quase branca. Ela funciona nas cenas escuras, mas tem pouco contraste no tema claro, e o "n" do wordmark Ntix some sobre fundo claro. Para essa cor, informe um `logoUrl`.
