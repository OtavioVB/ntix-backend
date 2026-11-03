/* Letras da Montserrat ExtraBold em minúsculas, convertidas em traçados. */
const N =
  "M251 0L61 0L61 542L242 542L242 483Q264 506 292 522Q346 551 414 551Q477 551 528 525.5Q579 500 608.5 447Q638 394 638 310L638 0L448 0L448 279Q448 337 424.5 363.5Q401 390 359 390Q329 390 304 376.5Q279 363 265 335Q251 307 251 262Z";
/* A assinatura: a perna do "t" desce e corre sob o "i" e o "x" como um sublinhado.
   O corte final é paralelo à perna direita do "x" e termina na mesma vertical que ela. */
const T =
  "M4 542L83 542L83 663L273 663L273 542L397 542L397 401L273 401L273 20Q273-70 363-70L1176-70L1281-211L320-211Q83-211 83 20L83 401L4 401Z";
const I =
  "M251 0L61 0L61 542L251 542ZM156 602Q104 602 72 631Q40 660 40 703Q40 746 72 775Q104 804 156 804Q208 804 240 776.5Q272 749 272 706Q272 661 240.5 631.5Q209 602 156 602Z";
const X = "M204 0L-2 0L204 271L4 542L218 542L314 405L414 542L615 542L415 278L622 0L405 0L308 142Z";

/** Os traçados da marca, para desenhar fora do React, como na imagem do story. */
export const WORDMARK = { N, T, I, X };

type WordmarkProps = {
  href?: string;
  label?: string;
  /** Marca repetida ao lado de texto que já a identifica: fica fora do leitor de tela. */
  decorative?: boolean;
  className?: string;
};

/** A marca Ntix: "n" em ouro e "tix" em cinza. As cores vêm da cena (--n-logo-n, --n-logo-tix). */
export function Wordmark({ href, label = "Ntix", decorative, className }: WordmarkProps) {
  const svg = (
    <svg viewBox="61 0 1880 1015" {...(decorative ? { "aria-hidden": true } : { role: "img", "aria-label": label })}>
      <g transform="matrix(1 0 0 -1 0 804)">
        <path className="n-wordmark__n" d={N} />
        <g className="n-wordmark__tix">
          <path transform="translate(660)" d={T} />
          <path transform="translate(1032)" d={I} />
          <path transform="translate(1319)" d={X} />
        </g>
      </g>
    </svg>
  );
  const classes = ["n-wordmark", className].filter(Boolean).join(" ");

  return href ? (
    <a className={classes} href={href}>
      {svg}
    </a>
  ) : (
    <span className={classes}>{svg}</span>
  );
}
