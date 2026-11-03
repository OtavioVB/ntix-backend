import { QRCodeSVG } from "qrcode.react";
import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Button, Icon, Meter, Scene } from "../../../design-system";
import { signal, useQrScanner } from "../../../features/entry/useQrScanner";
import { admitTicket, entryStats, normalizeCode, sampleTickets, type EntryResult } from "../../../platform/entry";
import { fullDate } from "../../../platform/selectors";
import type { ProducerEvent } from "../../../platform/types";
import { useProducerSession } from "../../../producer/session";
import { usePlatform } from "./usePlatform";

/** Entrada liberada some sozinha para a fila andar; recusa espera um toque, para ninguém passar sem ver. */
const ADMITTED_MS = 2200;
/** Tempo em que o mesmo QR lido de novo pela câmera é ignorado. Digitado, vale sempre. */
const SAME_TICKET_MS = 10_000;

function time(at: number) {
  return new Date(at).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

const RESULT_TEXT: Record<EntryResult["kind"], string> = {
  ADMITTED: "Entrada liberada",
  ALREADY_USED: "Ingresso já utilizado",
  OTHER_EVENT: "Ingresso de outro evento",
  NOT_FOUND: "Ingresso não encontrado",
};

function resultDetail(result: EntryResult) {
  switch (result.kind) {
    case "ADMITTED":
      return [result.batchName, result.holder].filter(Boolean).join(" · ");
    case "ALREADY_USED":
      return `Entrou às ${time(result.at)}, validado por ${result.by}.`;
    case "OTHER_EVENT":
      return `Este ingresso é de ${result.eventName}.`;
    case "NOT_FOUND":
      return "O código não pertence a nenhum ingresso da sua produtora.";
  }
}

/** O evento de hoje, ou o próximo; sem nenhum dos dois, o mais recente. */
function defaultEvent(events: ProducerEvent[], now: number): string {
  const startOfDay = new Date(now).setHours(0, 0, 0, 0);
  const upcoming = events.filter((event) => event.date >= startOfDay).sort((a, b) => a.date - b.date)[0];
  return (upcoming ?? [...events].sort((a, b) => b.date - a.date)[0])?.id ?? "";
}

function ResultCard({ result, onNext }: { result: EntryResult; onNext: () => void }) {
  const ok = result.kind === "ADMITTED";
  return (
    <div className={ok ? "entry-result is-ok" : "entry-result is-refused"} role="alert">
      <span className="entry-result__icon"><Icon name={ok ? "check" : "close"} /></span>
      <p className="entry-result__title">{RESULT_TEXT[result.kind]}</p>
      <p className="entry-result__detail">{resultDetail(result)}</p>
      <p className="entry-result__code n-figure">{result.code}</p>
      <Button variant={ok ? "ghost" : "primary"} onClick={onNext}>{ok ? "Ler próximo" : "Entendi, ler próximo"}</Button>
    </div>
  );
}

export function EntryPage() {
  const { data, save } = usePlatform();
  const { session } = useProducerSession();
  const events = useMemo(() => data.events.filter((event) => event.status !== "DRAFT"), [data.events]);
  const [eventId, setEventId] = useState(() => defaultEvent(events, Date.now()));
  const [result, setResult] = useState<EntryResult>();
  const [history, setHistory] = useState<EntryResult[]>([]);
  const [typed, setTyped] = useState("");
  const videoRef = useRef<HTMLVideoElement>(null);
  const dataRef = useRef(data);
  const lastScanRef = useRef({ code: "", at: 0 });
  dataRef.current = data;

  const validator = session.team.find((member) => member.isAdmin)?.name ?? session.adminEmail;
  const event = events.find((candidate) => candidate.id === eventId);
  const stats = entryStats(data, eventId);

  function validateScanned(raw: string) {
    // Quem acabou de entrar ainda está com o QR na frente da câmera: não vira "já utilizado" na cara dele.
    const last = lastScanRef.current;
    const now = Date.now();
    if (normalizeCode(raw) === last.code && now - last.at < SAME_TICKET_MS) return;
    lastScanRef.current = { code: normalizeCode(raw), at: now };
    validate(raw);
  }

  function validate(raw: string) {
    // Lê o dado mais novo: duas leituras seguidas não podem enxergar o mesmo estado antigo.
    const outcome = admitTicket(dataRef.current, eventId, raw, Date.now(), validator);
    if (outcome.data !== dataRef.current) {
      dataRef.current = outcome.data;
      save(outcome.data);
    }
    setResult(outcome.result);
    setHistory((items) => [outcome.result, ...items].slice(0, 8));
    signal(outcome.result.kind === "ADMITTED");
  }

  const scanner = useQrScanner(videoRef, validateScanned, Boolean(result));

  useEffect(() => {
    if (result?.kind !== "ADMITTED") return;
    const timer = setTimeout(() => setResult(undefined), ADMITTED_MS);
    return () => clearTimeout(timer);
  }, [result]);

  function handleTyped(formEvent: FormEvent) {
    formEvent.preventDefault();
    if (!typed.trim()) return;
    validate(typed);
    setTyped("");
  }

  if (events.length === 0) {
    return (
      <>
        <title>Portaria · Ntix</title>
        <header className="page-head"><h1 className="n-display n-display--md">Portaria</h1></header>
        <section className="tile empty-state">
          <h2 className="n-title">Nenhum evento publicado</h2>
          <p>Publique um evento e, no dia, valide os ingressos aqui pela câmera do celular.</p>
        </section>
      </>
    );
  }

  const samples = sampleTickets(data, eventId, 3);

  return (
    <>
      <title>Portaria · Ntix</title>
      <header className="page-head">
        <h1 className="n-display n-display--md">Portaria</h1>
      </header>

      <div className="entry-layout">
        <div className="entry-main">
          <div className="entry-bar tile">
            <div className="n-field">
              <label className="n-field__label" htmlFor="entry-event">Evento</label>
              <select id="entry-event" className="n-input" value={eventId} onChange={(change) => { setEventId(change.target.value); setResult(undefined); }}>
                {events.map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.name} · {fullDate(candidate.date)}</option>)}
              </select>
            </div>
            <div className="entry-count" aria-live="polite">
              <p><strong className="n-figure">{stats.admitted.toLocaleString("pt-BR")}</strong> de {stats.issued.toLocaleString("pt-BR")} entraram</p>
              <Meter value={stats.admitted} max={Math.max(stats.issued, 1)} label={`${stats.admitted} de ${stats.issued} ingressos já entraram`} />
            </div>
          </div>

          <Scene light="palco" fixed className="entry-scanner" aria-label={`Leitor de ingressos de ${event?.name ?? ""}`}>
            <video ref={videoRef} className="entry-scanner__video" muted playsInline hidden={scanner.state !== "SCANNING"} />
            {scanner.state === "SCANNING" && <div className="entry-scanner__frame" aria-hidden="true"><span /></div>}

            {scanner.state !== "SCANNING" && (
              <div className="entry-scanner__intro">
                <span className="entry-scanner__glyph"><Icon name="scan" /></span>
                {scanner.state === "DENIED" ? (
                  <>
                    <p className="entry-scanner__title">A câmera foi bloqueada</p>
                    <p>Libere a câmera para este site nas configurações do navegador e toque de novo. Enquanto isso, dá para digitar o código abaixo.</p>
                    <Button onClick={scanner.start}>Tentar de novo</Button>
                  </>
                ) : scanner.state === "UNAVAILABLE" ? (
                  <>
                    <p className="entry-scanner__title">Câmera indisponível aqui</p>
                    <p>A leitura precisa de um aparelho com câmera e do site em conexão segura (https). Digite o código do ingresso abaixo.</p>
                  </>
                ) : (
                  <>
                    <p className="entry-scanner__title">Aponte para o QR Code do ingresso</p>
                    <p>O navegador vai pedir para usar a câmera. A imagem não é gravada nem enviada: só o código lido é conferido.</p>
                    <Button size="lg" arrow disabled={scanner.state === "STARTING"} onClick={scanner.start}>
                      {scanner.state === "STARTING" ? "Abrindo a câmera…" : "Abrir câmera"}
                    </Button>
                  </>
                )}
              </div>
            )}

            {scanner.state === "SCANNING" && !result && (
              <button type="button" className="entry-scanner__stop" onClick={scanner.stop}>Fechar câmera</button>
            )}
            {result && <ResultCard result={result} onNext={() => setResult(undefined)} />}
          </Scene>

          <form className="entry-manual tile" onSubmit={handleTyped}>
            <div className="n-field">
              <label className="n-field__label" htmlFor="entry-code">Código do ingresso</label>
              <input id="entry-code" className="n-input n-figure" autoComplete="off" autoCapitalize="characters" spellCheck={false} placeholder="Ex.: NT-10001-01" value={typed} onChange={(change) => setTyped(change.target.value)} />
            </div>
            <Button type="submit" variant="ghost">Validar</Button>
          </form>
        </div>

        <aside className="entry-side">
          <section className="tile" aria-labelledby="entry-history-title">
            <h2 className="tile__title" id="entry-history-title">Últimas leituras</h2>
            {history.length === 0 ? (
              <p className="tile__note">As leituras deste aparelho aparecem aqui.</p>
            ) : (
              <ul className="entry-history">
                {history.map((item, index) => (
                  <li key={`${item.code}-${index}`} className={item.kind === "ADMITTED" ? "is-ok" : "is-refused"}>
                    <Icon name={item.kind === "ADMITTED" ? "check" : "close"} />
                    <span><b className="n-figure">{item.code}</b>{RESULT_TEXT[item.kind]}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {samples.length > 0 && (
            <details className="tile entry-samples">
              <summary>Ingressos para testar</summary>
              <p className="tile__note">Demonstração: abra esta tela no celular e leia estes códigos na tela do computador.</p>
              <ul>
                {samples.map((code) => (
                  <li key={code}>
                    <QRCodeSVG value={code} size={112} marginSize={2} title={`QR Code do ingresso ${code}`} />
                    <span className="n-figure">{code}</span>
                  </li>
                ))}
              </ul>
            </details>
          )}
        </aside>
      </div>
    </>
  );
}
