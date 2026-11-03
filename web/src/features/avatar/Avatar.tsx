import "./avatar.css";

import { useId, type ReactNode } from "react";
import { readableInk } from "../../design-system";
import { isDress, normalizeAvatar, type AvatarConfig } from "./avatarOptions";

/* Bonequinho em SVG com cara de brinquedo: volume por gradientes e sombras suaves, cabelo com mechas,
   olhos com íris e tecido com costura e dobras. Sem imagens. A cabeça continua grande de propósito. */

const INK = "#2b2022";
const HEAD = "M67 150C67 86 116 50 175 50C234 50 283 86 283 150C283 214 234 252 175 252C116 252 67 214 67 150Z";
const CAP = "M60 160C50 82 110 36 175 36C240 36 300 82 290 160C276 112 240 94 175 94C110 94 74 112 60 160Z";

function mix(hex: string, target: string, amount: number): string {
  const channel = (value: string, start: number) => parseInt(value.slice(start, start + 2), 16);
  const blend = (start: number) => Math.round(channel(hex, start) + (channel(target, start) - channel(hex, start)) * amount);
  return `#${[1, 3, 5].map((start) => blend(start).toString(16).padStart(2, "0")).join("")}`;
}
const light = (hex: string, amount = 0.28) => mix(hex, "#ffffff", amount);
const dark = (hex: string, amount = 0.26) => mix(hex, "#000000", amount);

/** Dá ao tecido preto um pouco de brilho e ao branco alguma sombra: sem isso, as dobras somem. */
function clothRamp(color: string) {
  return { high: light(color, 0.2), mid: color, low: dark(color, 0.3), seam: mix(color, readableInk(color), 0.45) };
}

type Ids = { url: (name: string) => string; prefix: string };

function Defs({ ids, config }: { ids: Ids; config: AvatarConfig }) {
  const top = clothRamp(config.topColor);
  const bottom = clothRamp(config.bottomColor);
  const p = ids.prefix;
  const cloth = (name: string, ramp: ReturnType<typeof clothRamp>) => (
    <linearGradient id={`${p}${name}`} x1="0" x2="1" y1="0" y2="0.25">
      <stop offset="0" stopColor={ramp.high} />
      <stop offset="0.5" stopColor={ramp.mid} />
      <stop offset="1" stopColor={ramp.low} />
    </linearGradient>
  );
  return (
    <defs>
      <radialGradient id={`${p}skin`} cx="0.4" cy="0.32" r="0.78">
        <stop offset="0" stopColor={light(config.skin, 0.22)} />
        <stop offset="0.6" stopColor={config.skin} />
        <stop offset="1" stopColor={dark(config.skin, 0.2)} />
      </radialGradient>
      <radialGradient id={`${p}hair`} cx="0.38" cy="0.22" r="0.85">
        <stop offset="0" stopColor={light(config.hairColor, 0.3)} />
        <stop offset="0.5" stopColor={config.hairColor} />
        <stop offset="1" stopColor={dark(config.hairColor, 0.35)} />
      </radialGradient>
      <radialGradient id={`${p}iris`} cx="0.5" cy="0.62" r="0.6">
        <stop offset="0" stopColor={light(config.eyes, 0.5)} />
        <stop offset="1" stopColor={dark(config.eyes, 0.25)} />
      </radialGradient>
      <linearGradient id={`${p}gold`} x1="0" x2="1" y1="0" y2="1">
        <stop offset="0" stopColor="#ffe27a" />
        <stop offset="0.5" stopColor="#ffcf33" />
        <stop offset="1" stopColor="#d99a06" />
      </linearGradient>
      <clipPath id={`${p}head-clip`}><path d={HEAD} /></clipPath>
      {cloth("top", top)}
      {cloth("bottom", bottom)}
      <filter id={`${p}soft`} x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="3" /></filter>
      <filter id={`${p}softer`} x="-40%" y="-40%" width="180%" height="180%"><feGaussianBlur stdDeviation="8" /></filter>
    </defs>
  );
}

/** Braço ou perna com volume: lado escuro, cor e brilho no alto. */
function Limb({ d, color, width, ids }: { d: string; color: string; width: number; ids: Ids }) {
  return (
    <g fill="none" strokeLinecap="round" strokeLinejoin="round">
      <path d={d} stroke={dark(color, 0.22)} strokeWidth={width} />
      <path d={d} stroke={color} strokeWidth={width - 5} transform="translate(-1.4 -1.4)" />
      <path d={d} stroke={light(color, 0.35)} strokeWidth={width * 0.28} opacity="0.6" transform={`translate(${-width * 0.17} ${-width * 0.17})`} filter={ids.url("soft")} />
    </g>
  );
}

function curls(points: [number, number, number][], fill: string) {
  return points.map(([cx, cy, r]) => <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r={r} fill={fill} />);
}

type HairShape = { back?: (fill: string) => ReactNode; front?: (fill: string) => ReactNode; strands?: string[] };

