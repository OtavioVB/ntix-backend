import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ButtonLink, Scene } from "../../design-system";
import { CATEGORIES, type Category, type HubEvent } from "../../hub/catalog";
import { eventDay, filterEvents, type WhenFilter } from "../../hub/rules";
import { useHub } from "../../hub/useHub";
import { EventCard, EventImage, PriceFrom, Rail } from "./parts";
import { producerAreaHref } from "../../producer/paths";

const WHEN_OPTIONS: { value: WhenFilter; label: string }[] = [
  { value: "", label: "Qualquer data" },
  { value: "7", label: "Próximos 7 dias" },
  { value: "30", label: "Próximos 30 dias" },
  { value: "60", label: "Próximos 60 dias" },
];

const ROTATION_MS = 7000;

function Spotlight({ events }: { events: HubEvent[] }) {
  const { now, extra } = useHub();
  const [active, setActive] = useState(0);
  const [paused, setPaused] = useState(false);
  const event = events[active];
  const day = eventDay(event.date);

  useEffect(() => {
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
    if (paused || reduced) return;
    const timer = setInterval(() => setActive((current) => (current + 1) % events.length), ROTATION_MS);
    return () => clearInterval(timer);
  }, [paused, events.length]);

  return (
    <Scene
      light="palco"
      fixed
      className="spotlight"
      aria-roledescription="carrossel"
      aria-label="Eventos em destaque"
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
    >
      <div className="spotlight__images" aria-hidden="true">
        {events.map((item, index) => (
          <div className={index === active ? "spotlight__image is-active" : "spotlight__image"} key={item.id}>
            <EventImage event={item} eager />
          </div>
        ))}
      </div>
      <div className="spotlight__scrim" />
      <div className="n-container spotlight__content">
        <div className="spotlight__copy" key={event.id}>
          <p className="n-label n-label--accent">{event.category} · {event.city}, {event.state}</p>
          <h1 className="n-display n-display--lg spotlight__title">{event.title}</h1>
          <p className="n-lead spotlight__tagline">{event.tagline}</p>
          <p className="spotlight__when">{day.full} · {event.venue}</p>
          <div className="spotlight__action">
            <ButtonLink to={`/eventos/${event.slug}`} size="lg" arrow>Ver ingressos</ButtonLink>
            <PriceFrom event={event} now={now} extra={extra} />
          </div>
        </div>
        <ul className="spotlight__thumbs" aria-label="Escolher destaque">
          {events.map((item, index) => (
            <li key={item.id}>
              <button
                type="button"
                className={index === active ? "spotlight__thumb is-active" : "spotlight__thumb"}
                aria-current={index === active ? "true" : undefined}
                aria-label={`Ver destaque: ${item.title}`}
                onClick={() => setActive(index)}
              >
                <EventImage event={item} eager />
                <span>{item.title}</span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    </Scene>
  );
}

export function HubHomePage() {
  const { events, extra, now } = useHub();
  const [params, setParams] = useSearchParams();

  const query = params.get("q") ?? "";
  const category = (params.get("categoria") ?? "") as Category | "";
  const city = params.get("cidade") ?? "";
  const when = (params.get("quando") ?? "") as WhenFilter;
  const filtering = Boolean(query || category || city || when);

  const upcoming = useMemo(() => events.filter((event) => event.date >= now), [events, now]);
  const cities = useMemo(() => [...new Set(upcoming.map((event) => event.city))].filter((name) => name !== "A confirmar").sort(), [upcoming]);
  const results = useMemo(() => filterEvents(events, { query, category, city, when }, now), [events, query, category, city, when, now]);
  const featured = useMemo(() => upcoming.filter((event) => event.featured), [upcoming]);
  const trending = useMemo(() => [...upcoming].sort((a, b) => b.popularity - a.popularity).slice(0, 5), [upcoming]);
  const soon = useMemo(() => [...upcoming].sort((a, b) => a.date - b.date).slice(0, 8), [upcoming]);
  const music = upcoming.filter((event) => event.category === "Shows" || event.category === "Festivais");
  const stage = upcoming.filter((event) => event.category === "Teatro" || event.category === "Stand-up");
  const family = upcoming.filter((event) => ["Infantil", "Gastronomia", "Esportes"].includes(event.category));
  const fromProducers = upcoming.filter((event) => event.fromProducer);

  function setFilter(key: string, value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  }

  const card = (event: HubEvent, rank?: number) => <EventCard key={event.id} event={event} now={now} extra={extra} rank={rank} />;

  return (
    <>
      <title>Ntix: encontre seu próximo evento</title>
      <meta name="description" content="Shows, festivais, teatro, stand-up e muito mais. Encontre seu próximo evento e compre ingressos na Ntix." />

      {!filtering && featured.length > 0 && <Spotlight events={featured} />}

      <Scene light="penumbra" className="hub-body" id="eventos">
        <div className="n-container">
          {filtering && <h1 className="n-display n-display--md hub-body__title">{query ? `Resultados para "${query}"` : "Encontre seu próximo evento"}</h1>}

          <div className="hub-filters">
            <div className="hub-chips" role="group" aria-label="Categorias">
              <button type="button" className="hub-chip" aria-pressed={!category} onClick={() => setFilter("categoria", "")}>Todos</button>
              {CATEGORIES.map((name) => (
                <button key={name} type="button" className="hub-chip" aria-pressed={category === name} onClick={() => setFilter("categoria", category === name ? "" : name)}>{name}</button>
              ))}
            </div>
            <div className="hub-selects">
              <div>
                <label className="n-visually-hidden" htmlFor="hub-city">Cidade</label>
                <select id="hub-city" className="n-input" value={city} onChange={(event) => setFilter("cidade", event.target.value)}>
                  <option value="">Todas as cidades</option>
                  {cities.map((name) => <option key={name} value={name}>{name}</option>)}
                </select>
              </div>
              <div>
                <label className="n-visually-hidden" htmlFor="hub-when">Quando</label>
                <select id="hub-when" className="n-input" value={when} onChange={(event) => setFilter("quando", event.target.value)}>
                  {WHEN_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
              </div>
            </div>
          </div>

          {filtering ? (
            <section aria-live="polite" aria-label="Resultados">
              <p className="hub-count">
                {results.length === 1 ? "1 evento encontrado" : `${results.length} eventos encontrados`}
                {" · "}
                <Link to="/#eventos">Limpar filtros</Link>
              </p>
              {results.length === 0 ? (
                <div className="hub-empty">
                  <h2 className="n-title">Nada por aqui com esses filtros</h2>
                  <p>Tente outra cidade, outra data ou uma busca mais ampla.</p>
                </div>
              ) : (
                <div className="event-grid">{results.map((event) => card(event))}</div>
              )}
            </section>
          ) : (
            <>
              <Rail title="Em alta agora" subtitle="Os eventos que mais estão chamando atenção.">
                {trending.map((event, index) => card(event, index + 1))}
              </Rail>
              {fromProducers.length > 0 && <Rail title="Acabaram de chegar">{fromProducers.map((event) => card(event))}</Rail>}
              <Rail title="Vem aí" subtitle="Os próximos da agenda, do mais perto ao mais distante.">{soon.map((event) => card(event))}</Rail>
              <Rail title="Música ao vivo">{music.map((event) => card(event))}</Rail>
              <Rail title="Teatro e humor">{stage.map((event) => card(event))}</Rail>
              <Rail title="Para ir com todo mundo">{family.map((event) => card(event))}</Rail>
            </>
          )}
        </div>
      </Scene>

      <Scene light="ouro" className="hub-cta">
        <div className="n-container hub-cta__inner">
          <h2 className="n-display n-display--md">Produz eventos? Venda aqui também.</h2>
          <ButtonLink to={producerAreaHref("signup")} size="lg" arrow>Conhecer a Ntix para produtores</ButtonLink>
        </div>
      </Scene>
    </>
  );
}
