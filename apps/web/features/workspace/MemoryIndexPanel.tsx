"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, errorMessage, type Schema } from "./api";

const indexLabels: Record<string, string> = { queued: "Na fila", processing: "Preparando", calling: "Gerando embeddings", completed: "Indexado", failed: "Falhou", uncertain: "Resultado incerto" };

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
  return <section aria-label="Indexação da memória">
    <h3>Índice de contexto</h3>
    <p className="hint">O perfil Codex prepara um índice textual local, sem chamar IA ou gerar embeddings. Perfis de API usam o provedor configurado, quando habilitado. Resultados incertos exigem avaliação antes de uma nova solicitação.</p>
    {error && <p role="alert" className="error">{error}</p>}
    <ul>{indexes.map(job => <li key={job.id}>{indexLabels[job.state] ?? job.state} · {job.sourceVersion !== source.version ? "Histórico; a fonte mudou" : "Versão atual"} · {job.model} · versão {job.sourceVersion}
      {job.failureCode && ` · ${job.failureCode}`} · custo {job.estimatedCostUsd == null ? "desconhecido" : `US$ ${job.estimatedCostUsd}`}</li>)}</ul>
    <button disabled={busy} onClick={() => setRefresh(value => value + 1)}>Atualizar indexação</button>
    <form onSubmit={queue} aria-label="Solicitar indexação"><fieldset disabled={busy || source.status !== "validated" || profiles.length === 0}>
      <label>Perfil para indexação<select name="profile" required>{profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}</select></label>
      <label>Orçamento de indexação em US$<input name="budget" type="number" min="0.01" max="5" step="0.01" defaultValue="0.05" required /></label>
      <button>Indexar ou reconstruir contexto</button>
    </fieldset></form>
    {source.status !== "validated" && <p>Valide a fonte antes de indexar.</p>}
    {profiles.length === 0 && <p>Configure um perfil ativo para indexar o contexto.</p>}
  </section>;
}
