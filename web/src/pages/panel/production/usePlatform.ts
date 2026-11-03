import { emptyPlatformData } from "../../../platform/demoData";
import type { PlatformData } from "../../../platform/types";
import { useProducerSession } from "../../../producer/session";

/** Dados do produtor aprovado. Hoje vêm da sessão local; é aqui que a API vai entrar. */
export function usePlatform() {
  const { session, update } = useProducerSession();
  return {
    data: session.platform ?? emptyPlatformData(Date.now()),
    hasStore: session.ticketeria === "ACTIVE",
    save: (platform: PlatformData) => update({ platform }),
  };
}
