import { DAY_MS } from "../platform/demoData";
import type { PlatformData, ProducerEvent } from "../platform/types";
import type { ArtSpec, ArtStyle } from "./art";

export const CATEGORIES = ["Shows", "Festivais", "Teatro", "Stand-up", "Eletrônica", "Esportes", "Gastronomia", "Infantil"] as const;
export type Category = (typeof CATEGORIES)[number] | "Outros";

export type HubBatch = {
  id: string;
  name: string;
  note?: string;
  priceCents: number;
  quantity: number;
  /** Já vendidos antes desta sessão. */
  sold: number;
  startsAt: number;
  endsAt: number;
};

export type HubEvent = {
  id: string;
  slug: string;
  title: string;
  tagline: string;
  category: Category;
  producer: string;
  venue: string;
  address: string;
  city: string;
  state: string;
  date: number;
  doorsAt: string;
  startsAt: string;
  ageRating: string;
  description: string[];
  schedule: { time: string; title: string }[];
  included: string[];
  /** Foto do evento. Sem ela, vale a arte gerada. */
  imageUrl?: string;
  art: ArtSpec;
  accent: string;
  popularity: number;
  featured?: boolean;
  batches: HubBatch[];
  /** Publicado por um produtor da plataforma: a venda vai para a carteira dele. */
  fromProducer?: boolean;
};

type BatchSeed = [name: string, priceCents: number, quantity: number, sold: number, startsInDays: number, endsInDays: number, note?: string];

type EventSeed = Omit<HubEvent, "id" | "date" | "batches" | "art"> & {
  inDays: number;
  style: ArtStyle;
  palette: ArtSpec["palette"];
  batchSeeds: BatchSeed[];
};