const HAIR: Record<AvatarConfig["hairStyle"], HairShape> = {
  long: {
    back: (fill) => <path fill={fill} d="M48 150C48 58 110 26 175 26C240 26 302 58 302 150L314 352C314 372 272 372 268 352L262 240L88 240L82 352C78 372 36 372 36 352Z" />,
    front: (fill) => <path fill={fill} d="M60 164C52 80 110 36 175 36C240 36 298 80 290 164C278 120 248 96 208 86C178 114 110 124 60 164Z" />,
    strands: ["M72 150C66 220 58 280 54 350", "M284 150C292 220 298 280 302 350", "M84 200L74 340", "M270 200L284 340", "M120 62C100 80 84 104 74 132", "M204 54C234 62 262 84 276 120"],
  },
  waves: {
    back: (fill) => (
      <>
        <path fill={fill} d="M44 150C44 56 108 24 175 24C242 24 306 56 306 150L300 330L50 330Z" />
        {curls([[46, 190, 36], [36, 244, 38], [44, 300, 38], [64, 346, 34], [304, 190, 36], [314, 244, 38], [306, 300, 38], [286, 346, 34]], fill)}
      </>
    ),
    front: (fill) => <path fill={fill} d="M60 164C50 78 110 34 178 34C246 34 300 80 290 164C280 124 262 104 232 92C206 118 170 106 134 102C106 112 76 132 60 164Z" />,
    strands: ["M36 220Q56 244 30 270", "M40 280Q64 304 44 336", "M314 220Q294 244 320 270", "M310 280Q286 304 306 336", "M118 62C98 78 82 102 74 130", "M212 50C238 60 262 82 276 116"],
  },
  bob: {
    back: (fill) => <path fill={fill} d="M46 150C46 58 108 28 175 28C242 28 304 58 304 150L312 268C312 294 266 296 260 270L256 200L94 200L90 270C84 296 38 294 38 268Z" />,
    front: (fill) => <path fill={fill} d="M62 146C60 78 112 38 175 38C238 38 290 78 288 146C262 122 88 122 62 146Z" />,
    strands: ["M56 170L46 268", "M294 170L304 268", "M100 76L92 128", "M136 56L128 122", "M175 50V120", "M214 56L222 122", "M250 76L258 128"],
  },
  spacebuns: {
    back: (fill) => curls([[78, 52, 44], [272, 52, 44]], fill),
    front: (fill) => <path fill={fill} d={CAP} />,
    strands: ["M60 40Q78 30 96 46Q78 58 64 56", "M254 46Q272 30 290 40Q286 56 272 58", "M175 40V92", "M136 46Q118 64 92 104", "M214 46Q232 64 258 104"],
  },
  braids: {
    back: (fill) => (
      <>
        <path fill={fill} d="M50 150C50 58 110 28 175 28C240 28 300 58 300 150V230H50Z" />
        {curls([[52, 238, 25], [44, 280, 23], [40, 320, 21], [40, 356, 18], [298, 238, 25], [306, 280, 23], [310, 320, 21], [310, 356, 18]], fill)}
      </>
    ),
    front: (fill) => <path fill={fill} d="M60 160C52 78 110 36 175 36C240 36 298 78 290 160C276 118 214 86 175 84C136 86 74 118 60 160Z" />,
    strands: ["M175 38V84", "M136 48Q100 76 78 124", "M214 48Q250 76 272 124", "M32 260L60 258", "M28 300L54 298", "M322 260L294 258", "M326 300L300 298"],
  },
  afro: {
    back: (fill) => <circle fill={fill} cx="175" cy="124" r="150" />,
    front: (fill) => <path fill={fill} d="M66 140C66 84 116 52 175 52C234 52 284 84 284 140C256 106 94 106 66 140Z" />,
  },
  short: {
    front: (fill) => <path fill={fill} d="M58 156C44 70 108 26 178 26C250 26 306 74 292 156C284 118 262 100 236 96C196 110 130 104 100 110C82 120 66 134 58 156Z" />,
    strands: ["M96 58C120 44 156 38 190 40", "M90 86C130 70 190 66 240 74", "M214 44C244 56 268 78 278 112", "M78 120C84 104 92 96 104 90"],
  },
  quiff: {
    front: (fill) => <path fill={fill} d="M58 154C44 90 74 8 192 2C282 -2 316 80 292 154C284 116 262 98 236 92C190 84 130 94 100 106C82 116 66 132 58 154Z" />,
    strands: ["M90 72C116 38 160 20 214 22", "M84 96C120 60 176 44 240 50", "M104 100C140 76 190 66 262 78", "M250 32C278 54 292 84 288 116"],
  },
  mullet: {
    back: (fill) => <path fill={fill} d="M50 150C50 60 110 30 175 30C240 30 300 60 300 150L318 300C322 328 280 330 270 310L262 240L88 240L80 310C70 330 28 328 32 300Z" />,
    front: (fill) => <path fill={fill} d="M58 156C46 74 108 28 178 28C250 28 304 76 292 156C286 124 276 108 262 100L250 124L232 96L212 122L192 94L170 120L150 96L130 122L112 102C86 112 68 132 58 156Z" />,
    strands: ["M44 230L38 306", "M60 240L56 318", "M306 230L312 306", "M290 240L294 318", "M96 62C124 44 170 38 214 44"],
  },
  curly: {
    front: (fill) => (
      <>
        <path fill={fill} d={CAP} />
        {curls([[74, 112, 32], [104, 70, 34], [146, 44, 36], [204, 44, 36], [246, 70, 34], [276, 112, 32], [175, 66, 30]], fill)}
      </>
    ),
  },
  mohawk: {
    front: (fill) => (
      <>
        <path fill={fill} d={CAP} opacity="0.28" />
        <path fill={fill} d="M136 74L120 8L152 34L175 -18L198 34L230 8L214 74C190 62 160 62 136 74Z" />
      </>
    ),
    strands: ["M150 66L134 22", "M175 58V-6", "M200 66L216 22"],
  },
  buzz: {
    front: (fill) => <path fill={fill} d="M64 136C68 80 116 44 175 44C234 44 282 80 286 136C262 96 222 84 175 84C128 84 88 96 64 136Z" opacity="0.78" />,
  },
  bald: {},
};

function Eye({ cx, config, side, ids }: { cx: number; config: AvatarConfig; side: "left" | "right"; ids: Ids }) {
  const cy = 165;
  const { expression, style } = config;
  if (expression === "love") {
    return <path d={`M${cx} ${cy + 22}C${cx - 36} ${cy} ${cx - 18} ${cy - 28} ${cx} ${cy - 8}C${cx + 18} ${cy - 28} ${cx + 36} ${cy} ${cx} ${cy + 22}Z`} fill="#e23b6d" stroke="#b81f50" strokeWidth="2" />;
  }
  if (expression === "laugh" || (expression === "wink" && side === "right")) {
    return (
      <g fill="none" stroke={INK} strokeLinecap="round">
        <path d={`M${cx - 22} ${cy + 6}Q${cx} ${cy - 20} ${cx + 22} ${cy + 6}`} strokeWidth="7" />
        {style === "FEMININE" && <path d={`M${cx + (side === "left" ? -22 : 22)} ${cy + 6}l${side === "left" ? -9 : 9} -6`} strokeWidth="4" />}
      </g>
    );
  }
  const wide = expression === "wow" ? 3 : 0;
  const outer = side === "left" ? -1 : 1;
  return (
    <g>
      <ellipse cx={cx} cy={cy} rx={23 + wide} ry={22 + wide} fill="#fff" />
      <ellipse cx={cx} cy={cy + 1} rx={23 + wide} ry={22 + wide} fill="none" stroke="rgb(0 0 0 / 0.12)" strokeWidth="2" />
      <circle cx={cx} cy={cy + 2} r={16 + wide / 2} fill={ids.url("iris")} />
      <circle cx={cx} cy={cy + 2} r={16 + wide / 2} fill="none" stroke={dark(config.eyes, 0.45)} strokeWidth="2" />
      <circle cx={cx} cy={cy + 2} r={8.5} fill="#120c0e" />
      <circle cx={cx + 6} cy={cy - 6} r="5.5" fill="#fff" />
      <circle cx={cx - 5} cy={cy + 8} r="2.5" fill="#fff" opacity="0.85" />
      <path d={`M${cx - 25} ${cy - 2}C${cx - 18} ${cy - 26} ${cx + 18} ${cy - 26} ${cx + 25} ${cy - 2}`} fill="none" stroke={INK} strokeWidth={style === "FEMININE" ? 6 : 4} strokeLinecap="round" />
      {style === "FEMININE" && (
        <g stroke={INK} strokeWidth="3.5" strokeLinecap="round">
          <path d={`M${cx + outer * 25} ${cy - 3}l${outer * 10} -7`} />
          <path d={`M${cx + outer * 21} ${cy - 12}l${outer * 8} -8`} />
          <path d={`M${cx + outer * 14} ${cy - 19}l${outer * 5} -8`} />
        </g>
      )}
    </g>
  );
}

