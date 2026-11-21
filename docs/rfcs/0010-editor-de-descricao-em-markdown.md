# RFC 0010: Editor da descrição do evento em Markdown

- **Status**: Aceita
- **Data**: 2026-11-21
- **Requisito**: [REQ14 - Organização da Rota de Eventos do Produtor](../../backlog/REQ14-organizacao-da-rota-de-eventos-do-produtor.md) (RN19 a RN23)

## Contexto

A descrição do evento (REQ9) é um `textarea` de texto puro, e a vitrine (REQ11) a mostra como parágrafos corridos. O REQ14 pede que o produtor escreva com títulos, subtítulos, negrito, itálico, listas, links e citações, numa edição fluida, sem ver sintaxe, e que a vitrine mostre o resultado formatado.

Isso exige duas peças que o front (`web/`, React 19 + Vite) não tem:

1. **Editor visual** que grave Markdown: seleção, desfazer/refazer, colar de outro site sem trazer HTML, atalhos de teclado e de digitação (`## `, `- `), acessibilidade. `contenteditable` cru resolve a demonstração, mas não esses casos.
2. **Renderizador seguro** de Markdown na vitrine: transforma o texto em elementos React sem `dangerouslySetInnerHTML`, ignora HTML cru e filtra protocolos de link.

## Decisão

- **Editor (painel do produtor)**: [Lexical](https://lexical.dev) (MIT, Meta), pacotes `lexical`, `@lexical/react`, `@lexical/rich-text`, `@lexical/list`, `@lexical/link` e `@lexical/markdown`. Os transformadores oficiais de `@lexical/markdown` convertem o estado do editor para Markdown e de volta, limitados aos nós da RN19 (título, subtítulo, parágrafo, negrito, itálico, listas, link, citação).
- **Renderizador (vitrine)**: [`react-markdown`](https://github.com/remarkjs/react-markdown) (MIT), com `skipHtml`, `allowedElements` restrito à RN19 e `urlTransform` que só aceita `https`.
- Versões fixadas exatas (sem `^`) no `package.json`: `lexical`, `@lexical/react`, `@lexical/rich-text`, `@lexical/list`, `@lexical/link` e `@lexical/markdown` em `0.53.0` (28/10/2026), sempre na mesma versão; `react-markdown` em `10.1.0` (07/03/2025). `npm audit --omit=dev` sem vulnerabilidades na instalação. O `yjs` (colaboração) é peer opcional do `@lexical/react` e não é instalado.
- Onde: `RichTextEditor` e `MarkdownView` em `web/src/design-system/components/`. O editor é carregado com `React.lazy` só em `/eventos/novo` e `/eventos/{id}/editar`; o renderizador entra no chunk da página do evento da vitrine.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Não adicionar dependência (`contenteditable` + parser próprio) | Seleção, colagem, desfazer e listas aninhadas são justamente a parte difícil; um parser próprio de Markdown é superfície de XSS mantida por nós |
| Tiptap (ProseMirror) | Bom editor, mas a conversão para Markdown depende de extensão da comunidade (`tiptap-markdown`), não do mantenedor |
| Milkdown | Markdown nativo, mas comunidade e base de uso menores, e acopla tema próprio que concorre com o design system |
| Editor de Markdown com prévia (`textarea` + visualização) | Não atende a "experiência fluida": o produtor veria a sintaxe |
| Renderizar na vitrine com o próprio Lexical (somente leitura) | Carregaria o editor inteiro para o comprador; o renderizador é bem menor |
| Guardar HTML em vez de Markdown | Exigiria sanitização no back e no front e amarraria o formato ao editor |

## Justificativa

- Lexical tem a conversão Markdown oficial, é mantido pela Meta, compatível com React 19 e não traz CSS próprio: o visual fica com os tokens do design system e com o whitelabel.
- `react-markdown` gera elementos React (sem `innerHTML`) e descarta HTML cru, o que fecha o risco de XSS da descrição na vitrine.
- Markdown como formato gravado mantém a API simples (texto, limite de 5000 já existente) e as descrições antigas válidas sem migração.
- Licenças MIT, sem custo.

## Consequências

- Bundle: o editor só pesa nas rotas de criação e edição do painel; a vitrine ganha apenas o renderizador.
- Testes: Vitest com jsdom cobre a conversão para Markdown e a renderização segura; interações de seleção no editor são testadas pelas ações da barra e pelos atalhos.
- Nenhum impacto no back, em `devops/docker-compose.yml` ou na operação.
- Atualizações do Lexical precisam subir todos os pacotes juntos.

## Como reverter

Trocar `RichTextEditor` por `textarea` e `MarkdownView` por parágrafos, como era no REQ9, e remover os pacotes do `package.json`. As descrições gravadas continuam legíveis como texto, porque Markdown é texto.