const SEEDS: EventSeed[] = [
  {
    slug: "festival-horizonte", title: "Festival Horizonte", tagline: "Três palcos, um pôr do sol e nenhuma pressa", category: "Festivais", producer: "Maré Cultural",
    venue: "Parque das Dunas", address: "Av. Beira-Mar, 1200", city: "Recife", state: "PE", inDays: 26, doorsAt: "14h", startsAt: "15h", ageRating: "16 anos",
    description: ["Um dia inteiro de música à beira-mar, com três palcos, feira de artistas locais e praça de alimentação com cozinheiros da região.", "O portão abre cedo para quem quer ver o sol descer ao som da primeira atração. Leve protetor solar e chegue com tempo."],
    schedule: [{ time: "15h", title: "Palco Maré: Coletivo Preamar" }, { time: "17h30", title: "Palco Sol: Banda Vento Leste" }, { time: "20h", title: "Palco Horizonte: Orquestra Popular do Cais" }, { time: "22h30", title: "Encerramento: Noite Azul" }],
    included: ["Acesso aos três palcos", "Feira de artistas", "Área de descanso"], accent: "#ef7d2d", popularity: 98, featured: true, style: "sunburst", palette: ["#3a1465", "#c92f6b", "#f28a2e", "#ffd25a"],
    batchSeeds: [["Pista · 1º lote", 14_000, 800, 800, -40, -12], ["Pista · 2º lote", 18_000, 1_000, 742, -12, 20], ["Pista · meia-entrada", 9_000, 300, 214, -12, 20, "Apresente o documento na entrada"], ["Área VIP", 34_000, 200, 171, -12, 20, "Bar exclusivo e banheiros reservados"]],
  },
  {
    slug: "pulso-noturno", title: "Pulso Noturno", tagline: "Doze horas de pista contínua", category: "Eletrônica", producer: "Coletivo Frequência",
    venue: "Galpão 44", address: "Rua dos Trilhos, 44", city: "São Paulo", state: "SP", inDays: 12, doorsAt: "22h", startsAt: "23h", ageRating: "18 anos",
    description: ["A pista abre às 23h e só fecha com o dia claro. Som calibrado, luz desenhada para o espaço e uma escalação de quem faz a cena local girar.", "Galpão climatizado, com área externa aberta durante toda a noite."],
    schedule: [{ time: "23h", title: "Abertura: DJ Marsala" }, { time: "2h", title: "Duo Sublime" }, { time: "5h", title: "Set de encerramento: Nyx" }],
    included: ["Acesso à pista", "Área externa", "Água gratuita"], accent: "#e03ad0", popularity: 94, featured: true, style: "neon", palette: ["#0c0824", "#27d7ff", "#ff2fa8", "#ffb347"],
    batchSeeds: [["Pista · 2º lote", 9_000, 600, 588, -30, 10], ["Pista · último lote", 12_000, 400, 96, -2, 12]],
  },
  {
    slug: "o-ultimo-ato", title: "O Último Ato", tagline: "A peça que fez São Paulo prender a respiração", category: "Teatro", producer: "Companhia Aurora",
    venue: "Teatro das Artes", address: "Praça da República, 88", city: "São Paulo", state: "SP", inDays: 8, doorsAt: "19h30", startsAt: "20h", ageRating: "14 anos",
    description: ["Na noite da última apresentação de sua companhia, uma atriz recebe uma carta que muda o que ela achava saber sobre a própria história.", "Direção de Helena Prates. Duração de 95 minutos, sem intervalo."],
    schedule: [{ time: "19h30", title: "Abertura da casa" }, { time: "20h", title: "Início do espetáculo" }, { time: "21h35", title: "Conversa com o elenco" }],
    included: ["Assento marcado na retirada", "Programa impresso"], accent: "#b3273f", popularity: 88, featured: true, style: "curtain", palette: ["#1a0810", "#8e1a2e", "#6d1224", "#ffd9a0"],
    batchSeeds: [["Plateia", 12_000, 240, 196, -25, 8], ["Plateia · meia-entrada", 6_000, 80, 61, -25, 8, "Apresente o documento na entrada"], ["Balcão", 8_000, 120, 120, -25, 8]],
  },
  {
    slug: "riso-solto", title: "Riso Solto", tagline: "Stand-up sem ensaio e com a plateia no meio", category: "Stand-up", producer: "Casa do Riso",
    venue: "Bar do Palco", address: "Rua Augusta, 512", city: "Belo Horizonte", state: "MG", inDays: 5, doorsAt: "20h", startsAt: "21h", ageRating: "16 anos",
    description: ["Quatro comediantes, um microfone e nenhuma piada repetida. A noite termina com o quadro de improviso, com temas sugeridos por quem está na plateia.", "As mesas ficam a poucos metros do palco. Quem senta na frente costuma entrar no show."],
    schedule: [{ time: "21h", title: "Abertura: Tita Moraes" }, { time: "21h40", title: "Rafa Nogueira" }, { time: "22h30", title: "Improviso com a plateia" }],
    included: ["Mesa compartilhada"], accent: "#e8a93a", popularity: 81, style: "spotlight", palette: ["#160f0b", "#3a2318", "#101014", "#ffe29a"],
    batchSeeds: [["Mesa compartilhada", 6_000, 120, 74, -14, 5]],
  },
  {
    slug: "classico-do-norte", title: "Clássico do Norte", tagline: "A final que a cidade esperou o ano inteiro", category: "Esportes", producer: "Liga Norte",
    venue: "Arena Norte", address: "Av. das Torcidas, 900", city: "Porto Alegre", state: "RS", inDays: 19, doorsAt: "14h", startsAt: "16h", ageRating: "Livre",
    description: ["Decisão do campeonato regional, com os portões abrindo duas horas antes para o aquecimento e a festa das torcidas.", "Crianças até 5 anos entram sem ingresso se ficarem no colo."],
    schedule: [{ time: "14h", title: "Abertura dos portões" }, { time: "15h30", title: "Festa das torcidas" }, { time: "16h", title: "Início da partida" }],
    included: ["Assento no setor escolhido"], accent: "#2e9e6a", popularity: 90, style: "stadium", palette: ["#0b2a1e", "#17553b", "#1f7350", "#f4f1d0"],
    batchSeeds: [["Arquibancada", 8_000, 4_000, 3_180, -20, 19], ["Arquibancada · meia-entrada", 4_000, 1_200, 1_004, -20, 19, "Apresente o documento na entrada"], ["Cadeira coberta", 16_000, 900, 610, -20, 19]],
  },
  {
    slug: "cozinha-a-ceu-aberto", title: "Cozinha a Céu Aberto", tagline: "Vinte cozinheiros, um quarteirão inteiro", category: "Gastronomia", producer: "Sabor de Rua",
    venue: "Praça do Mercado", address: "Largo do Mercado, s/n", city: "Salvador", state: "BA", inDays: 33, doorsAt: "11h", startsAt: "11h", ageRating: "Livre",
    description: ["Um quarteirão fechado para comer bem: vinte cozinheiros, música ao vivo e oficinas rápidas ao longo do dia.", "O ingresso dá direito à entrada e a quatro degustações à sua escolha."],
    schedule: [{ time: "11h", title: "Abertura das barracas" }, { time: "14h", title: "Oficina de moquecas" }, { time: "17h", title: "Roda de samba" }],
    included: ["Entrada", "Quatro degustações"], accent: "#d9482b", popularity: 72, style: "confetti", palette: ["#fff1d6", "#d9482b", "#2c7a5b", "#e8a93a"],
    batchSeeds: [["Entrada com degustação", 7_500, 900, 311, -10, 33]],
  },
  {
    slug: "pequenos-gigantes", title: "Pequenos Gigantes", tagline: "Um musical para toda a família cantar junto", category: "Infantil", producer: "Trupe Girassol",
    venue: "Teatro Bela Vista", address: "Rua das Flores, 210", city: "Curitiba", state: "PR", inDays: 16, doorsAt: "15h30", startsAt: "16h", ageRating: "Livre",
    description: ["Dois irmãos descobrem que o quintal de casa esconde um reino inteiro. Música ao vivo, bonecos gigantes e muita participação da plateia.", "Sessão com 60 minutos, pensada para crianças a partir de 3 anos."],
    schedule: [{ time: "15h30", title: "Abertura da casa" }, { time: "16h", title: "Início do musical" }, { time: "17h", title: "Fotos com os bonecos" }],
    included: ["Assento marcado na retirada"], accent: "#3aa0d9", popularity: 64, style: "bauhaus", palette: ["#fff4d9", "#3aa0d9", "#f2653a", "#f7c531"],
    batchSeeds: [["Adulto", 7_000, 200, 88, -12, 16], ["Criança", 3_500, 200, 104, -12, 16, "De 3 a 12 anos"]],
  },
  {
    slug: "acustico-na-serra", title: "Acústico na Serra", tagline: "Violão, lareira e céu aberto", category: "Shows", producer: "Rota Sonora",
    venue: "Mirante da Serra", address: "Estrada do Mirante, km 8", city: "Curitiba", state: "PR", inDays: 40, doorsAt: "16h", startsAt: "17h", ageRating: "Livre",
    description: ["Um fim de tarde na serra com três artistas revezando o palco em formato acústico. Público limitado para manter o clima de quintal.", "Leve casaco: o frio chega rápido depois que o sol se põe."],
    schedule: [{ time: "17h", title: "Lia Monteverde" }, { time: "18h30", title: "Trio Araucária" }, { time: "20h", title: "Encontro de todos no palco" }],
    included: ["Acesso ao mirante", "Bebida quente"], accent: "#5e8fb5", popularity: 58, style: "mountains", palette: ["#f2b27a", "#6a6f96", "#2f3f63", "#fff0c7"],
    batchSeeds: [["Ingresso individual", 11_000, 250, 92, -8, 40]],
  },
  {
    slug: "noites-de-vinil", title: "Noites de Vinil", tagline: "Soul, funk e disco, do jeito que foi gravado", category: "Shows", producer: "Selo Lado B",
    venue: "Clube da Esquina Velha", address: "Rua do Ouvidor, 77", city: "Rio de Janeiro", state: "RJ", inDays: 10, doorsAt: "21h", startsAt: "22h", ageRating: "18 anos",
    description: ["Colecionadores tocam só em vinil, com banda ao vivo entrando na segunda metade da noite.", "Traga um disco para a mesa de trocas no salão de entrada."],
    schedule: [{ time: "22h", title: "Discotecagem: Selo Lado B" }, { time: "0h", title: "Banda Groove Carioca" }],
    included: ["Acesso ao salão", "Mesa de trocas"], accent: "#c9502f", popularity: 77, style: "vinyl", palette: ["#efe3cf", "#1c1a1c", "#6f6a66", "#c9502f"],
    batchSeeds: [["1º lote", 7_000, 200, 200, -18, -2], ["2º lote", 9_000, 200, 122, -2, 10]],
  },
  {
    slug: "mar-de-sons", title: "Mar de Sons", tagline: "Uma orquestra na areia ao cair da tarde", category: "Shows", producer: "Maré Cultural",
    venue: "Praia do Forte", address: "Orla, posto 6", city: "Rio de Janeiro", state: "RJ", inDays: 54, doorsAt: "16h", startsAt: "17h30", ageRating: "Livre",
    description: ["A orquestra toca de frente para o mar, com repertório que vai do clássico à música popular brasileira.", "Cadeiras de praia incluídas. O evento acontece mesmo com chuva leve."],
    schedule: [{ time: "17h30", title: "Primeira parte: clássicos" }, { time: "19h", title: "Segunda parte: música popular" }],
    included: ["Cadeira de praia", "Programa do concerto"], accent: "#1f7f9e", popularity: 69, style: "waves", palette: ["#062a3a", "#1f9eb5", "#7fd6cf", "#ffd98a"],
    batchSeeds: [["Área geral", 9_500, 1_500, 402, -6, 54], ["Frente do palco", 18_000, 200, 66, -6, 54]],
  },
  {
    slug: "feira-do-sol", title: "Feira do Sol", tagline: "Música, feira e cinema ao ar livre", category: "Festivais", producer: "Rota Sonora",
    venue: "Parque da Cidade", address: "Eixo Monumental, s/n", city: "Brasília", state: "DF", inDays: 47, doorsAt: "12h", startsAt: "13h", ageRating: "Livre",
    description: ["Dois palcos, feira de pequenos produtores e uma sessão de cinema ao ar livre para fechar o dia.", "Crianças até 10 anos não pagam."],
    schedule: [{ time: "13h", title: "Abertura: Banda Cerrado" }, { time: "18h", title: "Show principal: Sertão Elétrico" }, { time: "20h30", title: "Cinema ao ar livre" }],
    included: ["Acesso aos palcos", "Sessão de cinema"], accent: "#d68a1c", popularity: 61, style: "sunburst", palette: ["#0e4f4a", "#14706a", "#f2b632", "#f46b3b"],
    batchSeeds: [["Dia inteiro", 8_000, 2_000, 540, -7, 47]],
  },
  {
    slug: "improviso-geral", title: "Improviso Geral", tagline: "Nada está escrito, e é aí que mora a graça", category: "Teatro", producer: "Casa do Riso",
    venue: "Teatro Municipal da Lapa", address: "Rua da Lapa, 30", city: "Rio de Janeiro", state: "RJ", inDays: 22, doorsAt: "19h", startsAt: "20h", ageRating: "12 anos",
    description: ["Cinco atores criam na hora uma peça inteira a partir de uma palavra sugerida pelo público.", "Cada sessão é única: não dá para ver a mesma peça duas vezes."],
    schedule: [{ time: "20h", title: "Início da sessão" }, { time: "21h15", title: "Encerramento" }],
    included: ["Assento livre"], accent: "#7a4fc2", popularity: 55, style: "curtain", palette: ["#0f0a24", "#4b2f8f", "#3a2370", "#ffe9a8"],
    batchSeeds: [["Ingresso único", 6_500, 300, 118, -9, 22]],
  },
  {
    slug: "cidade-submersa", title: "Cidade Submersa", tagline: "Techno, água e luz em um reservatório antigo", category: "Eletrônica", producer: "Coletivo Frequência",
    venue: "Reservatório da Luz", address: "Rua da Represa, 12", city: "Belo Horizonte", state: "MG", inDays: 30, doorsAt: "21h", startsAt: "22h", ageRating: "18 anos",
    description: ["Uma pista instalada em um reservatório desativado, com projeções sobre a água e som pensado para a acústica do lugar.", "Capacidade limitada e entrada apenas com documento original."],
    schedule: [{ time: "22h", title: "Vera Lux" }, { time: "1h", title: "Kôa" }, { time: "4h", title: "Encerramento: Maré Baixa" }],
    included: ["Acesso à pista"], accent: "#1ba7a7", popularity: 75, style: "waves", palette: ["#04161f", "#0f6e78", "#1ba7a7", "#c8fff2"],
    batchSeeds: [["Pista · 1º lote", 11_000, 350, 349, -16, 30]],
  },
  {
    slug: "copa-das-ruas", title: "Copa das Ruas", tagline: "Basquete de rua com a arquibancada colada na quadra", category: "Esportes", producer: "Liga Norte",
    venue: "Quadra do Porto", address: "Cais do Porto, armazém 7", city: "Salvador", state: "BA", inDays: 38, doorsAt: "13h", startsAt: "14h", ageRating: "Livre",
    description: ["Oito times, formato eliminatório e DJ ao vivo durante todos os jogos.", "A final acontece ao pôr do sol, com a quadra iluminada."],
    schedule: [{ time: "14h", title: "Quartas de final" }, { time: "16h", title: "Semifinais" }, { time: "18h", title: "Final" }],
    included: ["Arquibancada livre"], accent: "#e2592a", popularity: 47, style: "bauhaus", palette: ["#1b2340", "#e2592a", "#f4b63f", "#f4efe2"],
    batchSeeds: [["Arquibancada", 3_000, 800, 210, -5, 38]],
  },
];

