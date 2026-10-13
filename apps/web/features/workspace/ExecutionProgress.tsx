"use client";

import { useEffect, useState } from "react";
import { Alert } from "./ui";
import { api, errorMessage, type Schema } from "./api";

type Progress = Schema["ExecutionProgress"];
type Event = Schema["ProgressEvent"];
const stateLabels: Record<Schema["ExecutionState"], string> = {
  queued: "Na fila", leased: "Em execução", succeeded: "Testes concluídos", failed: "Falhou", cancelled: "Cancelada",
};
const toolLabels: Record<string, string> = { read: "Leitura", write: "Escrita", test: "Testes" };

export function ExecutionProgress({ path }: { path: string }) {
  const [progress, setProgress] = useState<Progress | null>(null);
  const [events, setEvents] = useState<Event[]>([]);
  const [connection, setConnection] = useState("Conectando…");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    let source: EventSource | undefined;
    function merge(incoming: Event[]) {
      setEvents(previous => [...new Map([...previous, ...incoming].map(item => [item.eventId, item])).values()]
        .sort((a, b) => Number(a.sequence) - Number(b.sequence)));
    }
    async function connect() {
      let cursor: string | null = null;
      do {
        const result: Progress = await api<Progress>(`${path}/progress${cursor ? `?after=${cursor}` : ""}`, { signal: controller.signal });
        if (controller.signal.aborted) return;
        setProgress(result); merge(result.events); cursor = result.nextCursor;
        if (result.events.length < 100) break;
      } while (!controller.signal.aborted);
      source = new EventSource(`/api/v1${path}/events`);
      source.onopen = () => { setConnection("Atualização ao vivo"); setError(null); };
      source.onerror = () => setConnection("Reconectando…");
      source.addEventListener("progress", event => {
        try { merge([JSON.parse((event as MessageEvent<string>).data) as Event]); }
        catch { source?.close(); setError("Resposta de progresso inválida. Reabra a execução."); }
      });
      source.addEventListener("state", event => {
        try {
          const result = JSON.parse((event as MessageEvent<string>).data) as Progress;
          setProgress(result);
          if (!["queued", "leased"].includes(result.execution.state)) {
            source?.close(); setConnection("Execução encerrada");
          }
        } catch { source?.close(); setError("Resposta de estado inválida. Reabra a execução."); }
      });
      source.addEventListener("access-ended", () => {
        source?.close(); setError("O acesso a esta execução foi encerrado.");
      });
    }
    void connect().catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => { controller.abort(); source?.close(); };
  }, [path]);

  return <section aria-label="Progresso da execução" className="progress">
    <div className="progress-meta">
      <span role="status">{connection}</span>
      {progress && <>
        <span>{stateLabels[progress.execution.state]} · tentativa {progress.execution.attempt}</span>
        <span>Custo estimado: {progress.estimatedCostUsd === null ? "desconhecido" : `US$ ${Number(progress.estimatedCostUsd).toFixed(6)}`}</span>
        {progress.execution.failureCode && <span>Motivo: {progress.execution.failureCode}</span>}
      </>}
    </div>
    {error && <Alert>{error}</Alert>}
    {!events.length && <p className="hint" style={{ marginTop: 12 }}>{progress?.execution.state === "queued" ? "Aguardando um runner do projeto assumir a execução." : "Aguardando o primeiro checkpoint."}</p>}
    <ol className="timeline">{events.map(event => <li key={event.eventId} data-ok={event.succeeded === null ? undefined : String(event.succeeded)}>
      <span className="what">{event.tool ? toolLabels[event.tool] ?? event.tool : "Início"}{event.path ? ` · ${event.path}` : ""}{event.succeeded !== null && (event.succeeded ? " · passou" : " · falhou")}</span>
      <span className="when"><time dateTime={event.timestamp}>{new Date(event.timestamp).toLocaleTimeString()}</time> · tentativa {event.attempt}
        {event.hash && <> · hash {event.hash.slice(0, 12)}</>} · <a href={`/api/v1${path}/reports/${event.eventId}`}>Relatório</a></span>
    </li>)}</ol>
    <p className="hint">Checkpoints e métricas persistidos. Conteúdo de arquivos e saídas livres das ferramentas não são exibidos.</p>
  </section>;
}
