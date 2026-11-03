/* Arte gerada para os eventos de exemplo. Cada evento real terá a própria foto: o hub trata tudo
   como imagem comum, então a troca é só preencher imageUrl. Determinística: mesma semente, mesma arte. */

export type ArtStyle = "sunburst" | "waves" | "curtain" | "mountains" | "bauhaus" | "neon" | "stadium" | "confetti" | "spotlight" | "vinyl";

export type ArtSpec = { style: ArtStyle; palette: [string, string, string, string]; seed: number };

const W = 1200;
const H = 1500;

function seeded(seed: number) {
  let state = seed;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let mixed = Math.imul(state ^ (state >>> 15), 1 | state);
    mixed = (mixed + Math.imul(mixed ^ (mixed >>> 7), 61 | mixed)) ^ mixed;
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4_294_967_296;
  };
}

function sunburst([bg, a, b, c]: ArtSpec["palette"], random: () => number): string {
  const cx = W * (0.3 + random() * 0.4);
  const cy = H * 0.62;
  const rays = Array.from({ length: 22 }, (_, index) => {
    const from = (index / 22) * Math.PI * 2;
    const to = ((index + 0.5) / 22) * Math.PI * 2;
    const far = 2200;
    return `<path d="M${cx} ${cy}L${cx + Math.cos(from) * far} ${cy + Math.sin(from) * far}L${cx + Math.cos(to) * far} ${cy + Math.sin(to) * far}Z" fill="${a}"/>`;
  }).join("");
  return `<rect width="${W}" height="${H}" fill="${bg}"/>${rays}<circle cx="${cx}" cy="${cy}" r="330" fill="${b}"/><circle cx="${cx}" cy="${cy}" r="210" fill="${c}"/><path d="M0 ${H * 0.82}C300 ${H * 0.74} 620 ${H * 0.9} ${W} ${H * 0.78}V${H}H0Z" fill="${bg}"/>`;
}

function waves([bg, a, b, c]: ArtSpec["palette"], random: () => number): string {
  const lines = Array.from({ length: 34 }, (_, index) => {
    const y = 120 + index * 40;
    const amp = 40 + Math.sin(index / 4) * 150 + random() * 40;
    const color = index % 7 === 0 ? c : index % 3 === 0 ? b : a;
    return `<path d="M-40 ${y}C260 ${y - amp} 420 ${y + amp} 640 ${y}S1040 ${y - amp} 1260 ${y}" fill="none" stroke="${color}" stroke-width="${index % 7 === 0 ? 14 : 6}" opacity="${0.45 + (index % 5) * 0.11}"/>`;
  }).join("");
  return `<rect width="${W}" height="${H}" fill="${bg}"/>${lines}`;
}

function curtain([bg, a, b, c]: ArtSpec["palette"]): string {
  const pleats = (from: number, to: number) =>
    Array.from({ length: 6 }, (_, index) => {
      const x = from + ((to - from) / 6) * index;
      return `<rect x="${x}" width="${(to - from) / 12}" height="${H}" fill="${b}"/>`;
    }).join("");
  return `<rect width="${W}" height="${H}" fill="${bg}"/><path d="M600 120L1060 ${H}H140Z" fill="${c}" opacity="0.32"/><ellipse cx="600" cy="${H - 120}" rx="430" ry="70" fill="${c}" opacity="0.65"/><path d="M0 0H430C400 500 300 1100 170 ${H}H0Z" fill="${a}"/><path d="M${W} 0H770C800 500 900 1100 1030 ${H}H${W}Z" fill="${a}"/><g opacity="0.35"><g clip-path="inset(0)">${pleats(0, 300)}${pleats(900, W)}</g></g><path d="M0 0H${W}V150C1000 240 800 150 600 220C400 150 200 240 0 150Z" fill="${b}"/>`;
}

function mountains([bg, a, b, c]: ArtSpec["palette"], random: () => number): string {
  const ridge = (base: number, height: number, color: string) => {
    let path = `M0 ${H}L0 ${base}`;
    for (let x = 0; x <= W + 150; x += 150) path += `L${x} ${base - random() * height}`;
    return `<path d="${path}L${W} ${H}Z" fill="${color}"/>`;
  };
  return `<rect width="${W}" height="${H}" fill="${bg}"/><circle cx="${W * (0.25 + random() * 0.5)}" cy="430" r="190" fill="${c}"/>${ridge(860, 300, a)}${ridge(1080, 260, b)}${ridge(1290, 200, "#000000")}`;
}

function bauhaus([bg, a, b, c]: ArtSpec["palette"], random: () => number): string {
  const size = 300;
  const colors = [a, b, c, bg];
  let shapes = "";
  for (let row = 0; row < 5; row++) {
    for (let column = 0; column < 4; column++) {
      const x = column * size;
      const y = row * size;
      const fill = colors[Math.floor(random() * 3)];
      const kind = Math.floor(random() * 4);
      if (kind === 0) shapes += `<circle cx="${x + 150}" cy="${y + 150}" r="130" fill="${fill}"/>`;
      if (kind === 1) shapes += `<path d="M${x} ${y}H${x + size}A${size} ${size} 0 0 1 ${x} ${y + size}Z" fill="${fill}"/>`;
      if (kind === 2) shapes += `<path d="M${x} ${y + size}V${y}A${size} ${size} 0 0 1 ${x + size} ${y + size}Z" fill="${fill}"/>`;
      if (kind === 3) shapes += `<rect x="${x + 30}" y="${y + 30}" width="240" height="240" fill="${fill}"/>`;
    }
  }
  return `<rect width="${W}" height="${H}" fill="${bg}"/>${shapes}`;
}