function Mouth({ config }: { config: AvatarConfig }) {
  const fem = config.style === "FEMININE";
  const lip = fem ? "#d8395f" : mix(config.skin, "#a0443f", 0.55);
  switch (config.expression) {
    case "laugh":
      return (
        <g>
          <path d="M144 202Q175 208 206 202Q204 242 175 242Q146 242 144 202Z" fill="#561a22" stroke={lip} strokeWidth={fem ? 5 : 3} strokeLinejoin="round" />
          <path d="M150 205Q175 210 200 205L198 215Q175 219 152 215Z" fill="#fff" />
          <ellipse cx="175" cy="231" rx="14" ry="9" fill="#e8626f" />
        </g>
      );
    case "tongue":
      return (
        <g>
          <path d="M150 204Q175 222 200 204" fill="none" stroke={lip} strokeWidth={fem ? 7 : 5} strokeLinecap="round" />
          <path d="M163 212H191V228A14 14 0 0 1 163 228Z" fill="#e8626f" />
          <path d="M177 214V230" stroke="#c9485a" strokeWidth="2.5" strokeLinecap="round" />
        </g>
      );
    case "wow":
      return <ellipse cx="175" cy="216" rx="14" ry="18" fill="#561a22" stroke={lip} strokeWidth={fem ? 5 : 3} />;
    default:
      return fem ? (
        <g>
          <path d="M152 206Q163 198 175 203Q187 198 198 206Q187 222 175 222Q163 222 152 206Z" fill={lip} />
          <path d="M152 206Q175 212 198 206" fill="none" stroke={dark(lip, 0.35)} strokeWidth="2.5" />
          <ellipse cx="172" cy="215" rx="8" ry="2.5" fill="#fff" opacity="0.4" />
        </g>
      ) : (
        <g>
          <path d="M152 204Q175 226 198 204" fill="none" stroke={lip} strokeWidth="6" strokeLinecap="round" />
          <path d="M160 226Q175 232 190 226" fill="none" stroke="rgb(0 0 0 / 0.14)" strokeWidth="3" strokeLinecap="round" />
        </g>
      );
  }
}

function FacePaint({ paint }: { paint: AvatarConfig["paint"] }) {
  const star = (x: number, y: number, s: number) => `M${x} ${y - s}L${x + s * 0.3} ${y - s * 0.3}L${x + s} ${y - s * 0.2}L${x + s * 0.45} ${y + s * 0.25}L${x + s * 0.6} ${y + s}L${x} ${y + s * 0.55}L${x - s * 0.6} ${y + s}L${x - s * 0.45} ${y + s * 0.25}L${x - s} ${y - s * 0.2}L${x - s * 0.3} ${y - s * 0.3}Z`;
  if (paint === "glitter") {
    return (
      <g fill="#ffe27a" stroke="#fff" strokeWidth="0.6">
        {[[88, 192], [104, 204], [82, 210], [118, 216], [98, 224], [262, 192], [246, 204], [268, 210], [232, 216], [252, 224]].map(([cx, cy], index) => (
          <path key={index} d={`M${cx} ${cy - 7}L${cx + 2} ${cy - 2}L${cx + 7} ${cy}L${cx + 2} ${cy + 2}L${cx} ${cy + 7}L${cx - 2} ${cy + 2}L${cx - 7} ${cy}L${cx - 2} ${cy - 2}Z`} />
        ))}
      </g>
    );
  }
  if (paint === "stars") return <path d={`${star(94, 210, 13)}${star(116, 226, 7)}${star(256, 210, 13)}${star(234, 226, 7)}`} fill="#3ec6e0" stroke="#fff" strokeWidth="1" />;
  if (paint === "hearts") {
    const heart = (x: number, y: number, s: number) => `M${x} ${y + s}C${x - s * 1.6} ${y} ${x - s * 0.7} ${y - s} ${x} ${y - s * 0.2}C${x + s * 0.7} ${y - s} ${x + s * 1.6} ${y} ${x} ${y + s}Z`;
    return <path d={`${heart(96, 212, 11)}${heart(254, 212, 11)}`} fill="#e23b6d" stroke="#fff" strokeWidth="1" />;
  }
  return null;
}

function BeardLayer({ config, ids }: { config: AvatarConfig; ids: Ids }) {
  const fill = ids.url("hair");
  switch (config.beard) {
    case "none":
      return null;
    case "stubble":
      return <path d="M72 186C82 232 124 256 175 256C226 256 268 232 278 186C258 208 232 208 214 200Q175 190 136 200C118 208 92 208 72 186Z" fill={config.hairColor} opacity="0.3" />;
    case "goatee":
      return <path d="M150 230Q175 222 200 230C200 252 188 270 175 270C162 270 150 252 150 230Z" fill={fill} />;
    case "mustache":
      return <path d="M136 200Q156 178 175 192Q194 178 214 200Q194 208 175 198Q156 208 136 200Z" fill={fill} />;
    case "full":
      return (
        <g>
          <path d="M66 172C70 240 120 272 175 272C230 272 280 240 284 172C268 202 234 204 212 196Q175 186 138 196C116 204 82 202 66 172ZM150 208Q175 214 200 208Q196 240 175 240Q154 240 150 208Z" fillRule="evenodd" fill={fill} />
          <path d="M92 214L100 238M120 228L126 252M230 228L224 252M258 214L250 238" stroke={dark(config.hairColor, 0.4)} strokeWidth="3" strokeLinecap="round" />
        </g>
      );
  }
}

function GlassesLayer({ glasses }: { glasses: AvatarConfig["glasses"] }) {
  const lens = "rgb(255 255 255 / 0.16)";
  switch (glasses) {
    case "none":
      return null;
    case "cat":
      return (
        <g fill={lens} stroke="#c2185b" strokeWidth="6" strokeLinejoin="round">
          <path d="M92 146Q128 136 164 150Q164 192 128 194Q98 192 92 146ZM78 136L92 146" />
          <path d="M258 146Q222 136 186 150Q186 192 222 194Q252 192 258 146ZM272 136L258 146" />
          <path d="M164 156Q175 150 186 156" fill="none" />
        </g>
      );
    case "heart": {
      const heart = (x: number) => `M${x} 204C${x - 62} 170 ${x - 40} 122 ${x} 148C${x + 40} 122 ${x + 62} 170 ${x} 204Z`;
      return (
        <g>
          <path d={`${heart(128)}${heart(222)}`} fill="#e23b6d" stroke="#b81f50" strokeWidth="5" />
          <path d="M100 152Q108 140 118 148M194 152Q202 140 212 148" fill="none" stroke="#fff" strokeWidth="4" strokeLinecap="round" opacity="0.7" />
          <rect x="160" y="154" width="30" height="6" fill="#b81f50" />
        </g>
      );
    }
    case "aviator":
      return (
        <g fill="#3b2a18" fillOpacity="0.72" stroke="#c9a23a" strokeWidth="4.5">
          <path d="M90 146Q128 138 166 146Q168 196 128 198Q92 194 90 146Z" />
          <path d="M260 146Q222 138 184 146Q182 196 222 198Q258 194 260 146Z" />
          <path d="M166 150Q175 144 184 150M168 142H182" fill="none" />
          <path d="M104 160L130 158M200 158L226 160" stroke="#fff" strokeWidth="4" strokeLinecap="round" opacity="0.5" fill="none" />
        </g>
      );
    case "square":
      return (
        <g fill={lens} stroke="#1f1f24" strokeWidth="7">
          <rect x="92" y="138" width="74" height="54" rx="9" />
          <rect x="184" y="138" width="74" height="54" rx="9" />
          <path d="M166 158Q175 152 184 158" fill="none" />
        </g>
      );
    case "sun":
      return (
        <g fill="#16161a">
          <path d="M84 142H166Q172 142 171 150C168 182 152 196 128 196C102 196 84 176 80 150Q79 142 84 142Z" />
          <path d="M184 142H266Q272 142 270 150C266 176 248 196 222 196C198 196 182 182 179 150Q178 142 184 142Z" />
          <rect x="164" y="146" width="22" height="7" />
          <path d="M100 156L128 156M200 156L228 156" stroke="#fff" strokeWidth="5" strokeLinecap="round" opacity="0.45" />
        </g>
      );
  }
}

