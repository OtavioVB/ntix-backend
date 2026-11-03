export type AvatarStyle = "FEMININE" | "MASCULINE";

type Option<T extends string> = { id: T; label: string; style?: AvatarStyle };

export const HAIR_STYLES = [
  { id: "long", label: "Longo liso", style: "FEMININE" },
  { id: "waves", label: "Ondas", style: "FEMININE" },
  { id: "bob", label: "Chanel com franja", style: "FEMININE" },
  { id: "spacebuns", label: "Coquinhos", style: "FEMININE" },
  { id: "braids", label: "Tranças", style: "FEMININE" },
  { id: "afro", label: "Black power", style: "FEMININE" },
  { id: "short", label: "Degradê", style: "MASCULINE" },
  { id: "quiff", label: "Topete", style: "MASCULINE" },
  { id: "mullet", label: "Mullet", style: "MASCULINE" },
  { id: "curly", label: "Cacheado", style: "MASCULINE" },
  { id: "mohawk", label: "Moicano", style: "MASCULINE" },
  { id: "buzz", label: "Raspado", style: "MASCULINE" },
  { id: "bald", label: "Careca", style: "MASCULINE" },
] as const;

export type HairStyle = (typeof HAIR_STYLES)[number]["id"];

export const TOPS = [
  { id: "cropped", label: "Cropped", style: "FEMININE", piece: true },
  { id: "blouse", label: "Blusinha ombro a ombro", style: "FEMININE", piece: true },
  { id: "bralette", label: "Top", style: "FEMININE", piece: true },
  { id: "corset", label: "Corset", style: "FEMININE", piece: true },
  { id: "slip", label: "Vestido de alcinha", style: "FEMININE", piece: false },
  { id: "fringe", label: "Vestido de franja", style: "FEMININE", piece: false },
  { id: "tank", label: "Regata", style: "MASCULINE", piece: true },
  { id: "band", label: "Camiseta de banda", style: "MASCULINE", piece: true },
  { id: "oversized", label: "Camisa larga", style: "MASCULINE", piece: true },
  { id: "openshirt", label: "Camisa aberta", style: "MASCULINE", piece: true },
  { id: "jersey", label: "Camisa de time", style: "MASCULINE", piece: true },
  { id: "jacket", label: "Jaqueta jeans", style: "MASCULINE", piece: true },
] as const;

export type Top = (typeof TOPS)[number]["id"];

export const BOTTOMS = [
  { id: "mini", label: "Minissaia", style: "FEMININE" },
  { id: "pleated", label: "Saia plissada", style: "FEMININE" },
  { id: "shorts", label: "Short jeans", style: "FEMININE" },
  { id: "wide", label: "Pantalona", style: "FEMININE" },
  { id: "bermuda", label: "Bermuda", style: "MASCULINE" },
  { id: "cargoshorts", label: "Bermuda cargo", style: "MASCULINE" },
  { id: "cargo", label: "Calça cargo", style: "MASCULINE" },
  { id: "baggy", label: "Jeans largo", style: "MASCULINE" },
  { id: "jogger", label: "Jogger", style: "MASCULINE" },
] as const;

export type Bottom = (typeof BOTTOMS)[number]["id"];

export const SHOES = [
  { id: "platform", label: "Plataforma", style: "FEMININE" },
  { id: "sneakers", label: "Tênis" },
  { id: "hitop", label: "Cano alto", style: "MASCULINE" },
  { id: "boots", label: "Coturno" },
  { id: "cowboy", label: "Bota texana" },
] as const;

export type Shoes = (typeof SHOES)[number]["id"];

export const HATS = [
  { id: "none", label: "Sem nada" },
  { id: "flowers", label: "Tiara de flores", style: "FEMININE" },
  { id: "bow", label: "Laço", style: "FEMININE" },
  { id: "cap", label: "Boné", style: "MASCULINE" },
  { id: "bucket", label: "Bucket", style: "MASCULINE" },
  { id: "bandana", label: "Bandana", style: "MASCULINE" },
  { id: "cowboy", label: "Chapéu de rodeio" },
] as const;

export type Hat = (typeof HATS)[number]["id"];

export const GLASSES = [
  { id: "none", label: "Sem óculos" },
  { id: "cat", label: "Gatinho", style: "FEMININE" },
  { id: "heart", label: "De coração", style: "FEMININE" },
  { id: "aviator", label: "Aviador", style: "MASCULINE" },
  { id: "square", label: "Quadrado", style: "MASCULINE" },
  { id: "sun", label: "De sol" },
] as const;

export type Glasses = (typeof GLASSES)[number]["id"];

export const JEWELRY = [
  { id: "none", label: "Sem nada" },
  { id: "hoops", label: "Argolas", style: "FEMININE" },
  { id: "choker", label: "Gargantilha", style: "FEMININE" },
  { id: "chain", label: "Corrente", style: "MASCULINE" },
  { id: "watch", label: "Relógio", style: "MASCULINE" },
] as const;