/** Eventos que já aconteceram: só aparecem no histórico de quem foi. */
const PAST_SEEDS: EventSeed[] = [
  {
    slug: "verao-em-cena", title: "Verão em Cena", tagline: "A temporada que abriu o ano", category: "Festivais", producer: "Maré Cultural",
    venue: "Parque das Dunas", address: "Av. Beira-Mar, 1200", city: "Recife", state: "PE", inDays: -62, doorsAt: "14h", startsAt: "15h", ageRating: "Livre",
    description: ["Edição encerrada."], schedule: [], included: [], accent: "#d96a2b", popularity: 0, style: "sunburst", palette: ["#14305a", "#2c6ea8", "#f2a93a", "#ffe28a"],
    batchSeeds: [["Pista", 12_000, 500, 500, -120, -62]],
  },
  {
    slug: "cafe-com-comedia", title: "Café com Comédia", tagline: "Uma manhã de riso", category: "Stand-up", producer: "Casa do Riso",
    venue: "Bar do Palco", address: "Rua Augusta, 512", city: "Belo Horizonte", state: "MG", inDays: -24, doorsAt: "10h", startsAt: "11h", ageRating: "Livre",
    description: ["Edição encerrada."], schedule: [], included: [], accent: "#c9942f", popularity: 0, style: "spotlight", palette: ["#1c120c", "#42291c", "#101014", "#ffe6a8"],
    batchSeeds: [["Mesa compartilhada", 5_000, 80, 80, -60, -24]],
  },
];