function HatLayer({ config, ids }: { config: AvatarConfig; ids: Ids }) {
  const ramp = clothRamp(config.topColor);
  const fill = ids.url("top");
  switch (config.hat) {
    case "none":
      return null;
    case "bucket":
      return (
        <g>
          <path d="M84 96C88 44 126 16 175 16C224 16 262 44 266 96Z" fill={fill} />
          <path d="M84 96H266L298 134Q175 112 52 134Z" fill={ramp.low} />
          <path d="M92 86H258M60 128Q175 106 290 128" fill="none" stroke={ramp.seam} strokeWidth="2.5" strokeDasharray="7 6" />
          <circle cx="110" cy="58" r="3" fill={ramp.seam} /><circle cx="240" cy="58" r="3" fill={ramp.seam} />
        </g>
      );
    case "cap":
      return (
        <g>
          <path d="M72 112C74 52 122 22 175 22C228 22 276 52 278 112Z" fill={fill} />
          <path d="M175 22V100M120 34Q104 70 108 106M230 34Q246 70 242 106" fill="none" stroke={ramp.low} strokeWidth="2.5" />
          <path d="M150 106C210 94 280 96 322 124C326 136 316 140 306 136C262 122 208 118 150 124Z" fill={ramp.low} />
          <path d="M160 112C210 104 270 106 312 126" fill="none" stroke={ramp.seam} strokeWidth="2" strokeDasharray="5 5" />
          <circle cx="175" cy="24" r="7" fill={ramp.low} />
        </g>
      );
    case "bandana":
      return (
        <g>
          <path d="M62 128C74 76 124 52 175 52C226 52 276 76 288 128C240 100 110 100 62 128Z" fill={fill} />
          <path d="M280 112L322 92L316 126L330 150L286 132Z" fill={ramp.low} />
          {[[120, 86], [175, 74], [230, 86], [150, 100], [205, 100], [95, 104], [255, 104]].map(([cx, cy]) => (
            <g key={`${cx}-${cy}`} fill="none" stroke={ramp.seam} strokeWidth="2"><circle cx={cx} cy={cy} r="5" /></g>
          ))}
        </g>
      );
    case "cowboy":
      return (
        <g>
          <path d="M16 118Q60 138 175 134Q290 138 334 118Q322 158 175 158Q28 158 16 118Z" fill={ramp.low} />
          <path d="M92 132C86 60 110 16 142 18Q175 30 208 18C240 16 264 60 258 132Q175 146 92 132Z" fill={fill} />
          <path d="M92 118Q175 134 258 118V134Q175 148 92 134Z" fill={ramp.seam} />
          <path d="M142 18Q175 44 208 18" fill="none" stroke={ramp.low} strokeWidth="4" />
        </g>
      );
    case "bow":
      return (
        <g transform="translate(232 62) rotate(14)">
          <path d="M0 0L-46 -26Q-54 0 -46 26Z" fill={fill} />
          <path d="M0 0L46 -26Q54 0 46 26Z" fill={ramp.low} />
          <path d="M-34 -14L-14 -4M34 -14L14 -4" stroke={ramp.seam} strokeWidth="2.5" strokeLinecap="round" />
          <circle r="11" fill={ramp.high} />
        </g>
      );
    case "flowers":
      return (
        <g>
          {([[88, 92, "#ff7fb5"], [126, 62, "#ffd24d"], [175, 50, "#ff8a4d"], [224, 62, "#c78bff"], [262, 92, "#7fe0c3"]] as const).map(([cx, cy, color]) => (
            <g key={color}>
              {[[0, -13], [13, 0], [0, 13], [-13, 0]].map(([dx, dy]) => (
                <circle key={`${dx}-${dy}`} cx={cx + dx} cy={cy + dy} r="11.5" fill={color} stroke={dark(color, 0.18)} strokeWidth="1.5" />
              ))}
              <circle cx={cx} cy={cy} r="7" fill="#fff5c7" stroke="#e9c655" strokeWidth="1.5" />
            </g>
          ))}
        </g>
      );
  }
}

function Denim({ d }: { d: string }) {
  return <path d={d} fill="none" stroke="#fff" strokeWidth="2" strokeDasharray="5 4" opacity="0.55" />;
}

function BottomLayer({ config, ids }: { config: AvatarConfig; ids: Ids }) {
  const ramp = clothRamp(config.bottomColor);
  const fill = ids.url("bottom");
  const folds = (d: string) => <path d={d} fill="none" stroke={ramp.low} strokeWidth="4" strokeLinecap="round" opacity="0.7" filter={ids.url("soft")} />;
  const waist = <path d="M130 326H220V338H130Z" fill={ramp.low} />;
  switch (config.bottom) {
    case "mini":
      return (
        <g>
          <path d="M134 326H216L232 390H118Z" fill={fill} />
          {waist}
          {folds("M146 342L136 386M204 342L214 386")}
          <Denim d="M120 384H230" />
        </g>
      );
    case "pleated":
      return (
        <g>
          <path d="M134 326H216L240 398L224 390L208 400L191 390L175 400L159 390L142 400L126 390L110 398Z" fill={fill} />
          {waist}
          {[148, 161, 175, 189, 202].map((x, index) => <path key={x} d={`M${x} 338L${x + (index - 2) * 9} 396`} stroke={ramp.low} strokeWidth="3.5" />)}
        </g>
      );
    case "shorts":
      return (
        <g>
          <path d="M134 326H216L226 396H182L175 362L168 396H124Z" fill={fill} />
          {waist}
          <Denim d="M124 390H168M182 390H226M175 340V362" />
          <path d="M124 396L128 406M138 396L140 408M152 396L153 405M198 396L197 405M212 396L210 408" stroke={ramp.high} strokeWidth="3" strokeLinecap="round" />
        </g>
      );
    case "wide":
      return (
        <g>
          <path d="M134 326H216L252 472H186L175 370L164 472H98Z" fill={fill} />
          {waist}
          {folds("M126 370L108 468M146 372L134 470M204 372L216 470M224 370L242 468")}
        </g>
      );
    case "bermuda":
      return (
        <g>
          <path d="M128 326H222L234 424H184L175 366L166 424H116Z" fill={fill} />
          {waist}
          {folds("M138 350L128 420M212 350L222 420")}
          <path d="M116 418H166M184 418H234" stroke={ramp.seam} strokeWidth="2" strokeDasharray="5 4" />
        </g>
      );
    case "cargoshorts":
      return (
        <g>
          <path d="M128 326H222L236 428H184L175 366L166 428H114Z" fill={fill} />
          {waist}
          <rect x="112" y="374" width="34" height="36" rx="3" fill={ramp.mid} stroke={ramp.low} strokeWidth="3" />
          <rect x="204" y="374" width="34" height="36" rx="3" fill={ramp.mid} stroke={ramp.low} strokeWidth="3" />
          <path d="M112 386H146M204 386H238" stroke={ramp.low} strokeWidth="3" />
        </g>
      );
    case "cargo":
      return (
        <g>
          <path d="M128 326H222L236 470H186L175 372L164 470H114Z" fill={fill} />
          {waist}
          <rect x="108" y="396" width="34" height="38" rx="3" fill={ramp.mid} stroke={ramp.low} strokeWidth="3" />
          <rect x="208" y="396" width="34" height="38" rx="3" fill={ramp.mid} stroke={ramp.low} strokeWidth="3" />
          <path d="M108 408H142M208 408H242M118 448L160 452M232 448L190 452" stroke={ramp.low} strokeWidth="3" />
        </g>
      );
    case "baggy":
      return (
        <g>
          <path d="M128 326H222L248 472H188L175 372L162 472H102Z" fill={fill} />
          {waist}
          <Denim d="M136 336L120 468M214 336L230 468" />
          <path d="M122 412L152 406M124 420L148 416M204 432L234 426M206 440L230 436" stroke="#fff" strokeWidth="4" strokeLinecap="round" opacity="0.85" />
          {folds("M130 456L160 450M220 456L190 450")}
        </g>
      );
    case "jogger":
      return (
        <g>
          <path d="M128 326H222L224 448H188L175 372L162 448H126Z" fill={fill} />
          {waist}
          <path d="M126 448H162V466H128ZM188 448H224L222 466H188Z" fill={ramp.low} />
          <path d="M152 340L150 390M198 340L200 390" stroke={ramp.seam} strokeWidth="3" />
          {folds("M138 400L156 410M212 400L194 410")}
        </g>
      );
  }
}

