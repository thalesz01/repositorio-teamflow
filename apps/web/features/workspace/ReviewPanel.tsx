"use client";

import { useEffect, useRef, useState } from "react";
import { Alert, Icon } from "./ui";
import { api, errorMessage, type Schema, type TaskItem } from "./api";

const labels: Record<string, string> = {
  not_checked: "Revisão ainda não consultada", review_required: "Aguardando revisão independente", ci_pending: "Aguardando validação do commit",
  awaiting_merge: "Aprovada, aguardando merge", merged: "Merge confirmado", revalidation_required: "Código ou base mudou: revalidação necessária",
  merge_policy_unverified: "Merge detectado sem aprovação e testes confirmados",
};
const tones: Record<string, string> = { merged: "success", awaiting_merge: "success", revalidation_required: "warning", merge_policy_unverified: "warning", review_required: "info", ci_pending: "warning", not_checked: "" };

export function ReviewPanel({ path }: { path: string }) {
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
      setNotice(`Nova execução criada: ${execution.id}. Atualize o quadro para acompanhá-la. O histórico e a revisão anterior foram preservados.`);
    } catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  const merged = status?.state === "merged";
  return <section aria-label="Revisão do commit" className="step-list">
    {status && <div className="review-status">
      <span className={`badge ${tones[status.state] ?? ""}`}>{labels[status.state] ?? status.state}</span>
      <span className={`badge ${status.ciPassed ? "success" : "warning"}`}>{status.ciPassed ? "Testes do runner confirmados" : "Validação não confirmada"}</span>
    </div>}
    {error && <Alert>{error}</Alert>}
    {notice && <Alert tone="success" role="status">{notice}</Alert>}
    <div className="review-actions">
      <button disabled={busy} onClick={() => void review("refresh")}><Icon name="refresh" size="sm" />Consultar revisão e merge</button>
      <button className="primary" disabled={busy || merged} onClick={() => void review("approve")}><Icon name="check" size="sm" />Aprovar commit revisado</button>
    </div>
    <p className="hint">Outra pessoa da equipe aprova o commit testado. Faça o merge local com <code>git merge --ff-only</code> e consulte novamente para confirmar a conclusão.</p>
    {!merged && <><hr />
    <div className="stack">
      <h4>Pedir ajustes</h4>
      <label>Perfil dos ajustes<select value={profileId} onChange={event => setProfileId(event.target.value)} disabled={busy}>
        <option value="">Manter perfil da execução anterior</option>{profiles.map(p => <option key={p.id} value={p.id}>{p.name} · {p.model}</option>)}
      </select></label>
      <label>Ajustes solicitados<textarea value={feedback} onChange={event => setFeedback(event.target.value)} maxLength={2000} rows={3} disabled={busy} placeholder="Descreva o que precisa mudar. A nova execução recebe o histórico e as alterações já confirmadas." /></label>
      <p className="hint">A nova execução reexecuta os testes e passa por revisão de novo; a aprovação anterior não é herdada.</p>
      <div><button disabled={busy || !feedback.trim() || notice !== null} onClick={() => void requestChanges()}>Criar execução de ajustes</button></div>
    </div></>}
  </section>;
}