function neon([bg, a, b, c]: ArtSpec["palette"]): string {
  const horizon = 760;
  let grid = "";
  for (let index = -12; index <= 12; index++) grid += `<path d="M600 ${horizon}L${600 + index * 190} ${H}" stroke="${a}" stroke-width="4"/>`;
  for (let index = 0; index < 12; index++) {
    const y = horizon + Math.pow(index / 12, 2) * (H - horizon);
    grid += `<path d="M0 ${y}H${W}" stroke="${a}" stroke-width="4"/>`;
  }
  const stripes = Array.from({ length: 6 }, (_, index) => `<rect x="280" y="${560 + index * 34}" width="640" height="${6 + index * 3}" fill="${bg}"/>`).join("");
  return `<rect width="${W}" height="${H}" fill="${bg}"/><circle cx="600" cy="520" r="320" fill="${b}"/><circle cx="600" cy="520" r="320" fill="${c}" opacity="0.45"/>${stripes}<rect y="${horizon}" width="${W}" height="${H - horizon}" fill="${bg}"/><g fill="none">${grid}</g>`;
}

function stadium([bg, a, b, c]: ArtSpec["palette"]): string {
  const arcs = Array.from({ length: 9 }, (_, index) => {
    const r = 1450 - index * 150;
    return `<circle cx="600" cy="1550" r="${r}" fill="${index % 2 ? a : b}"/>`;
  }).join("");
  return `<rect width="${W}" height="${H}" fill="${bg}"/>${arcs}<path d="M250 0L420 760L560 0ZM950 0L780 760L640 0Z" fill="${c}" opacity="0.32"/><circle cx="600" cy="1240" r="130" fill="${c}"/>`;
}

function confetti([bg, a, b, c]: ArtSpec["palette"], random: () => number): string {
  const colors = [a, b, c];
  let blobs = "";
  for (let index = 0; index < 90; index++) {
    const x = random() * W;
    const y = random() * H;
    const size = 18 + random() * 70;
    const color = colors[index % 3];
    const kind = index % 3;
    if (kind === 0) blobs += `<circle cx="${x}" cy="${y}" r="${size / 1.6}" fill="${color}"/>`;
    if (kind === 1) blobs += `<rect x="${x}" y="${y}" width="${size}" height="${size / 2.4}" rx="8" fill="${color}" transform="rotate(${random() * 180} ${x} ${y})"/>`;
    if (kind === 2) blobs += `<path d="M${x} ${y}q${size / 2} -${size} ${size} 0t${size} 0" fill="none" stroke="${color}" stroke-width="12" stroke-linecap="round"/>`;
  }
  return `<rect width="${W}" height="${H}" fill="${bg}"/>${blobs}`;
}

function spotlight([bg, a, b, c]: ArtSpec["palette"]): string {
  let bricks = "";
  for (let row = 0; row < 16; row++) {
    for (let column = -1; column < 8; column++) {
      bricks += `<rect x="${column * 200 + (row % 2) * 100}" y="${row * 100}" width="190" height="90" fill="${a}"/>`;
    }
  }
  return `<rect width="${W}" height="${H}" fill="${bg}"/><g opacity="0.55">${bricks}</g><path d="M600 -60L1020 1180H180Z" fill="${c}" opacity="0.34"/><ellipse cx="600" cy="1180" rx="420" ry="90" fill="${c}" opacity="0.8"/><g fill="${b}"><rect x="540" y="540" width="120" height="240" rx="60"/><rect x="584" y="770" width="32" height="360"/><rect x="480" y="1120" width="240" height="30" rx="15"/></g>`;
}

function vinyl([bg, a, b, c]: ArtSpec["palette"]): string {
  const grooves = Array.from({ length: 16 }, (_, index) => `<circle cx="760" cy="760" r="${620 - index * 32}" fill="none" stroke="${b}" stroke-width="3" opacity="0.5"/>`).join("");
  return `<rect width="${W}" height="${H}" fill="${bg}"/><circle cx="760" cy="760" r="640" fill="${a}"/>${grooves}<circle cx="760" cy="760" r="150" fill="${c}"/><circle cx="760" cy="760" r="24" fill="${bg}"/><path d="M0 1120L900 ${H}H0Z" fill="${c}" opacity="0.85"/>`;
}

const STYLES = { sunburst, waves, curtain, mountains, bauhaus, neon, stadium, confetti, spotlight, vinyl } as const;

export function artToImage(spec: ArtSpec): string {
  const body = STYLES[spec.style](spec.palette, seeded(spec.seed));
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid slice">${body}</svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}