function build(seed: EventSeed, index: number, now: number): HubEvent {
  const { inDays, style, palette, batchSeeds, ...rest } = seed;
  const at = (days: number) => now + days * DAY_MS;
  return {
    ...rest,
    id: seed.slug,
    date: at(inDays),
    art: { style, palette, seed: 1000 + index * 37 },
    batches: batchSeeds.map(([name, priceCents, quantity, sold, startsIn, endsIn, note], batchIndex) => ({
      id: `${seed.slug}-${batchIndex + 1}`,
      name,
      note,
      priceCents,
      quantity,
      sold,
      startsAt: at(startsIn),
      endsAt: at(endsIn),
    })),
  };
}

export function buildCatalog(now: number): HubEvent[] {
  return SEEDS.map((seed, index) => build(seed, index, now));
}

export function pastEvents(now: number): HubEvent[] {
  return PAST_SEEDS.map((seed, index) => build(seed, index + 100, now));
}

const PRODUCER_ART: ArtSpec["palette"][] = [
  ["#1b1d20", "#c99b14", "#7f5f0a", "#f4f4f2"],
  ["#0b2a3a", "#1f9eb5", "#f2a93a", "#fff0c7"],
  ["#2a0f2e", "#c92f6b", "#f28a2e", "#ffd25a"],
];
const PRODUCER_STYLES: ArtStyle[] = ["sunburst", "bauhaus", "waves"];