export type Jewelry = (typeof JEWELRY)[number]["id"];

export const BAGS = [
  { id: "none", label: "Sem bolsa" },
  { id: "crossbody", label: "Bolsinha", style: "FEMININE" },
  { id: "fanny", label: "Pochete", style: "MASCULINE" },
] as const;

export type Bag = (typeof BAGS)[number]["id"];

export const PAINTS = [
  { id: "none", label: "Sem pintura" },
  { id: "glitter", label: "Glitter", style: "FEMININE" },
  { id: "hearts", label: "Coraçõezinhos", style: "FEMININE" },
  { id: "stars", label: "Estrelinhas" },
] as const;

export type Paint = (typeof PAINTS)[number]["id"];

export const BEARDS = [
  { id: "none", label: "Sem barba" },
  { id: "stubble", label: "Por fazer", style: "MASCULINE" },
  { id: "goatee", label: "Cavanhaque", style: "MASCULINE" },
  { id: "mustache", label: "Bigode", style: "MASCULINE" },
  { id: "full", label: "Cheia", style: "MASCULINE" },
] as const;

export type Beard = (typeof BEARDS)[number]["id"];

export const EXPRESSIONS = [
  { id: "smile", label: "Sorrindo" },
  { id: "laugh", label: "Empolgado" },
  { id: "wink", label: "Piscando" },
  { id: "tongue", label: "Língua de fora" },
  { id: "love", label: "Apaixonado" },
  { id: "wow", label: "Uau!" },
] as const;

export type Expression = (typeof EXPRESSIONS)[number]["id"];

export const SKIN_TONES = ["#f8d9bf", "#eebb95", "#d39a6c", "#ad7148", "#7f4e30", "#573521"] as const;

export const EYE_COLORS = [
  { value: "#5b3a1e", label: "Castanhos" },
  { value: "#2b1c14", label: "Pretos" },
  { value: "#3f7d4f", label: "Verdes" },
  { value: "#3d7fb8", label: "Azuis" },
] as const;

export const HAIR_COLORS = [
  { value: "#262124", label: "Preto" },
  { value: "#55331f", label: "Castanho" },
  { value: "#a8451f", label: "Ruivo" },
  { value: "#dcb050", label: "Loiro" },
  { value: "#d9d8d4", label: "Platinado" },
  { value: "#e85a9b", label: "Rosa" },
  { value: "#7b4fd6", label: "Roxo" },
  { value: "#2fa8e0", label: "Azul" },
  { value: "#38c98a", label: "Verde" },
] as const;

export const CLOTH_COLORS = [
  { value: "#26262c", label: "Preto" },
  { value: "#f1f1ee", label: "Branco" },
  { value: "#4f78b8", label: "Jeans" },
  { value: "#e85a9b", label: "Rosa" },
  { value: "#b6e23a", label: "Verde limão" },
  { value: "#7b4fd6", label: "Roxo" },
  { value: "#f07a2c", label: "Laranja" },
  { value: "#d33a3a", label: "Vermelho" },
  { value: "#c9cdd4", label: "Prata" },
  { value: "#d9af28", label: "Ouro" },
] as const;

export type AvatarConfig = {
  style: AvatarStyle;
  skin: string;
  eyes: string;
  hairStyle: HairStyle;
  hairColor: string;
  beard: Beard;
  glasses: Glasses;
  hat: Hat;
  jewelry: Jewelry;
  bag: Bag;
  paint: Paint;
  expression: Expression;
  top: Top;
  topColor: string;
  bottom: Bottom;
  bottomColor: string;
  shoes: Shoes;
};

const BASE = {
  skin: SKIN_TONES[2] as string,
  eyes: EYE_COLORS[0].value as string,
  hat: "none",
  glasses: "none",
  jewelry: "none",
  bag: "none",
  paint: "none",
  beard: "none",
  expression: "laugh",
} as const;

export const DEFAULT_LOOKS: Record<AvatarStyle, AvatarConfig> = {
  FEMININE: {
    ...BASE,
    style: "FEMININE",
    hairStyle: "waves",
    hairColor: HAIR_COLORS[1].value,
    top: "cropped",
    topColor: CLOTH_COLORS[0].value,
    bottom: "pleated",
    bottomColor: CLOTH_COLORS[1].value,
    shoes: "boots",
  },
  MASCULINE: {
    ...BASE,
    style: "MASCULINE",
    hairStyle: "short",
    hairColor: HAIR_COLORS[1].value,
    top: "band",
    topColor: CLOTH_COLORS[0].value,
    bottom: "cargo",
    bottomColor: CLOTH_COLORS[8].value,
    shoes: "sneakers",
  },
};

