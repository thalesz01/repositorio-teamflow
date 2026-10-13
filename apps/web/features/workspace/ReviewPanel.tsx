"use client";

import { useEffect, useRef, useState } from "react";
import { api, errorMessage, type Schema, type TaskItem } from "./api";

const labels: Record<string, string> = {
  not_checked: "Revisão ainda não consultada", review_required: "Aguardando revisão independente", ci_pending: "Aguardando CI",
  awaiting_merge: "Aprovada, aguardando merge", merged: "Merge confirmado", revalidation_required: "Código ou base mudou: revalidação necessária",
  merge_policy_unverified: "Merge detectado sem aprovação e CI confirmados",
};

export function ReviewPanel({ path, local = false }: { path: string; local?: boolean }) {
  const [status, setStatus] = useState<Schema["ReviewStatus"] | null>(null);
  const [feedback, setFeedback] = useState("");
  const [profiles, setProfiles] = useState<Schema["ProfileDto"][]>([]);
  const [profileId, setProfileId] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const pending = useRef<{ text: string; key: string; input: Schema["ExecutionInput"] } | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    api<Schema["PageResultOfProfileDto"]>(`${path.slice(0, path.indexOf("/tasks/"))}/profiles/?page=1&pageSize=100`, { signal: controller.signal })
      .then(result => setProfiles(result.items.filter(p => !p.revokedAt && p.capabilities.includes("textGeneration"))))
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    api<Schema["ReviewStatus"]>(`${path}/review`, { signal: controller.signal }).then(setStatus)
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => controller.abort();
  }, [path]);
  async function review(command: "approve" | "refresh") {
    setBusy(true); setError(null); setNotice(null);
    try { setStatus(await api<Schema["ReviewStatus"]>(`${path}/review/${command}`, { method: "POST" })); }
    catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  async function requestChanges() {
    setBusy(true); setError(null); setNotice(null);
    const taskPath = path.slice(0, path.indexOf("/executions/"));
    try {
      if (pending.current?.text !== `${profileId}:${feedback}`) {
        const task = await api<TaskItem>(taskPath);
        pending.current = { text: `${profileId}:${feedback}`, key: crypto.randomUUID(), input: {
          version: task.version, agentProfileId: profileId || null, parentExecutionId: path.slice(path.lastIndexOf("/") + 1), feedback,
          maxDurationSeconds: 900, maxAttempts: 3, maxSteps: 50, maxCostUsd: 5,
        } };
      }
      const request = pending.current;
      if (request === null) throw new Error("Não foi possível preparar a execução de ajustes.");
      const execution = await api<Schema["ExecutionDto"]>(`${taskPath}/executions/`, {
        method: "POST", headers: { "Idempotency-Key": request.key }, body: JSON.stringify(request.input),
      });
      setNotice(`Nova execução criada: ${execution.id}. Atualize o quadro para acompanhá-la. O histórico e o PR anterior foram preservados.`);
    } catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  return <section aria-label="Revisão do PR">
    <h3>Revisão e testes</h3>
    {status && <p>{labels[status.state] ?? status.state} · {status.ciPassed ? (local ? "Testes do runner confirmados" : "CI confirmado") : "Validação não confirmada"}</p>}
    {error && <p role="alert" className="error">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    <button disabled={busy} onClick={() => void review("refresh")}>Consultar revisão e merge</button>
    <button disabled={busy || status?.state === "merged"} onClick={() => void review("approve")}>Aprovar commit revisado</button>
    <p className="hint">{local ? "Outra pessoa da equipe aprova o commit testado. Faça o merge local com git merge --ff-only e consulte novamente para confirmar a conclusão." : "A aprovação exige outra pessoa da equipe e CI configurada. A tarefa termina após merge confirmado no GitHub."}</p>
    <label>Perfil dos ajustes<select value={profileId} onChange={event => setProfileId(event.target.value)} disabled={busy}>
      <option value="">Manter perfil da execução anterior</option>{profiles.map(p => <option key={p.id} value={p.id}>{p.name} · {p.model}</option>)}
    </select></label>
    <p className="hint">A nova execução recebe alterações e histórico confirmados na mesma base. Reexecuta testes e passa novamente por revisão; a aprovação anterior não é herdada.</p>
    <label>Ajustes solicitados<textarea value={feedback} onChange={event => setFeedback(event.target.value)} maxLength={2000} rows={3} disabled={busy} /></label>
    <button disabled={busy || !feedback.trim() || status?.state === "merged" || notice !== null} onClick={() => void requestChanges()}>Criar execução de ajustes</button>
  </section>;
}