function TopLayer({ config, ids }: { config: AvatarConfig; ids: Ids }) {
  const ramp = clothRamp(config.topColor);
  const fill = ids.url("top");
  const skin = ids.url("skin");
  const ink = readableInk(config.topColor);
  const folds = (d: string) => <path d={d} fill="none" stroke={ramp.low} strokeWidth="4" strokeLinecap="round" opacity="0.7" filter={ids.url("soft")} />;
  const seam = (d: string) => <path d={d} fill="none" stroke={ramp.seam} strokeWidth="2" strokeDasharray="4 4" opacity="0.8" />;

  switch (config.top) {
    case "cropped":
      return (
        <g>
          <path d="M141 268Q175 258 209 268Q214 292 208 318H142Q136 292 141 268Z" fill={fill} />
          <path d="M156 264Q175 286 194 264" fill={skin} />
          <path d="M156 264Q175 286 194 264" fill="none" stroke={ramp.low} strokeWidth="4" />
          <path d="M142 310H208" stroke={ramp.low} strokeWidth="9" />
          {seam("M144 304H206")}
          {folds("M152 288L148 306M198 288L202 306")}
        </g>
      );
    case "blouse":
      return (
        <g>
          <path d="M140 286H210Q216 306 208 322L214 344H136L142 322Q134 306 140 286Z" fill={fill} />
          <path d="M128 286Q134 276 142 284Q150 274 158 284Q166 274 175 284Q184 274 192 284Q200 274 208 284Q216 276 222 286Q224 304 210 300Q200 310 192 300Q184 310 175 300Q166 310 158 300Q150 310 140 300Q126 304 128 286Z" fill={ramp.high} stroke={ramp.low} strokeWidth="2" />
          <path d="M136 344Q156 352 175 344Q194 352 214 344L216 352H134Z" fill={ramp.low} />
          {folds("M154 310L150 340M196 310L200 340")}
        </g>
      );
    case "bralette":
      return (
        <g>
          <path d="M152 262L148 282M198 262L202 282" stroke={ramp.low} strokeWidth="5" strokeLinecap="round" />
          <path d="M138 284Q156 268 175 290Q194 268 212 284L212 308H138Z" fill={fill} />
          <path d="M138 308H212" stroke={ramp.low} strokeWidth="6" />
          <path d="M175 290V308" stroke={ramp.low} strokeWidth="3" />
          {[148, 158, 192, 202].map((x) => <circle key={x} cx={x} cy="296" r="2.5" fill={ramp.seam} />)}
        </g>
      );
    case "corset":
      return (
        <g>
          <path d="M140 278Q156 266 175 284Q194 266 210 278Q216 300 206 320L216 346L175 358L134 346L144 320Q134 300 140 278Z" fill={fill} />
          {[150, 162, 188, 200].map((x) => <path key={x} d={`M${x} 280V348`} stroke={ramp.low} strokeWidth="3" />)}
          <path d="M175 286V356" stroke={ramp.seam} strokeWidth="2" />
          {[298, 312, 326, 340].map((y) => <path key={y} d={`M167 ${y}L183 ${y + 9}M183 ${y}L167 ${y + 9}`} stroke={ramp.seam} strokeWidth="2.5" />)}
          <path d="M140 278Q156 266 175 284Q194 266 210 278" fill="none" stroke={ramp.high} strokeWidth="3" />
        </g>
      );
    case "slip":
      return (
        <g>
          <path d="M150 262L146 286M200 262L204 286" stroke={ramp.low} strokeWidth="4.5" strokeLinecap="round" />
          <path d="M138 286Q156 272 175 292Q194 272 212 286Q218 306 208 324L246 420H104L142 324Q132 306 138 286Z" fill={fill} />
          {folds("M146 340L122 414M166 340L156 416M186 340L194 416M204 340L228 414")}
          <path d="M104 420H246" stroke={ramp.low} strokeWidth="4" />
          <path d="M138 286Q156 272 175 292Q194 272 212 286" fill="none" stroke={ramp.high} strokeWidth="2.5" />
        </g>
      );
    case "fringe":
      return (
        <g>
          <path d="M150 262L146 286M200 262L204 286" stroke={ramp.low} strokeWidth="4.5" strokeLinecap="round" />
          <path d="M138 286Q156 272 175 292Q194 272 212 286Q218 306 208 324L224 384H126L142 324Q132 306 138 286Z" fill={fill} />
          {[330, 356].map((y) => (
            <g key={y}>
              {Array.from({ length: y === 330 ? 9 : 12 }, (_, index) => {
                const count = y === 330 ? 9 : 12;
                const width = y === 330 ? 66 : 92;
                const x = 175 - width / 2 + (width / (count - 1)) * index;
                return <path key={x} d={`M${x} ${y}V${y + 30}`} stroke={index % 2 ? ramp.high : ramp.low} strokeWidth="3.5" strokeLinecap="round" />;
              })}
            </g>
          ))}
          {Array.from({ length: 14 }, (_, index) => {
            const x = 122 + index * 8.1;
            return <path key={x} d={`M${x} 382V${index % 2 ? 424 : 414}`} stroke={index % 2 ? ramp.high : ramp.mid} strokeWidth="4.5" strokeLinecap="round" />;
          })}
        </g>
      );
    case "tank":
      return (
        <g>
          <path d="M144 262H158Q175 298 192 262H206L212 300Q214 320 222 348H128Q136 320 138 300Z" fill={fill} />
          <path d="M144 262H158Q175 298 192 262H206" fill="none" stroke={ramp.low} strokeWidth="5" />
          <path d="M138 300Q144 288 146 264M212 300Q206 288 204 264" fill="none" stroke={ramp.low} strokeWidth="4" />
          {folds("M150 322L144 346M200 322L206 346")}
          {seam("M130 342H220")}
        </g>
      );
    case "band":
      return (
        <g>
          <path d="M122 266Q175 254 228 266L240 384H110Z" fill={fill} />
          <path d="M154 262Q175 284 196 262" fill={skin} />
          <path d="M154 262Q175 284 196 262" fill="none" stroke={ramp.low} strokeWidth="6" />
          <g fill={ink} opacity="0.92">
            <path d="M175 298L184 318L206 321L190 335L194 357L175 346L156 357L160 335L144 321L166 318Z" />
            <rect x="146" y="362" width="58" height="5" rx="2" />
            <text x="175" y="292" textAnchor="middle" fontFamily="Archivo Variable, Archivo, sans-serif" fontWeight="800" fontSize="11" letterSpacing="1.5">ON TOUR</text>
          </g>
          {folds("M126 300L118 380M224 300L232 380")}
          {seam("M112 378H238")}
        </g>
      );
    case "oversized":
      return (
        <g>
          <path d="M116 268Q175 252 234 268L250 404H100Z" fill={fill} />
          <path d="M150 260L175 292L200 260" fill={skin} />
          <path d="M148 258L175 296L162 306L140 272ZM202 258L175 296L188 306L210 272Z" fill={ramp.high} stroke={ramp.low} strokeWidth="2" />
          <path d="M175 296V404" stroke={ramp.low} strokeWidth="3" />
          {[322, 348, 374].map((y) => <circle key={y} cx="181" cy={y} r="3" fill={ramp.seam} />)}
          <rect x="196" y="316" width="28" height="28" rx="2" fill="none" stroke={ramp.low} strokeWidth="3" />
          {folds("M122 320L108 400M148 330L140 402M212 350L220 402M232 320L244 400")}
          {seam("M102 398H248")}
        </g>
      );
    case "openshirt":
      return (
        <g>
          <path d="M134 268H216L222 372H128Z" fill="#f1f1ee" />
          <path d="M154 262Q175 284 196 262" fill={skin} />
          <path d="M120 266Q140 256 156 260L166 386H106Z" fill={fill} />
          <path d="M230 266Q210 256 194 260L184 386H244Z" fill={fill} />
          <path d="M156 260L138 306L164 318ZM194 260L212 306L186 318Z" fill={ramp.high} stroke={ramp.low} strokeWidth="2" />
          {[[126, 336], [146, 362], [122, 372], [224, 336], [204, 362], [228, 372], [214, 320], [136, 320]].map(([cx, cy]) => (
            <g key={`${cx}-${cy}`} fill={ramp.seam}>
              {[[0, -5], [5, 0], [0, 5], [-5, 0]].map(([dx, dy]) => <circle key={`${dx}-${dy}`} cx={cx + dx} cy={cy + dy} r="3.4" />)}
            </g>
          ))}
          {folds("M150 372L146 322")}
        </g>
      );
    case "jersey":
      return (
        <g>
          <path d="M122 266Q175 254 228 266L238 384H112Z" fill={fill} />
          <path d="M156 260L175 292L194 260" fill={skin} />
          <path d="M152 258L175 298L198 258" fill="none" stroke={ramp.seam} strokeWidth="6" />
          <text x="175" y="358" textAnchor="middle" fontFamily="Archivo Variable, Archivo, sans-serif" fontWeight="800" fontSize="54" fill={ink} style={{ fontStretch: "75%" }}>10</text>
          <path d="M128 296H150M200 296H222" stroke={ramp.seam} strokeWidth="5" />
          {folds("M126 310L118 380M224 310L232 380")}
          {seam("M114 378H236")}
        </g>
      );
    case "jacket":
      return (
        <g>
          <path d="M134 268H216L222 340H128Z" fill="#f1f1ee" />
          <path d="M154 262Q175 284 196 262" fill={skin} />
          <path d="M120 266Q140 256 156 260L166 366H106Z" fill={fill} />
          <path d="M230 266Q210 256 194 260L184 366H244Z" fill={fill} />
          <path d="M156 260L138 304L166 316M194 260L212 304L184 316" fill="none" stroke={ramp.low} strokeWidth="5" />
          <rect x="116" y="320" width="28" height="22" rx="3" fill={ramp.low} />
          <rect x="206" y="320" width="28" height="22" rx="3" fill={ramp.low} />
          {seam("M110 356H164M186 356H240M118 316H146M204 316H232")}
          <circle cx="130" cy="330" r="2.5" fill="#d9af28" /><circle cx="220" cy="330" r="2.5" fill="#d9af28" />
        </g>
      );
  }
}

