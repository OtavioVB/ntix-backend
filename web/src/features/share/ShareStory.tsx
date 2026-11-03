import "./share.css";

import { useEffect, useRef, useState } from "react";
import type { CustomerOrder } from "../../customer/orders";
import { Button } from "../../design-system";
import { firstNameOf } from "../../hub/rules";
import type { AvatarConfig } from "../avatar/avatarOptions";
import { TARGET_NAMES, downloadBlob, shareStory, storyFileName, type ShareTarget } from "./socialShare";
import { buildStoryImage } from "./story";

const TARGETS: ShareTarget[] = ["instagram", "tiktok", "whatsapp"];

/** Espera o avatar parar de mudar antes de remontar a imagem. */
const REBUILD_DELAY_MS = 450;

type ShareStoryProps = {
  order: CustomerOrder;
  avatar: AvatarConfig;
  customerName: string;
};

type Stage = { status: "building" } | { status: "ready"; blob: Blob; url: string } | { status: "failed" };

/** Monta o story desta compra e leva para as redes. A imagem acompanha o avatar se ele mudar. */
export function ShareStory({ order, avatar, customerName }: ShareStoryProps) {
  const [stage, setStage] = useState<Stage>({ status: "building" });
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState<ShareTarget | null>(null);
  const urlRef = useRef<string | null>(null);
  const firstName = firstNameOf(customerName);
  const avatarKey = JSON.stringify(avatar);

  useEffect(() => {
    let active = true;
    const timer = setTimeout(() => {
      buildStoryImage(order, avatar, firstName)
        .then((blob) => {
          if (!active) return;
          const url = URL.createObjectURL(blob);
          if (urlRef.current) URL.revokeObjectURL(urlRef.current);
          urlRef.current = url;
          setStage({ status: "ready", blob, url });
        })
        .catch(() => {
          if (active) setStage({ status: "failed" });
        });
    }, REBUILD_DELAY_MS);
    return () => {
      active = false;
      clearTimeout(timer);
    };
    // O avatar é comparado pelo conteúdo: um objeto novo igual não remonta a imagem.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [order.code, avatarKey, firstName]);

  useEffect(
    () => () => {
      if (urlRef.current) URL.revokeObjectURL(urlRef.current);
    },
    [],
  );

  async function share(target: ShareTarget) {
    if (stage.status !== "ready" || busy) return;
    setBusy(target);
    setMessage("");
    const result = await shareStory(target, {
      blob: stage.blob,
      slug: order.event.slug,
      eventTitle: order.event.title,
      eventUrl: `${window.location.origin}/eventos/${order.event.slug}`,
    });
    setMessage(result.message);
    setBusy(null);
  }

  const ready = stage.status === "ready";

  return (
    <section className="share-story" aria-labelledby="share-story-title">
      <div className="share-story__preview">
        {ready && <img src={stage.url} alt={`Story com seu avatar segurando o ingresso de ${order.event.title}`} />}
        {stage.status === "building" && <p>Montando seu story...</p>}
        {stage.status === "failed" && <p>Não conseguimos montar a imagem neste navegador.</p>}
      </div>
      <div className="share-story__body">
        <h2 className="n-title" id="share-story-title">Conta pra todo mundo que você vai</h2>
        <p className="share-story__text">Fizemos um story só seu, com a arte de {order.event.title} e o seu avatar com o ingresso na mão.</p>
        <div className="share-story__targets">
          {TARGETS.map((target) => (
            <Button key={target} variant={target === "instagram" ? "primary" : "ghost"} disabled={!ready || Boolean(busy)} onClick={() => share(target)}>
              {TARGET_NAMES[target]}
            </Button>
          ))}
        </div>
        <button
          type="button"
          className="share-story__download"
          disabled={!ready}
          onClick={() => {
            if (ready) downloadBlob(stage.blob, storyFileName(order.event.slug));
          }}
        >
          Só baixar a imagem
        </button>
        <p className="share-story__status" role="status">{message}</p>
        <p className="signup__note">No celular, abre a lista de aplicativos do seu aparelho com a imagem já anexada. O código do seu ingresso nunca aparece no story, então pode postar sem medo.</p>
      </div>
    </section>
  );
}
