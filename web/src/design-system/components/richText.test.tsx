import { render, screen, waitFor } from "@testing-library/react";
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
