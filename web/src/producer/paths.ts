/**
 * Endereços da área do produtor. Em produtores.ntix.com.br, o cadastro fica na raiz e o painel também, sem prefixo
 * (/visao-geral, /eventos, /vendas...). Em qualquer outro endereço (localhost, testes), tudo segue no mesmo app,
 * sob /produtores, /entrar e /painel.
 */
const PRODUCER_HOST_PREFIX = "produtores.";
const PRODUCER_ORIGIN = "https://produtores.ntix.com.br";
const PUBLIC_HOSTS = ["ntix.com.br", "www.ntix.com.br"];
const OVERVIEW_SEGMENT = "visao-geral";

const currentHost = () => window.location.hostname;

export function isProducerHost(hostname = currentHost()): boolean {
  return hostname.startsWith(PRODUCER_HOST_PREFIX);
}

/** Rota do painel. Sem argumento, a visão geral; com argumento, a área ("eventos", `eventos/${id}`...). */
export function panelPath(area = "", hostname = currentHost()): string {
  if (isProducerHost(hostname)) return `/${area || OVERVIEW_SEGMENT}`;
  return area ? `/painel/${area}` : "/painel";
}

/** Segmento da visão geral dentro das rotas do painel: no subdomínio ela tem caminho próprio, porque a raiz é o cadastro. */
export function overviewSegment(hostname = currentHost()): string | undefined {
  return isProducerHost(hostname) ? OVERVIEW_SEGMENT : undefined;
}

export function signupPath(hostname = currentHost()): string {
  return isProducerHost(hostname) ? "/" : "/produtores";
}

export const LOGIN_PATH = "/entrar";

/** Links do hub para a área do produtor: no domínio público, levam para o subdomínio. */
export function producerAreaHref(target: "signup" | "login", hostname = currentHost()): string {
  const path = target === "signup" ? signupPath(hostname) : LOGIN_PATH;
  if (!PUBLIC_HOSTS.includes(hostname)) return path;
  return target === "signup" ? `${PRODUCER_ORIGIN}/` : `${PRODUCER_ORIGIN}${LOGIN_PATH}`;
}
