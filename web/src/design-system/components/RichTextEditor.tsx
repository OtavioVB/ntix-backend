import { useEffect, useState } from "react";
import { LexicalComposer } from "@lexical/react/LexicalComposer";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import { ContentEditable } from "@lexical/react/LexicalContentEditable";
import { LexicalErrorBoundary } from "@lexical/react/LexicalErrorBoundary";
import { HistoryPlugin } from "@lexical/react/LexicalHistoryPlugin";
import { LinkPlugin } from "@lexical/react/LexicalLinkPlugin";
import { ListPlugin } from "@lexical/react/LexicalListPlugin";
import { MarkdownShortcutPlugin } from "@lexical/react/LexicalMarkdownShortcutPlugin";
import { OnChangePlugin } from "@lexical/react/LexicalOnChangePlugin";
import { RichTextPlugin } from "@lexical/react/LexicalRichTextPlugin";
import { $isLinkNode, LinkNode, TOGGLE_LINK_COMMAND } from "@lexical/link";
import { INSERT_ORDERED_LIST_COMMAND, INSERT_UNORDERED_LIST_COMMAND, ListItemNode, ListNode } from "@lexical/list";
import {
  $convertFromMarkdownString,
  $convertToMarkdownString,
  BOLD_ITALIC_STAR,
  BOLD_STAR,
  HEADING,
  ITALIC_STAR,
  LINK,
  ORDERED_LIST,
  QUOTE,
  UNORDERED_LIST,
  type Transformer,
} from "@lexical/markdown";
import { $createHeadingNode, $createQuoteNode, $isHeadingNode, $isQuoteNode, HeadingNode, QuoteNode } from "@lexical/rich-text";
import {
  $createParagraphNode,
  $getSelection,
  $isElementNode,
  $isRangeSelection,
  $setSelection,
  COMMAND_PRIORITY_NORMAL,
  FORMAT_TEXT_COMMAND,
  KEY_MODIFIER_COMMAND,
  type BaseSelection,
  type ElementNode,
} from "lexical";

/** Só o subconjunto da descrição (REQ14, RN19): sem código, tabela, imagem nem HTML. */
export const DESCRIPTION_TRANSFORMERS: Transformer[] = [HEADING, QUOTE, UNORDERED_LIST, ORDERED_LIST, BOLD_ITALIC_STAR, BOLD_STAR, ITALIC_STAR, LINK];

type Block = "paragraph" | "h2" | "h3" | "quote";

const isHttps = (url: string) => /^https:\/\/[^\s]+$/.test(url);

const THEME = {
  paragraph: "n-markdown__p",
  heading: { h1: "n-markdown__h2", h2: "n-markdown__h2", h3: "n-markdown__h3", h4: "n-markdown__h3", h5: "n-markdown__h3", h6: "n-markdown__h3" },
  quote: "n-markdown__quote",
  list: { ul: "n-markdown__ul", ol: "n-markdown__ol", listitem: "n-markdown__li" },
  link: "n-markdown__link",
  text: { bold: "n-markdown__bold", italic: "n-markdown__italic" },
};

function createBlock(block: Block): ElementNode {
  if (block === "h2" || block === "h3") return $createHeadingNode(block);
  if (block === "quote") return $createQuoteNode();
  return $createParagraphNode();
}

function blockOf(node: ElementNode): Block {
  if ($isHeadingNode(node)) return node.getTag() === "h3" ? "h3" : "h2";
  if ($isQuoteNode(node)) return "quote";
  return "paragraph";
}

/** Título "#" e níveis abaixo de "###" viram h2/h3: a página já tem o h1 e a descrição só usa dois níveis. */
function HeadingLevelPlugin() {
  const [editor] = useLexicalComposerContext();

  useEffect(() => editor.registerNodeTransform(HeadingNode, (node) => {
    const tag = node.getTag();
    if (tag === "h2" || tag === "h3") return;
    const replacement = $createHeadingNode(tag === "h1" ? "h2" : "h3");
    replacement.append(...node.getChildren());
    node.replace(replacement);
  }), [editor]);

  return null;
}