export const DEFAULT_AVATAR = DEFAULT_LOOKS.MASCULINE;

/** Só o que combina com o estilo: opções sem estilo marcado servem para os dois. */
export function optionsFor<T extends string>(options: readonly Option<T>[], style: AvatarStyle): Option<T>[] {
  return options.filter((option) => !option.style || option.style === style);
}

export function isDress(top: Top): boolean {
  return TOPS.find((option) => option.id === top)?.piece === false;
}

function valid<T extends string>(options: readonly Option<T>[], value: unknown, style: AvatarStyle, fallback: T): T {
  return optionsFor(options, style).some((option) => option.id === value) ? (value as T) : fallback;
}

/** Troca de estilo: mantém pele, olhos, cor de cabelo e expressão, e veste o look padrão do novo estilo. */
export function switchStyle(config: AvatarConfig, style: AvatarStyle): AvatarConfig {
  if (config.style === style) return config;
  return { ...DEFAULT_LOOKS[style], skin: config.skin, eyes: config.eyes, hairColor: config.hairColor, expression: config.expression };
}

const LEGACY_TOPS: Record<string, Top> = { tee: "band", hoodie: "oversized", shirt: "openshirt", dress: "slip" };
const LEGACY_HAIR: Record<string, HairStyle> = { bun: "spacebuns" };

function str(value: unknown, fallback: string): string {
  return typeof value === "string" ? value : fallback;
}

/** Aceita avatares salvos em versões antigas e nunca deixa peça de um estilo no outro. */
export function normalizeAvatar(stored: Record<string, unknown> | null | undefined): AvatarConfig {
  const source = stored ?? {};
  const hairId = LEGACY_HAIR[source.hairStyle as string] ?? source.hairStyle;
  const hairStyle = HAIR_STYLES.find((option) => option.id === hairId);
  const style: AvatarStyle = source.style === "FEMININE" || source.style === "MASCULINE" ? source.style : (hairStyle?.style ?? DEFAULT_AVATAR.style);
  const look = DEFAULT_LOOKS[style];
  const topId = LEGACY_TOPS[source.outfit as string] ?? source.outfit ?? source.top;

  return {
    style,
    skin: str(source.skin, look.skin),
    eyes: str(source.eyes, look.eyes),
    hairStyle: valid(HAIR_STYLES, hairId, style, look.hairStyle),
    hairColor: str(source.hairColor, look.hairColor),
    beard: valid(BEARDS, source.beard, style, "none"),
    glasses: valid(GLASSES, source.glasses, style, "none"),
    hat: valid(HATS, source.hat, style, "none"),
    jewelry: valid(JEWELRY, source.jewelry, style, "none"),
    bag: valid(BAGS, source.bag, style, "none"),
    paint: valid(PAINTS, source.paint, style, "none"),
    expression: valid(EXPRESSIONS, source.expression, style, look.expression),
    top: valid(TOPS, topId, style, look.top),
    topColor: str(source.topColor ?? source.outfitColor, look.topColor),
    bottom: valid(BOTTOMS, source.bottom, style, look.bottom),
    bottomColor: str(source.bottomColor, look.bottomColor),
    shoes: valid(SHOES, source.shoes, style, look.shoes),
  };
}

function pick<T>(items: readonly T[], random: () => number): T {
  return items[Math.floor(random() * items.length)];
}

/** Para o botão "Surpreenda-me". Acessórios aparecem de vez em quando, para não virar fantasia. */
export function randomAvatar(random: () => number = Math.random, chosen?: AvatarStyle): AvatarConfig {
  const style: AvatarStyle = chosen ?? (random() < 0.5 ? "FEMININE" : "MASCULINE");
  const maybe = <T extends string>(options: readonly Option<T>[], chance: number, none: T): T => (random() < chance ? pick(optionsFor(options, style), random).id : none);
  return {
    style,
    skin: pick(SKIN_TONES, random),
    eyes: pick(EYE_COLORS, random).value,
    hairStyle: pick(optionsFor(HAIR_STYLES, style), random).id,
    hairColor: pick(HAIR_COLORS, random).value,
    beard: maybe(BEARDS, 0.3, "none"),
    glasses: maybe(GLASSES, 0.35, "none"),
    hat: maybe(HATS, 0.25, "none"),
    jewelry: maybe(JEWELRY, 0.45, "none"),
    bag: maybe(BAGS, 0.25, "none"),
    paint: maybe(PAINTS, 0.3, "none"),
    expression: pick(EXPRESSIONS, random).id,
    top: pick(optionsFor(TOPS, style), random).id,
    topColor: pick(CLOTH_COLORS, random).value,
    bottom: pick(optionsFor(BOTTOMS, style), random).id,
    bottomColor: pick(CLOTH_COLORS, random).value,
    shoes: pick(optionsFor(SHOES, style), random).id,
  };
}
