import "./store.css";

import type { CSSProperties } from "react";
import { Swatch, TextField, Window, brandVars } from "../../design-system";
import { slugify } from "../../producer/text";

const STORE_THEMES = [
  { name: "Ntix", brand: "#c99b14", hover: "#d9af28", event: "Noite de Estreia", bg: "#232529", ink: "#f4f4f2", muted: "#b4b6bb", line: "rgb(255 255 255 / 0.12)", poster: "#35383d" },
  { name: "Festival", brand: "#14a3a3", hover: "#2bb8b8", event: "Festival Maré Alta", bg: "#07252b", ink: "#eefaf9", muted: "#a5c9c9", line: "rgb(255 255 255 / 0.14)", poster: "#0c3a42" },
  { name: "Teatro", brand: "#9a2545", hover: "#b03257", event: "Temporada Aurora", bg: "#f7f1ea", ink: "#2b1a1f", muted: "#6d565c", line: "rgb(43 26 31 / 0.16)", poster: "#e9dcd0" },
  { name: "Esporte", brand: "#3d5afe", hover: "#5a73ff", event: "Final Regional", bg: "#10142b", ink: "#f0f2ff", muted: "#aeb5d6", line: "rgb(255 255 255 / 0.14)", poster: "#1c2348" },
];

export type StoreLook = { themeIndex: number; customColor?: string };

export const DEFAULT_STORE_LOOK: StoreLook = { themeIndex: 0 };

type StoreControlsProps = {
  name: string;
  onNameChange: (name: string) => void;
  look: StoreLook;
  onLookChange: (look: StoreLook) => void;
};

export function StoreControls({ name, onNameChange, look, onLookChange }: StoreControlsProps) {
  return (
    <>
      <TextField
        id="brand-name"
        label="Nome da sua produtora"
        type="text"
        maxLength={40}
        placeholder="Digite para testar"
        autoComplete="off"
        value={name}
        onChange={(event) => onNameChange(event.target.value)}
      />

      <div className="n-field">
        <span className="n-field__label" id="brand-style-label">Estilo de exemplo</span>
        <div className="brand-lab__swatches" role="group" aria-labelledby="brand-style-label">
          {STORE_THEMES.map((option, index) => (
            <Swatch
              key={option.name}
              color={option.brand}
              pressed={!look.customColor && index === look.themeIndex}
              onClick={() => onLookChange({ themeIndex: index })}
            >
              {option.name}
            </Swatch>
          ))}
        </div>
      </div>

      <div className="brand-lab__color">
        <label className="n-field__label" htmlFor="brand-color">Ou escolha a sua cor</label>
        <input
          id="brand-color"
          className="brand-lab__picker"
          type="color"
          value={look.customColor ?? STORE_THEMES[look.themeIndex].brand}
          onChange={(event) => onLookChange({ ...look, customColor: event.target.value })}
        />
      </div>
    </>
  );
}

export function StorePreview({ name, look }: { name: string; look: StoreLook }) {
  const theme = STORE_THEMES[look.themeIndex];
  const storeName = name.trim() || "Sua Produtora";
  const storeStyle = {
    ...(look.customColor ? brandVars(look.customColor) : brandVars(theme.brand, theme.hover)),
    "--store-bg": theme.bg,
    "--store-ink": theme.ink,
    "--store-muted": theme.muted,
    "--store-line": theme.line,
    "--store-poster": theme.poster,
  } as CSSProperties;

  return (
    <div role="group" aria-label="Pré-visualização da loja">
      <Window
        bar={
          <>
            <span className="window-dots" aria-hidden="true"><i /><i /><i /></span>
            <span>{slugify(name) || "sua-produtora"}.com.br</span>
          </>
        }
      >
        <div className="store" data-brand="" style={storeStyle}>
          <div className="store__head">
            <span className="store__logo" aria-hidden="true">{storeName.charAt(0).toUpperCase()}</span>
            <strong>{storeName}</strong>
            <span className="store__menu">Eventos · Meus ingressos</span>
          </div>
          <div className="store__hero">
            <div className="store__poster" aria-hidden="true">
              <span className="store__poster-sun" />
              <span className="store__poster-date">12<br />DEZ</span>
            </div>
            <div className="store__event">
              <span className="store__tag">Em venda · 2º lote</span>
              <p className="store__event-title">{theme.event}</p>
              <p className="store__event-meta">Sábado, 20h · Teatro Municipal</p>
              <div className="store__buy">
                <span><small>A partir de</small><b className="n-figure">R$ 120,00</b></span>
                <span className="store__cta">Comprar ingressos</span>
              </div>
            </div>
          </div>
        </div>
      </Window>
    </div>
  );
}
