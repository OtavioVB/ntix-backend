import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge } from "../../design-system";
import { artToImage, type ArtSpec } from "../../hub/art";
import type { HubEvent } from "../../hub/catalog";
import { eventDay, priceFrom, scarcity, type ExtraSold } from "../../hub/rules";
import { formatCents } from "../../platform/money";

type ImageSource = { title: string; imageUrl?: string; art: ArtSpec };

/** A imagem varia de evento para evento: foto real quando houver; se não houver ou falhar, a arte gerada. */
export function EventImage({ event, className, eager }: { event: ImageSource; className?: string; eager?: boolean }) {
  const art = useMemo(() => artToImage(event.art), [event.art]);
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [event.imageUrl]);
  return (
    <img
      className={["event-image", className].filter(Boolean).join(" ")}
      src={event.imageUrl && !failed ? event.imageUrl : art}
      alt=""
      loading={eager ? "eager" : "lazy"}
      decoding="async"
      onError={() => setFailed(true)}
    />
  );
}

export function PriceFrom({ event, now, extra }: { event: HubEvent; now: number; extra: ExtraSold }) {
  const price = priceFrom(event, now, extra);
  if (price === null) return <span className="price-from price-from--out">Esgotado</span>;
  return (
    <span className="price-from">
      <small>A partir de</small>
      <b>{formatCents(price)}</b>
    </span>
  );
}

export function EventCard({ event, now, extra, rank }: { event: HubEvent; now: number; extra: ExtraSold; rank?: number }) {
  const day = eventDay(event.date);
  const flag = scarcity(event, now, extra);

  return (
    <Link className={rank ? "event-tile event-tile--ranked" : "event-tile"} to={`/eventos/${event.slug}`}>
      {rank && <span className="event-tile__rank" aria-hidden="true">{rank}</span>}
      <span className="event-tile__poster">
        <EventImage event={event} />
        {flag === "LAST" && <span className="event-tile__flag">Últimos ingressos</span>}
        {flag === "SOLD_OUT" && <span className="event-tile__flag event-tile__flag--out">Esgotado</span>}
      </span>
      <span className="event-tile__stub">
        <span className="event-tile__date" aria-label={day.full}>
          <b>{day.day}</b>
          <small>{day.month}</small>
        </span>
        <span className="event-tile__info">
          <strong>{event.title}</strong>
          <span>{event.city}{event.state ? `, ${event.state}` : ""}</span>
          <PriceFrom event={event} now={now} extra={extra} />
        </span>
      </span>
    </Link>
  );
}

export function Rail({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    <section className="rail" aria-label={title}>
      <header className="rail__head">
        <h2 className="n-display n-display--sm">{title}</h2>
        {subtitle && <p>{subtitle}</p>}
      </header>
      <div className="rail__track" tabIndex={0} role="group" aria-label={`Eventos: ${title}. Role para ver mais.`}>
        {children}
      </div>
    </section>
  );
}

export function CategoryBadge({ event }: { event: HubEvent }) {
  return <Badge>{event.category}</Badge>;
}