function hash(text: string): number {
  return [...text].reduce((total, char) => (total * 31 + char.charCodeAt(0)) | 0, 7) >>> 0;
}

/** Premissa P-04: o hub reúne os eventos de todos os produtores, inclusive o da sessão. */
export function producerToHubEvent(event: ProducerEvent, data: PlatformData, producer: string): HubEvent {
  const code = hash(event.id);
  return {
    id: event.id,
    slug: event.id,
    title: event.name,
    tagline: `Por ${producer}`,
    category: "Outros",
    producer,
    venue: event.venue,
    address: event.venue,
    city: "A confirmar",
    state: "",
    date: event.date,
    doorsAt: "A confirmar",
    startsAt: "A confirmar",
    ageRating: "A confirmar",
    description: [`Evento publicado por ${producer}.`],
    schedule: [],
    included: [],
    art: { style: PRODUCER_STYLES[code % 3], palette: PRODUCER_ART[code % 3], seed: code % 10_000 },
    accent: "#c99b14",
    popularity: 50,
    fromProducer: true,
    batches: event.batches.map((batch) => ({
      id: batch.id,
      name: batch.name,
      priceCents: batch.priceCents,
      quantity: batch.quantity,
      sold: data.orders.reduce((sum, order) => (order.batchId === batch.id ? sum + order.quantity : sum), 0),
      startsAt: batch.startsAt,
      endsAt: batch.endsAt,
    })),
  };
}