function ShoesLayer({ config, ids }: { config: AvatarConfig; ids: Ids }) {
  const shadow = <path d="M116 494H172" stroke="rgb(0 0 0 / 0.3)" strokeWidth="3" />;
  const shape: Record<AvatarConfig["shoes"], ReactNode> = {
    sneakers: (
      <>
        <path d="M134 462H172V486H114C112 472 124 468 134 462Z" fill="#f1f1ee" />
        <path d="M118 476C126 474 140 472 150 474V486H114Q114 480 118 476Z" fill="#d33a3a" />
        <rect x="110" y="484" width="64" height="11" rx="5" fill="#fff" stroke="#c9cdd4" strokeWidth="2" />
        <path d="M138 468L158 470M136 475L156 477" stroke="#26262c" strokeWidth="3" strokeLinecap="round" />
        {shadow}
      </>
    ),
    hitop: (
      <>
        <path d="M136 430H172V486H114C112 472 124 466 136 460Z" fill="#26262c" />
        <path d="M136 430H172V442H136Z" fill="#f1f1ee" />
        <circle cx="160" cy="466" r="8" fill="#f1f1ee" />
        <rect x="110" y="484" width="64" height="11" rx="4" fill="#f1f1ee" stroke="#c9cdd4" strokeWidth="2" />
        <path d="M140 450L154 452M138 458L152 460M136 466L148 468" stroke="#f1f1ee" strokeWidth="2.5" strokeLinecap="round" />
      </>
    ),
    boots: (
      <>
        <path d="M136 424H172V482H114C112 470 124 464 136 458Z" fill="#26262c" />
        <path d="M136 424H172V434H136Z" fill="#41444a" />
        <rect x="108" y="480" width="66" height="15" rx="4" fill="#3a3a42" />
        <path d="M112 488H170" stroke="#d9af28" strokeWidth="2" strokeDasharray="4 4" />
        {[440, 450, 460, 470].map((y) => <path key={y} d={`M142 ${y}H166`} stroke="#d4d5d6" strokeWidth="2.5" strokeLinecap="round" />)}
      </>
    ),
    platform: (
      <>
        <path d="M134 450H172V472H114C112 462 124 456 134 450Z" fill="#f1f1ee" />
        <rect x="108" y="470" width="66" height="25" rx="6" fill="#e85a9b" />
        <rect x="108" y="480" width="66" height="5" fill="#ffd24d" />
        <path d="M140 456L156 458M138 463L154 465" stroke="#26262c" strokeWidth="2.5" strokeLinecap="round" />
      </>
    ),
    cowboy: (
      <>
        <path d="M138 412H172V486H104C104 476 124 468 138 462Z" fill="#8a5a2e" />
        <path d="M138 412H172V424Q155 432 138 424Z" fill="#6d4522" />
        <rect x="102" y="484" width="72" height="11" rx="5" fill="#4d3018" />
        <path d="M144 436Q156 450 168 436M146 454Q156 464 166 454" fill="none" stroke="#e9c655" strokeWidth="2.5" />
      </>
    ),
  };
  void ids;
  return (
    <g>
      <g>{shape[config.shoes]}</g>
      <g transform="matrix(-1 0 0 1 350 0)">{shape[config.shoes]}</g>
    </g>
  );
}

