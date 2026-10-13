"use client";

import { useEffect, useState } from "react";
import { ReviewPanel } from "./ReviewPanel";
import { api, errorMessage, type Schema } from "./api";

export function PublicationPanel({ path }: { path: string }) {
  const [status, setStatus] = useState<Schema["PublicationStatus"] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    async function refresh() {
      try { setStatus(await api<Schema["PublicationStatus"]>(`${path}/publication`, { signal: controller.signal })); }
      catch (failure) { if (!controller.signal.aborted) setError(errorMessage(failure)); }
    }
    void refresh();
    const timer = setInterval(() => { void refresh(); }, 10000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [path]);
  async function publish() {
    setBusy(true); setError(null);
    try { setStatus(await api<Schema["PublicationStatus"]>(`${path}/publication`, { method: "POST" })); }
    catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  return <section aria-label="Publicação da revisão">
    <h3>Alterações para revisão</h3>
    {error && <p role="alert" className="error">{error}</p>}
    {status?.pullRequest ? <>
      <a href={status.pullRequest.url.includes("/repository/local/reviews/") ? `/reviews${path.replace("/organizations/", "/").replace("/projects/", "/").replace("/tasks/", "/").replace("/executions/", "/")}` : status.pullRequest.url} target="_blank" rel="noreferrer">{status.pullRequest.url.includes("/repository/local/reviews/") ? "Abrir diff local" : `Abrir PR #${status.pullRequest.number}`}</a>
      <p className="hint">Commit: {status.pullRequest.headCommit.slice(0, 12)}. Revise as alterações e os testes antes de aprovar.</p>
      <ReviewPanel path={path} local={status.pullRequest.url.includes("/repository/local/reviews/")} />
    </> : <>
      <p>{status?.state === "publishing" ? "Publicando…" : "Nenhuma revisão preparada."}</p>
      {status?.failureCode && <p>Motivo: {status.failureCode}</p>}
      <button onClick={() => void publish()} disabled={busy || status?.state === "publishing" || Number(status?.attempts ?? 0) >= 3}>
        {busy ? "Publicando…" : "Preparar ou reconciliar revisão"}
      </button>
      <p className="hint">Disponível após os testes. A publicação precisa estar habilitada pelo administrador.</p>
    </>}
  </section>;
}
