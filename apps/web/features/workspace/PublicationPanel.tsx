"use client";

import { useEffect, useState } from "react";
import { DiffDialog } from "../review/DiffDialog";
import { ReviewPanel } from "./ReviewPanel";
import { Alert, Icon } from "./ui";
import { api, errorMessage, type Schema } from "./api";

export function PublicationPanel({ path }: { path: string }) {
  const [status, setStatus] = useState<Schema["PublicationStatus"] | null>(null);
  const [busy, setBusy] = useState(false);
  const [diffOpen, setDiffOpen] = useState(false);
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
  const [, organizationId = "", projectId = "", taskId = "", executionId = ""] = path.match(/^\/organizations\/([^/]+)\/projects\/([^/]+)\/tasks\/([^/]+)\/executions\/([^/]+)$/) ?? [];
  return <section aria-label="Publicação da revisão" className="step-list">
    <h4>Alterações para revisão</h4>
    {error && <Alert>{error}</Alert>}
    {status?.pullRequest ? <>
      <div className="review-status">
        <button className="sm" onClick={() => setDiffOpen(true)}><Icon name="eye" size="sm" />Ver diferenças</button>
        <span className="hint">Commit <span className="commit">{status.pullRequest.headCommit.slice(0, 12)}</span> · revise as alterações e os testes antes de aprovar.</span>
      </div>
      <ReviewPanel path={path} />
      {diffOpen && <DiffDialog scope={{ organizationId, projectId, taskId, executionId }} onClose={() => setDiffOpen(false)} />}
    </> : <>
      <p role="status">{status?.state === "publishing" ? <span className="loading-line" style={{ padding: 0 }}><span className="spinner" aria-hidden="true" />Publicando…</span> : "Nenhuma revisão preparada."}</p>
      {status?.failureCode && <Alert tone="warning">Motivo: {status.failureCode}</Alert>}
      <div><button onClick={() => void publish()} disabled={busy || status?.state === "publishing" || Number(status?.attempts ?? 0) >= 3}>
        {busy ? "Publicando…" : "Preparar ou reconciliar revisão"}
      </button></div>
      <p className="hint">Disponível após os testes. A publicação precisa estar habilitada pelo administrador.</p>
    </>}
  </section>;
}