function JewelryLayer({ config }: { config: AvatarConfig }) {
  if (config.jewelry === "hoops") {
    return (
      <g fill="none" stroke="#e9c655" strokeWidth="4">
        <circle cx="63" cy="196" r="15" />
        <circle cx="287" cy="196" r="15" />
      </g>
    );
  }
  return null;
}

function NeckJewelry({ config }: { config: AvatarConfig }) {
  if (config.jewelry === "choker") {
    return (
      <g>
        <path d="M158 256Q175 264 192 256" fill="none" stroke="#1d1d22" strokeWidth="7" />
        <circle cx="175" cy="266" r="5" fill="#e9c655" />
      </g>
    );
  }
  if (config.jewelry === "chain") {
    return (
      <g fill="none">
        <path d="M150 262Q175 322 200 262" stroke="#e9c655" strokeWidth="5" strokeDasharray="2 2" />
        <circle cx="175" cy="298" r="7" fill="#e9c655" stroke="#b98a12" strokeWidth="2" />
      </g>
    );
  }
  return null;
}

function BagLayer({ config, ids }: { config: AvatarConfig; ids: Ids }) {
  if (config.bag === "crossbody") {
    return (
      <g>
        <path d="M204 268L128 342" stroke="#3a2a1c" strokeWidth="4" fill="none" />
        <rect x="104" y="336" width="42" height="32" rx="7" fill="#c44a7d" stroke="#8f2a55" strokeWidth="2" />
        <path d="M104 348H146" stroke="#8f2a55" strokeWidth="2" />
        <circle cx="125" cy="352" r="3" fill="#e9c655" />
      </g>
    );
  }
  if (config.bag === "fanny") {
    return (
      <g>
        <path d="M204 270L146 340" stroke="#1d1d22" strokeWidth="9" fill="none" />
        <rect x="124" y="328" width="58" height="30" rx="12" fill={ids.url("bottom")} stroke="#1d1d22" strokeWidth="3" />
        <path d="M130 340H176" stroke="#d9af28" strokeWidth="3" strokeDasharray="3 3" />
      </g>
    );
  }
  return null;
}

function titleLines(title: string): string[] {
  const lines: string[] = [];
  let current = "";
  for (const word of title.toUpperCase().split(/\s+/)) {
    if (`${current} ${word}`.trim().length > 13 && current) {
      lines.push(current);
      current = word;
    } else {
      current = `${current} ${word}`.trim();
    }
  }
  if (current) lines.push(current);
  if (lines.length <= 2) return lines;
  return [lines[0], `${lines[1].slice(0, 11)}…`];
}

const TICKET_SHAPE = "M-70-34a10 10 0 0 1 10-10H60a10 10 0 0 1 10 10V-8a8 8 0 0 0 0 16V34a10 10 0 0 1-10 10H-60a10 10 0 0 1-10-10V8a8 8 0 0 0 0-16Z";

function HeldTicket({ title, count, accent, ids }: { title: string; count: number; accent: string; ids: Ids }) {
  const lines = titleLines(title);
  return (
    <g className="n-avatar__ticket">
      {count > 2 && <path d={TICKET_SHAPE} transform="translate(362 196) rotate(14)" fill="#8f6e0c" />}
      {count > 1 && <path d={TICKET_SHAPE} transform="translate(358 198) rotate(5)" fill="#a67e0d" />}
      <g transform="translate(350 200) rotate(-8)">
        <path d={TICKET_SHAPE} fill="rgb(0 0 0 / 0.25)" transform="translate(2 3)" filter={ids.url("soft")} />
        <path d={TICKET_SHAPE} fill={ids.url("gold")} />
        <rect x="-62" y="-44" width="6" height="88" fill={accent} />
        <path d="M34-38V38" stroke="#1b1d20" strokeWidth="2" strokeDasharray="4 5" opacity="0.5" />
        <g fill="#1b1d20" fontFamily="Archivo Variable, Archivo, sans-serif" fontWeight="800" style={{ fontStretch: "72%" }}>
          {lines.map((line, index) => (
            <text key={line} x="-50" y={(lines.length === 1 ? 6 : -4) + index * 19} fontSize="17">{line}</text>
          ))}
        </g>
        <g transform="translate(52 0) rotate(90)">
          <text textAnchor="middle" y="4" fontFamily="IBM Plex Mono, monospace" fontWeight="600" fontSize="11" fill="#1b1d20">{count > 1 ? `× ${count}` : "ADMIT 1"}</text>
        </g>
      </g>
    </g>
  );
}

function RockHand({ config, ids }: { config: AvatarConfig; ids: Ids }) {
  return (
    <g>
      <Limb d="M18 216L14 184" color={config.skin} width={14} ids={ids} />
      <Limb d="M48 216L52 184" color={config.skin} width={14} ids={ids} />
      <circle cx="32" cy="226" r="23" fill={ids.url("skin")} />
      <path d="M24 226L24 236M34 226L34 238M44 226L44 236" stroke="rgb(0 0 0 / 0.16)" strokeWidth="3" strokeLinecap="round" />
      <path d="M22 212Q32 222 44 212" fill="none" stroke="rgb(0 0 0 / 0.18)" strokeWidth="3" strokeLinecap="round" />
      {config.jewelry === "watch" && (
        <g>
          <rect x="22" y="246" width="30" height="13" rx="3" fill="#1d1d22" transform="rotate(32 37 252)" />
          <circle cx="37" cy="252" r="7" fill="#e9c655" stroke="#1d1d22" strokeWidth="2" />
        </g>
      )}
    </g>
  );
}

export const AVATAR_VIEWBOX = { full: "-4 -30 424 530", head: "47 18 256 256", body: "92 246 166 256" } as const;

type AvatarProps = {
  config: AvatarConfig;
  /** O ingresso na mão. Sem ele, a mão fica na cintura. */
  ticket?: { title: string; count?: number; accent?: string };
  /** full: corpo inteiro. head: só o rosto, para cabeçalho e listas. body: só as roupas, para o montador. */
  crop?: keyof typeof AVATAR_VIEWBOX;
  label?: string;
  className?: string;
};