function Toolbar({ id }: Readonly<{ id: string }>) {
  const [editor] = useLexicalComposerContext();
  const [linkOpen, setLinkOpen] = useState(false);
  const [url, setUrl] = useState("https://");
  const [linkError, setLinkError] = useState<string>();
  const [saved, setSaved] = useState<BaseSelection | null>(null);

  function setBlock(block: Block) {
    editor.update(() => {
      const selection = $getSelection();
      if (!$isRangeSelection(selection)) return;

      const blocks = new Set<ElementNode>();
      for (const node of [selection.anchor.getNode(), ...selection.getNodes()]) {
        const top = node.getTopLevelElementOrThrow();
        if ($isElementNode(top)) blocks.add(top);
      }

      // Clicar de novo no mesmo formato volta para parágrafo.
      const target = [...blocks].every((node) => blockOf(node) === block) ? "paragraph" : block;

      for (const node of blocks) {
        if (node instanceof ListNode) continue;
        const replacement = createBlock(target);
        replacement.append(...node.getChildren());
        node.replace(replacement);
      }
    });
    editor.focus();
  }

  function openLink() {
    const { selection, isLink } = editor.getEditorState().read(() => {
      const current = $getSelection();
      return {
        selection: current ? current.clone() : null,
        isLink: $isRangeSelection(current) && current.getNodes().some((node) => $isLinkNode(node) || $isLinkNode(node.getParent())),
      };
    });

    // Já é link: o botão remove.
    if (isLink) {
      editor.dispatchCommand(TOGGLE_LINK_COMMAND, null);
      return;
    }

    setSaved(selection);
    setUrl("https://");
    setLinkError(undefined);
    setLinkOpen(true);
  }

  function applyLink() {

    const value = url.trim();

    if (!isHttps(value)) {
      setLinkError("O link precisa começar com https://.");
      return;
    }

    editor.update(() => {
      if (saved) $setSelection(saved.clone());
    });
    editor.dispatchCommand(TOGGLE_LINK_COMMAND, { url: value, target: "_blank", rel: "noopener noreferrer nofollow" });
    setLinkOpen(false);
    editor.focus();
  }

  // Ctrl+K abre o link, como nos editores de texto.
  useEffect(() => editor.registerCommand(KEY_MODIFIER_COMMAND, (event: KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
      event.preventDefault();
      openLink();
      return true;
    }
    return false;
  }, COMMAND_PRIORITY_NORMAL), [editor]);

  return (
    <div className="n-rich-text__toolbar" role="toolbar" aria-label="Formatação da descrição" aria-controls={id}>
      <button type="button" onClick={() => setBlock("h2")}>Título</button>
      <button type="button" onClick={() => setBlock("h3")}>Subtítulo</button>
      <button type="button" aria-label="Negrito" title="Negrito (Ctrl+B)" onClick={() => editor.dispatchCommand(FORMAT_TEXT_COMMAND, "bold")}><strong>N</strong></button>
      <button type="button" aria-label="Itálico" title="Itálico (Ctrl+I)" onClick={() => editor.dispatchCommand(FORMAT_TEXT_COMMAND, "italic")}><em>I</em></button>
      <button type="button" onClick={() => editor.dispatchCommand(INSERT_UNORDERED_LIST_COMMAND, undefined)}>Lista</button>
      <button type="button" onClick={() => editor.dispatchCommand(INSERT_ORDERED_LIST_COMMAND, undefined)}>Lista numerada</button>
      <button type="button" title="Link (Ctrl+K)" onClick={openLink}>Link</button>
      <button type="button" onClick={() => setBlock("quote")}>Citação</button>

      {linkOpen && (
        <div className="n-rich-text__link" role="group" aria-label="Link">
          <label className="n-field__label" htmlFor={`${id}-link`}>Endereço do link</label>
          <input id={`${id}-link`} className="n-input" type="url" value={url} autoFocus aria-invalid={linkError ? true : undefined} onChange={(event) => setUrl(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); applyLink(); } }} />
          {linkError && <p className="n-field__error" role="alert">{linkError}</p>}
          <button type="button" onClick={applyLink}>Aplicar</button>
          <button type="button" onClick={() => setLinkOpen(false)}>Cancelar</button>
        </div>
      )}
    </div>
  );
}

type RichTextEditorProps = {
  id: string;
  label: string;
  value: string;
  onChange: (markdown: string) => void;
  hint?: string;
  error?: string;
};

/**
 * Editor visual da descrição (REQ14, RN21, RFC 0010): a pessoa vê o texto formatado, nunca a sintaxe, e o que se grava é
 * Markdown. Atalhos: Ctrl+B, Ctrl+I, Ctrl+K, e "## ", "### ", "- ", "1. " e "> " no começo da linha.
 * O valor inicial é lido só na montagem: para trocar de evento, troque a `key`.
 */
export default function RichTextEditor({ id, label, value, onChange, hint, error }: Readonly<RichTextEditorProps>) {
  const initialConfig = {
    namespace: id,
    theme: THEME,
    nodes: [HeadingNode, QuoteNode, ListNode, ListItemNode, LinkNode],
    editorState: () => $convertFromMarkdownString(value, DESCRIPTION_TRANSFORMERS),
    onError: (failure: Error) => {
      throw failure;
    },
  };

  return (
    <div className="n-field n-rich-text">
      <span className="n-field__label" id={`${id}-label`}>{label}</span>
      <LexicalComposer initialConfig={initialConfig}>
        <Toolbar id={id} />
        <RichTextPlugin
          contentEditable={(
            <ContentEditable
              id={id}
              className="n-rich-text__content n-markdown"
              aria-labelledby={`${id}-label`}
              aria-describedby={hint ? `${id}-hint` : undefined}
              aria-invalid={error ? true : undefined}
              aria-multiline
            />
          )}
          ErrorBoundary={LexicalErrorBoundary}
        />
        <HistoryPlugin />
        <ListPlugin />
        <LinkPlugin validateUrl={isHttps} />
        <MarkdownShortcutPlugin transformers={DESCRIPTION_TRANSFORMERS} />
        <HeadingLevelPlugin />
        <OnChangePlugin ignoreSelectionChange onChange={(state) => state.read(() => onChange($convertToMarkdownString(DESCRIPTION_TRANSFORMERS)))} />
      </LexicalComposer>
      {hint && <p className="n-rich-text__hint" id={`${id}-hint`}>{hint}</p>}
      <p className="n-field__error">{error}</p>
    </div>
  );
}
