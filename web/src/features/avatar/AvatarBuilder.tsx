import { Button, Segmented } from "../../design-system";
import { Avatar } from "./Avatar";
import {
  BAGS,
  BEARDS,
  BOTTOMS,
  CLOTH_COLORS,
  EXPRESSIONS,
  EYE_COLORS,
  GLASSES,
  HAIR_COLORS,
  HAIR_STYLES,
  HATS,
  JEWELRY,
  PAINTS,
  SHOES,
  SKIN_TONES,
  TOPS,
  isDress,
  normalizeAvatar,
  optionsFor,
  randomAvatar,
  switchStyle,
  type AvatarConfig,
} from "./avatarOptions";

const STYLES = [
  { value: "FEMININE", label: "Feminino" },
  { value: "MASCULINE", label: "Masculino" },
] as const;

type OptionGridProps<T extends string> = {
  label: string;
  options: readonly { id: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
  preview: (id: T) => AvatarConfig;
  crop: "head" | "body";
};

function OptionGrid<T extends string>({ label, options, value, onChange, preview, crop }: OptionGridProps<T>) {
  return (
    <div className="avatar-field">
      <span className="n-field__label">{label}</span>
      <div className="avatar-options" role="group" aria-label={label}>
        {options.map((option) => (
          <button key={option.id} type="button" className="avatar-option" aria-pressed={option.id === value} onClick={() => onChange(option.id)}>
            <span className="avatar-option__frame"><Avatar config={preview(option.id)} crop={crop} /></span>
            <span>{option.label}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

function ColorRow({ label, colors, value, onChange }: { label: string; colors: readonly { value: string; label: string }[]; value: string; onChange: (value: string) => void }) {
  return (
    <div className="avatar-field">
      <span className="n-field__label" id={`avatar-${label}`}>{label}</span>
      <div className="avatar-colors" role="group" aria-labelledby={`avatar-${label}`}>
        {colors.map((color) => (
          <button
            key={color.value}
            type="button"
            className="avatar-color"
            style={{ background: color.value }}
            aria-pressed={color.value === value}
            aria-label={color.label}
            title={color.label}
            onClick={() => onChange(color.value)}
          />
        ))}
      </div>
    </div>
  );
}

const SKINS = SKIN_TONES.map((value, index) => ({ value, label: `Tom de pele ${index + 1}` }));

type AvatarBuilderProps = {
  value: AvatarConfig;
  onChange: (config: AvatarConfig) => void;
};

/** Monta o avatar. Roupas, cabelos e acessórios mudam com o estilo escolhido. Cada mudança já vale. */
export function AvatarBuilder({ value: stored, onChange }: AvatarBuilderProps) {
  const value = normalizeAvatar(stored as unknown as Record<string, unknown>);
  const set = (changes: Partial<AvatarConfig>) => onChange({ ...value, ...changes });
  const fem = value.style === "FEMININE";
  const forStyle = <T extends string,>(options: readonly { id: T; label: string; style?: AvatarConfig["style"] }[]) => optionsFor(options, value.style);

  return (
    <div className="avatar-builder">
      <div className="avatar-builder__head">
        <Segmented name="avatar-style" legend="Estilo do avatar" options={STYLES} value={value.style} onChange={(style) => onChange(switchStyle(value, style))} />
        <Button type="button" variant="ghost" onClick={() => onChange(randomAvatar(Math.random, value.style))}>Surpreenda-me</Button>
      </div>

      <section className="avatar-section" aria-labelledby="avatar-look-title">
        <h3 className="avatar-section__title" id="avatar-look-title">{fem ? "Look dela" : "Look dele"}</h3>
        <OptionGrid label="Parte de cima" crop="body" options={forStyle(TOPS)} value={value.top} onChange={(top) => set({ top })} preview={(top) => ({ ...value, top })} />
        <ColorRow label="Cor da parte de cima" colors={CLOTH_COLORS} value={value.topColor} onChange={(topColor) => set({ topColor })} />
        {!isDress(value.top) && (
          <>
            <OptionGrid label="Parte de baixo" crop="body" options={forStyle(BOTTOMS)} value={value.bottom} onChange={(bottom) => set({ bottom })} preview={(bottom) => ({ ...value, bottom })} />
            <ColorRow label="Cor da parte de baixo" colors={CLOTH_COLORS} value={value.bottomColor} onChange={(bottomColor) => set({ bottomColor })} />
          </>
        )}
        <OptionGrid label="Calçado" crop="body" options={forStyle(SHOES)} value={value.shoes} onChange={(shoes) => set({ shoes })} preview={(shoes) => ({ ...value, shoes })} />
      </section>

      <section className="avatar-section" aria-labelledby="avatar-hair-title">
        <h3 className="avatar-section__title" id="avatar-hair-title">Cabelo</h3>
        <OptionGrid label="Corte de cabelo" crop="head" options={forStyle(HAIR_STYLES)} value={value.hairStyle} onChange={(hairStyle) => set({ hairStyle })} preview={(hairStyle) => ({ ...value, hairStyle })} />
        <ColorRow label="Cor do cabelo" colors={HAIR_COLORS} value={value.hairColor} onChange={(hairColor) => set({ hairColor })} />
      </section>

      <section className="avatar-section" aria-labelledby="avatar-extras-title">
        <h3 className="avatar-section__title" id="avatar-extras-title">Acessórios</h3>
        <OptionGrid label="Na cabeça" crop="head" options={forStyle(HATS)} value={value.hat} onChange={(hat) => set({ hat })} preview={(hat) => ({ ...value, hat })} />
        <OptionGrid label="Óculos" crop="head" options={forStyle(GLASSES)} value={value.glasses} onChange={(glasses) => set({ glasses })} preview={(glasses) => ({ ...value, glasses })} />
        <OptionGrid label={fem ? "Joias" : "Corrente e relógio"} crop={fem ? "head" : "body"} options={forStyle(JEWELRY)} value={value.jewelry} onChange={(jewelry) => set({ jewelry })} preview={(jewelry) => ({ ...value, jewelry })} />
        <OptionGrid label={fem ? "Bolsa" : "Pochete"} crop="body" options={forStyle(BAGS)} value={value.bag} onChange={(bag) => set({ bag })} preview={(bag) => ({ ...value, bag })} />
        <OptionGrid label="Pintura no rosto" crop="head" options={forStyle(PAINTS)} value={value.paint} onChange={(paint) => set({ paint })} preview={(paint) => ({ ...value, paint })} />
      </section>

      <section className="avatar-section" aria-labelledby="avatar-face-title">
        <h3 className="avatar-section__title" id="avatar-face-title">Rosto</h3>
        <OptionGrid label="Expressão" crop="head" options={EXPRESSIONS} value={value.expression} onChange={(expression) => set({ expression })} preview={(expression) => ({ ...value, expression })} />
        <ColorRow label="Tom de pele" colors={SKINS} value={value.skin} onChange={(skin) => set({ skin })} />
        <ColorRow label="Cor dos olhos" colors={EYE_COLORS} value={value.eyes} onChange={(eyes) => set({ eyes })} />
        {!fem && <OptionGrid label="Barba" crop="head" options={forStyle(BEARDS)} value={value.beard} onChange={(beard) => set({ beard })} preview={(beard) => ({ ...value, beard })} />}
      </section>
    </div>
  );
}
