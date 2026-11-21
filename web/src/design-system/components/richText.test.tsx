import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { MarkdownView } from "./MarkdownView";
import RichTextEditor from "./RichTextEditor";

describe("descrição na vitrine (MarkdownView)", () => {
  it("renderiza títulos, negrito, itálico, listas e citação", () => {
    render(<MarkdownView markdown={"## Atrações\n\n### Palco 1\n\n- **Banda Maré** às *21h*\n\n1. Portões\n\n> Leve documento."} />);

    expect(screen.getByRole("heading", { level: 2, name: "Atrações" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 3, name: "Palco 1" })).toBeInTheDocument();
    expect(screen.getByText("Banda Maré").tagName).toBe("STRONG");
    expect(screen.getByText("21h").tagName).toBe("EM");
    expect(screen.getAllByRole("list")).toHaveLength(2);
    expect(screen.getByText("Leve documento.").closest("blockquote")).not.toBeNull();
  });

  it("não executa HTML e transforma link que não é https em texto", () => {
    const { container } = render(<MarkdownView markdown={"<script>alert(1)</script><img src=x onerror=alert(1)>\n\n[golpe](javascript:alert(1)) e [site](http://exemplo.com)"} />);

    expect(container.querySelector("script")).toBeNull();
    expect(container.querySelector("img")).toBeNull();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.getByText("golpe")).toBeInTheDocument();
  });

  it("abre os links https em outra aba sem passar a origem", () => {
    render(<MarkdownView markdown="[Mapa](https://exemplo.com/mapa)" />);

    const link = screen.getByRole("link", { name: "Mapa" });
    expect(link).toHaveAttribute("href", "https://exemplo.com/mapa");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer nofollow");
  });

  it("título principal vira subtítulo da página, que já tem o h1", () => {
    render(<MarkdownView markdown="# Grande" />);

    expect(screen.getByRole("heading", { level: 2, name: "Grande" })).toBeInTheDocument();
  });
});

describe("editor da descrição (RichTextEditor)", () => {
  it("mostra o Markdown já formatado, sem a sintaxe", async () => {
    render(<RichTextEditor id="description" label="Descrição" value={"## Atrações\n\n- **Banda Maré**"} onChange={vi.fn()} />);

    const editor = screen.getByRole("textbox", { name: "Descrição" });
    await waitFor(() => expect(editor.querySelector("h2")).toHaveTextContent("Atrações"));
    expect(editor.querySelector("li")).toHaveTextContent("Banda Maré");
    expect(editor).not.toHaveTextContent("##");
    expect(editor).not.toHaveTextContent("**");
  });

  it("tem a barra de formatação com os comandos da descrição", () => {
    render(<RichTextEditor id="description" label="Descrição" value="" onChange={vi.fn()} />);

    const toolbar = screen.getByRole("toolbar", { name: "Formatação da descrição" });
    for (const name of ["Título", "Subtítulo", "Negrito", "Itálico", "Lista", "Lista numerada", "Link", "Citação"]) {
      expect(toolbar.querySelector(`button[aria-label="${name}"]`) ?? screen.getByRole("button", { name })).toBeInTheDocument();
    }
  });

  it("recusa link que não é https", async () => {
    const user = userEvent.setup();
    render(<RichTextEditor id="description" label="Descrição" value="Mapa do local" onChange={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: "Link" }));
    const input = screen.getByLabelText("Endereço do link");
    await user.clear(input);
    await user.type(input, "http://exemplo.com");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));

    expect(screen.getByRole("alert")).toHaveTextContent("O link precisa começar com https://.");
  });
});

