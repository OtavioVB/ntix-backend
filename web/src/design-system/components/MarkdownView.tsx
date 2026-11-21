import type { ReactNode } from "react";
import Markdown, { type Components } from "react-markdown";

/** Subconjunto aceito na descrição (REQ14, RN19): o resto é desembrulhado como texto. */
const ALLOWED_ELEMENTS = ["p", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "em", "ul", "ol", "li", "a", "blockquote", "br"];

function SafeLink({ href, children }: Readonly<{ href?: string; children?: ReactNode }>) {
  // RN20: só https vira link, aberto em outra aba e sem passar a página de origem.
  if (!href?.startsWith("https://")) return <span>{children}</span>;

  return <a href={href} target="_blank" rel="noopener noreferrer nofollow">{children}</a>;
}

// Título vira h2 e qualquer nível abaixo, h3: a página do evento já tem o h1.
const COMPONENTS: Components = {
  h1: ({ children }) => <h2>{children}</h2>,
  h4: ({ children }) => <h3>{children}</h3>,
  h5: ({ children }) => <h3>{children}</h3>,
  h6: ({ children }) => <h3>{children}</h3>,
  a: ({ href, children }) => <SafeLink href={href}>{children}</SafeLink>,
};

/**
 * Descrição em Markdown renderizada com segurança (REQ14, RN20, RFC 0010): sem HTML cru, só os elementos do subconjunto,
 * e links que não sejam https viram texto.
 */
export function MarkdownView({ markdown, className = "n-markdown" }: Readonly<{ markdown: string; className?: string }>) {
  return (
    <div className={className}>
      <Markdown skipHtml allowedElements={ALLOWED_ELEMENTS} unwrapDisallowed components={COMPONENTS}>
        {markdown}
      </Markdown>
    </div>
  );
}