export function Avatar({ config: stored, ticket, crop = "full", label, className }: AvatarProps) {
  const config = normalizeAvatar(stored as unknown as Record<string, unknown>);
  const prefix = `a${useId().replace(/[^a-zA-Z0-9]/g, "")}`;
  const ids: Ids = { prefix, url: (name) => `url(#${prefix}${name})` };

  const fem = config.style === "FEMININE";
  const hair = HAIR[config.hairStyle];
  const hairFill = ids.url("hair");
  const holding = Boolean(ticket) && crop === "full";
  const dress = isDress(config.top);
  const longSleeve = config.top === "jacket";
  const shortSleeve = config.top === "band" || config.top === "jersey" || config.top === "cropped" || config.top === "openshirt";
  const wideSleeve = config.top === "oversized";
  const sleeveColor = config.topColor;
  const armColor = longSleeve ? sleeveColor : config.skin;
  const shoulderLeft = fem ? 142 : 130;
  const shoulderRight = fem ? 208 : 220;
  const legWidth = fem ? 27 : 31;
  const armWidth = fem ? 22 : 26;
  const torso = fem
    ? "M143 266Q175 256 207 266Q213 296 206 318Q216 336 220 350H130Q134 336 144 318Q137 296 143 266Z"
    : "M126 268Q175 254 224 268L222 350H128Z";
  const hairDark = dark(config.hairColor, 0.38);
  const hairLight = light(config.hairColor, 0.4);

  return (
    <svg
      className={["n-avatar", crop !== "full" && "n-avatar--crop", className].filter(Boolean).join(" ")}
      viewBox={AVATAR_VIEWBOX[crop]}
      {...(label ? { role: "img", "aria-label": label } : { "aria-hidden": true })}
    >
      <Defs ids={ids} config={config} />

      {crop === "full" && <ellipse cx="175" cy="496" rx="96" ry="9" fill="#000" opacity="0.32" filter={ids.url("soft")} />}

      {hair.back?.(hairFill)}

      <Limb d="M155 342L153 468" color={config.skin} width={legWidth} ids={ids} />
      <Limb d="M195 342L197 468" color={config.skin} width={legWidth} ids={ids} />
      <path d={torso} fill={ids.url("skin")} />
      <rect x="159" y="236" width="32" height="36" rx="12" fill={ids.url("skin")} />
      <path d="M159 246Q175 276 191 246V262Q175 278 159 262Z" fill="#000" opacity="0.2" filter={ids.url("soft")} />
      <ShoesLayer config={config} ids={ids} />
      {!dress && <BottomLayer config={config} ids={ids} />}
      <TopLayer config={config} ids={ids} />
      <NeckJewelry config={config} />
      <BagLayer config={config} ids={ids} />

      {/* Braço livre: sinal de rock */}
      <Limb d={`M${shoulderLeft} 282L74 306L38 246`} color={armColor} width={armWidth} ids={ids} />
      {shortSleeve && <Limb d={`M${shoulderLeft} 282L${shoulderLeft - 34} 295`} color={sleeveColor} width={armWidth + 7} ids={ids} />}
      {wideSleeve && <Limb d={`M${shoulderLeft} 282L${shoulderLeft - 46} 300`} color={sleeveColor} width={armWidth + 14} ids={ids} />}
      <RockHand config={config} ids={ids} />

      {/* Cabeça */}
      <path d="M90 236Q175 286 260 236L250 272H100Z" fill="#000" opacity="0.16" filter={ids.url("softer")} />
      <circle cx="65" cy="166" r="17" fill={ids.url("skin")} />
      <circle cx="285" cy="166" r="17" fill={ids.url("skin")} />
      <path d="M58 160Q66 172 62 180M292 160Q284 172 288 180" fill="none" stroke="rgb(0 0 0 / 0.15)" strokeWidth="3" strokeLinecap="round" />
      <path d={HEAD} fill={ids.url("skin")} />
      <g clipPath={ids.url("head-clip")}>
        {hair.front && <g transform="translate(0 9)" opacity="0.22" filter={ids.url("soft")} style={{ color: "#000" }}>{hair.front("#000")}</g>}
      </g>
      <circle cx="100" cy="202" r="22" fill="#ff5a6e" opacity="0.28" filter={ids.url("soft")} />
      <circle cx="250" cy="202" r="22" fill="#ff5a6e" opacity="0.28" filter={ids.url("soft")} />
      {config.hairStyle === "bald" && <ellipse cx="146" cy="86" rx="38" ry="18" fill="#fff" opacity="0.3" filter={ids.url("soft")} />}
      <FacePaint paint={config.paint} />
      {fem ? (
        <g fill="none" stroke={dark(config.hairColor, 0.3)} strokeWidth="4.5" strokeLinecap="round">
          <path d="M106 134Q128 118 152 128" />
          <path d="M198 128Q222 118 244 134" />
        </g>
      ) : (
        <g fill={dark(config.hairColor, 0.25)}>
          <path d="M104 132Q128 112 154 124L152 134Q128 126 108 142Z" />
          <path d="M196 124Q222 112 246 132L242 142Q222 126 198 134Z" />
        </g>
      )}
      <Eye cx={130} config={config} side="left" ids={ids} />
      <Eye cx={220} config={config} side="right" ids={ids} />
      <path d="M167 190Q175 198 183 190" fill="none" stroke="rgb(0 0 0 / 0.28)" strokeWidth="3.5" strokeLinecap="round" />
      <ellipse cx="175" cy="182" rx="5" ry="7" fill="#fff" opacity="0.22" />
      <BeardLayer config={config} ids={ids} />
      <Mouth config={config} />
      {hair.front?.(hairFill)}
      {hair.strands && (
        <g fill="none" strokeLinecap="round">
          {hair.strands.map((d) => (
            <g key={d}>
              <path d={d} stroke={hairDark} strokeWidth="3.5" opacity="0.5" />
              <path d={d} stroke={hairLight} strokeWidth="1.8" opacity="0.5" transform="translate(-3 -2)" />
            </g>
          ))}
        </g>
      )}
      {hair.front && <path d="M104 74Q175 40 246 74" fill="none" stroke="#fff" strokeWidth="11" strokeLinecap="round" opacity="0.2" filter={ids.url("soft")} />}
      <JewelryLayer config={config} />
      <HatLayer config={config} ids={ids} />
      <GlassesLayer glasses={config.glasses} />

      {/* Braço do ingresso */}
      {holding && ticket ? (
        <>
          <Limb d={`M${shoulderRight} 282L290 308L336 270`} color={armColor} width={armWidth} ids={ids} />
          {shortSleeve && <Limb d={`M${shoulderRight} 282L${shoulderRight + 34} 295`} color={sleeveColor} width={armWidth + 7} ids={ids} />}
          {wideSleeve && <Limb d={`M${shoulderRight} 282L${shoulderRight + 46} 300`} color={sleeveColor} width={armWidth + 14} ids={ids} />}
          <circle cx="340" cy="258" r="22" fill={ids.url("skin")} />
          <HeldTicket title={ticket.title} count={ticket.count ?? 1} accent={ticket.accent ?? "#c9502f"} ids={ids} />
          <ellipse cx="320" cy="244" rx="10" ry="18" fill={ids.url("skin")} transform="rotate(30 320 244)" />
          {[334, 348, 362].map((x, index) => (
            <rect key={x} x={x - 6.5} y={236 + index * 2} width="13" height="24" rx="6.5" fill={ids.url("skin")} stroke="rgb(0 0 0 / 0.14)" strokeWidth="1.5" />
          ))}
        </>
      ) : (
        <>
          <Limb d={`M${shoulderRight} 282L262 314L228 344`} color={armColor} width={armWidth} ids={ids} />
          {shortSleeve && <Limb d={`M${shoulderRight} 282L${shoulderRight + 28} 298`} color={sleeveColor} width={armWidth + 7} ids={ids} />}
          {wideSleeve && <Limb d={`M${shoulderRight} 282L${shoulderRight + 36} 304`} color={sleeveColor} width={armWidth + 14} ids={ids} />}
          <circle cx="226" cy="346" r="15" fill={ids.url("skin")} />
        </>
      )}
    </svg>
  );
}
