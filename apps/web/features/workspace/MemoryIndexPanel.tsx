"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { Alert } from "./ui";
import { api, errorMessage, type Schema } from "./api";

const indexLabels: Record<string, { label: string; tone: string }> = {
  queued: { label: "Na fila", tone: "info" }, processing: { label: "Preparando", tone: "info" }, completed: { label: "Indexado", tone: "success" },
  failed: { label: "Falhou", tone: "danger" }, uncertain: { label: "Resultado incerto", tone: "warning" },
};

export function MemoryIndexPanel({ path, source }: { path: string; source: Schema["MemoryDto"] }) {
  const [indexes, setIndexes] = useState<Schema["IndexDto"][]>([]);
  const [profiles, setProfiles] = useState<Schema["ProfileDto"][]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const requestKey = useRef<{ payload: string; key: string } | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      api<Schema["PageResultOfIndexDto"]>(`${path}/memory/${source.id}/indexes?page=1&pageSize=20`, { signal: controller.signal }),
      api<Schema["PageResultOfProfileDto"]>(`${path}/profiles/?page=1&pageSize=100`, { signal: controller.signal }),
    ]).then(([jobs, choices]) => { setIndexes(jobs.items); setProfiles(choices.items.filter(p => !p.revokedAt)); })
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => controller.abort();
  }, [path, source.id, source.version, refresh]);
  async function queue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null);
    const data = new FormData(event.currentTarget);
    const input = { version: source.version, profileId: String(data.get("profile")), maxCostUsd: Number(data.get("budget")) };
    const payload = JSON.stringify(input);
    if (requestKey.current?.payload !== payload) requestKey.current = { payload, key: crypto.randomUUID() };
    try {
      await api<Schema["IndexDto"]>(`${path}/memory/${source.id}/indexes`, { method: "POST", body: JSON.stringify({ ...input, idempotencyKey: requestKey.current.key }) });
      requestKey.current = null; setRefresh(value => value + 1);
    } catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  return <section className="card" aria-label="Indexação da memória">
    <div className="card-header"><div><h2>Índice de contexto</h2><p>O índice é textual e local: não chama IA nem gera embeddings.</p></div>
      <button className="sm" style={{ marginLeft: "auto" }} onClick={() => setRefresh(value => value + 1)}>Atualizar indexação</button></div>
    <div className="card-body stack">
      {error && <Alert>{error}</Alert>}
      {indexes.length > 0 && <ul className="index-list">{indexes.map(job => {
        const state = indexLabels[job.state] ?? { label: job.state, tone: "" };
        return <li key={job.id}><span className={`badge ${state.tone}`}>{state.label}</span>
          <span>{job.sourceVersion !== source.version ? "Histórico; a fonte mudou" : "Versão atual"} · {job.model}</span>
          {job.failureCode && <span className="hint">{job.failureCode}</span>}</li>;
      })}</ul>}
      {source.status !== "validated" && <Alert tone="info">Valide a fonte antes de indexar.</Alert>}
      {profiles.length === 0 && <Alert tone="info">Configure um perfil ativo para indexar o contexto.</Alert>}
      <form onSubmit={queue} aria-label="Solicitar indexação"><fieldset disabled={busy || source.status !== "validated" || profiles.length === 0}>
        <label>Perfil para indexação<select name="profile" required>{profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}</select></label>
        <label style={{ maxWidth: 240 }}>Orçamento de indexação em US$<input name="budget" type="number" min="0.01" max="5" step="0.01" defaultValue="0.05" required /></label>
        <div className="actions"><button>Indexar ou reconstruir contexto</button></div>
      </fieldset></form>
    </div>
  </section>;
}