describe("comandos do editor", () => {
  type Editor = { update: (fn: () => void, options?: { discrete?: boolean }) => void };

  /** Põe o cursor no fim do texto, como se a pessoa tivesse clicado ali. */
  async function caretAtEnd(): Promise<Editor> {
    const element = screen.getByRole("textbox", { name: "Descrição" }) as HTMLElement & { __lexicalEditor: Editor };
    const { $getRoot } = await import("lexical");
    act(() => element.__lexicalEditor.update(() => { $getRoot().selectEnd(); }, { discrete: true }));
    return element.__lexicalEditor;
  }

  /** Seleciona todo o texto. */
  async function selectAll() {
    const element = screen.getByRole("textbox", { name: "Descrição" }) as HTMLElement & { __lexicalEditor: Editor };
    const { $getRoot } = await import("lexical");
    act(() => element.__lexicalEditor.update(() => { $getRoot().select(0, $getRoot().getChildrenSize()); }, { discrete: true }));
  }

  function renderEditor(value: string) {
    const onChange = vi.fn();
    render(<RichTextEditor id="description" label="Descrição" value={value} onChange={onChange} hint="0 de 5000 caracteres." error="Erro de teste" />);
    return onChange;
  }

  const lastMarkdown = (onChange: ReturnType<typeof vi.fn>) => onChange.mock.calls.at(-1)?.[0] as string;

  it("transforma o parágrafo em título, em subtítulo e de volta em parágrafo", async () => {
    const onChange = renderEditor("Atrações");
    const user = userEvent.setup();
    await caretAtEnd();

    await user.click(screen.getByRole("button", { name: "Título" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("## Atrações"));

    await user.click(screen.getByRole("button", { name: "Subtítulo" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("### Atrações"));

    await user.click(screen.getByRole("button", { name: "Subtítulo" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("Atrações"));
  });

  it("vira citação", async () => {
    const onChange = renderEditor("Leve documento");
    const user = userEvent.setup();
    await caretAtEnd();

    await user.click(screen.getByRole("button", { name: "Citação" }));

    await waitFor(() => expect(lastMarkdown(onChange)).toBe("> Leve documento"));
  });

  it("aplica negrito e itálico no texto selecionado", async () => {
    const onChange = renderEditor("Banda Maré");
    const user = userEvent.setup();
    await selectAll();

    await user.click(screen.getByRole("button", { name: "Negrito" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("**Banda Maré**"));

    await selectAll();
    await user.click(screen.getByRole("button", { name: "Itálico" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("***Banda Maré***"));
  });

  it("vira lista com marcadores e lista numerada", async () => {
    const onChange = renderEditor("Portões às 20h");
    const user = userEvent.setup();
    await caretAtEnd();

    await user.click(screen.getByRole("button", { name: "Lista" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("- Portões às 20h"));

    await caretAtEnd();
    await user.click(screen.getByRole("button", { name: "Lista numerada" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("1. Portões às 20h"));
  });

  it("aplica link https no texto selecionado e remove com o mesmo botão", async () => {
    const onChange = renderEditor("Mapa");
    const user = userEvent.setup();
    await selectAll();

    await user.click(screen.getByRole("button", { name: "Link" }));
    const input = screen.getByLabelText("Endereço do link");
    await user.clear(input);
    await user.type(input, "https://exemplo.com/mapa{Enter}");

    await waitFor(() => expect(lastMarkdown(onChange)).toBe("[Mapa](https://exemplo.com/mapa)"));
    expect(screen.queryByLabelText("Endereço do link")).not.toBeInTheDocument();

    await selectAll();
    await user.click(screen.getByRole("button", { name: "Link" }));
    await waitFor(() => expect(lastMarkdown(onChange)).toBe("Mapa"));
  });

  it("abre o link com Ctrl+K e cancela", async () => {
    renderEditor("Mapa");
    const user = userEvent.setup();
    await selectAll();

    const element = screen.getByRole("textbox", { name: "Descrição" });
    act(() => { element.dispatchEvent(new KeyboardEvent("keydown", { key: "k", ctrlKey: true, bubbles: true, cancelable: true })); });

    expect(await screen.findByLabelText("Endereço do link")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByLabelText("Endereço do link")).not.toBeInTheDocument();
  });

  it("título de nível 1 ou abaixo de 3 vira h2 ou h3", async () => {
    renderEditor("# Grande\n\n#### Pequeno");
    await caretAtEnd();

    const editor = screen.getByRole("textbox", { name: "Descrição" });
    await waitFor(() => expect(editor.querySelector("h1")).toBeNull());
    expect(editor.querySelector("h2")).toHaveTextContent("Grande");
    expect(editor.querySelector("h3")).toHaveTextContent("Pequeno");
  });

  it("mostra a dica e o erro ligados ao campo", () => {
    renderEditor("");

    const editor = screen.getByRole("textbox", { name: "Descrição" });
    expect(editor).toHaveAttribute("aria-invalid", "true");
    expect(editor).toHaveAttribute("aria-describedby", "description-hint");
    expect(screen.getByText("Erro de teste")).toBeInTheDocument();
  });
});

describe("níveis de título na vitrine", () => {
  it("h4 a h6 viram h3", () => {
    render(<MarkdownView markdown={"#### Quatro\n\n##### Cinco\n\n###### Seis"} />);

    expect(screen.getAllByRole("heading", { level: 3 }).map((heading) => heading.textContent)).toEqual(["Quatro", "Cinco", "Seis"]);
  });
});
