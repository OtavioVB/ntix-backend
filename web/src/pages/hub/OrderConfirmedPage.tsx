import { useState, type CSSProperties } from "react";
import { Navigate, useParams } from "react-router-dom";
import { Button, ButtonLink, Scene, Stamp } from "../../design-system";
import { ticketCount } from "../../customer/orders";
import { useCustomer } from "../../customer/session";
import { Avatar } from "../../features/avatar/Avatar";
import { AvatarBuilder } from "../../features/avatar/AvatarBuilder";
import { ShareStory } from "../../features/share/ShareStory";
import { eventDay } from "../../hub/rules";
import { EventImage } from "./parts";

const CONFETTI = Array.from({ length: 26 }, (_, index) => ({
  left: `${(index * 37) % 100}%`,
  delay: `${(index % 9) * 110}ms`,
  duration: `${1500 + (index % 5) * 260}ms`,
  rotate: `${(index * 53) % 360}deg`,
  gold: index % 3 !== 0,
}));

/** O momento da compra: o avatar da pessoa, no palco, com o ingresso na mão. */
export function OrderConfirmedPage() {
  const { code } = useParams();
  const { customer, update, avatarOrDefault } = useCustomer();
  const order = customer.orders.find((candidate) => candidate.code === code);
  const [editing, setEditing] = useState(!customer.avatar);

  if (!customer.loggedIn) return <Navigate to="/conta/entrar" replace />;
  if (!order) return <Navigate to="/conta/pedidos" replace />;
  if (order.payment.status !== "PAID") return <Navigate to={`/conta/pedidos/${order.code}`} replace />;

  const count = ticketCount(order.items);
  const day = eventDay(order.event.date);
  const firstName = customer.account?.name.split(" ")[0] ?? "";
  const style = { "--event-accent": order.event.accent } as CSSProperties;

  return (
    <Scene light="palco" fixed className="stage" style={style}>
      <title>{`Ingresso garantido: ${order.event.title} · Ntix`}</title>
      <div className="stage__bg" aria-hidden="true"><EventImage event={order.event} eager /></div>
      <div className="stage__scrim" />
      <div className="stage__confetti" aria-hidden="true">
        {CONFETTI.map((piece, index) => (
          <i
            key={index}
            className={piece.gold ? "is-gold" : undefined}
            style={{ left: piece.left, animationDelay: piece.delay, animationDuration: piece.duration, rotate: piece.rotate }}
          />
        ))}
      </div>

      <div className="n-container stage__grid">
        <div className="stage__performer">
          <div className="stage__beam" aria-hidden="true" />
          <Avatar
            config={avatarOrDefault}
            ticket={{ title: order.event.title, count, accent: order.event.accent }}
            label={`Seu avatar segurando ${count === 1 ? "o ingresso" : `${count} ingressos`} de ${order.event.title}`}
          />
          <div className="stage__floor" aria-hidden="true" />
        </div>

        <div className="stage__copy">
          <Stamp>Ingresso garantido</Stamp>
          <h1 className="n-display n-display--md">{firstName ? `É isso, ${firstName}!` : "É isso!"} Você vai.</h1>
          <p className="n-lead">
            {count === 1 ? "Seu ingresso" : `Seus ${count} ingressos`} para <strong>{order.event.title}</strong> já {count === 1 ? "está" : "estão"} na sua conta.
          </p>
          <p className="stage__when">{day.full} · {order.event.venue}, {order.event.city}</p>
          <div className="stage__actions">
            <ButtonLink to="/conta/ingressos" size="lg" arrow>Ver meus ingressos</ButtonLink>
            <Button variant="ghost" size="lg" aria-expanded={editing} onClick={() => setEditing((open) => !open)}>
              {editing ? "Fechar avatar" : "Mudar meu avatar"}
            </Button>
          </div>
          <p className="signup__note">Pedido {order.code}. Demonstração: nenhuma cobrança foi feita.</p>
        </div>
      </div>

      <div className="n-container stage__share">
        <ShareStory order={order} avatar={avatarOrDefault} customerName={customer.account?.name ?? ""} />
      </div>

      {editing && (
        <div className="n-container stage__builder">
          <h2 className="n-title">{customer.avatar ? "Ajuste seu avatar" : "Quem está segurando esse ingresso? Monte seu avatar."}</h2>
          <p className="stage__builder-note">Cada mudança aparece no palco na hora e vale para todos os seus ingressos.</p>
          <AvatarBuilder value={avatarOrDefault} onChange={(avatar) => update({ avatar })} />
        </div>
      )}
    </Scene>
  );
}
