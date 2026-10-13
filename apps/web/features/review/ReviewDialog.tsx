"use client";

import { useEffect, useRef, useState } from "react";
import { api, errorMessage, type Schema } from "../workspace/api";
import { Alert, Icon } from "../workspace/ui";
import { commentLimit, DiffViewer, useDiff, type DiffComment, type DiffScope } from "./DiffViewer";
import "./review.css";

/** The server accepts up to 6000 characters of feedback per correction. */
export const feedbackLimit = 6000;

/** Turns the general note and the line comments into the text the agent receives with the next execution. */
export function formatFeedback(general: string, comments: DiffComment[]) {
  const parts: string[] = [];
  if (general.trim()) parts.push(`Observação geral: ${general.trim()}`);
  comments.forEach((comment, index) => parts.push(
    `${index + 1}. ${comment.path}, linha ${comment.line} (${comment.side === "before" ? "código antigo" : "código novo"}): ${comment.code.trim() ? `\`${comment.code.trim()}\`` : "(linha em branco)"}\n   Corrigir: ${comment.text}`));
  return parts.join("\n\n");
}

/** Review of one execution: read the changes, comment the lines to fix, then send the correction or conclude. */
export function ReviewDialog({ scope, onClose, onChanged }: { scope: DiffScope; onClose: () => void; onChanged: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const { data, error, reload } = useDiff(scope);
  const [comments, setComments] = useState<DiffComment[]>([]);
  const [general, setGeneral] = useState("");
  const [summary, setSummary] = useState<string | null>(null);
  const [busy, setBusy] = useState<"fix" | "complete" | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const idempotency = useRef<{ text: string; key: string } | null>(null);
  const taskPath = `/organizations/${scope.organizationId}/projects/${scope.projectId}/tasks/${scope.taskId}`;
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  // The agent closes each execution with a note on what it did and what it could not do; without it the person only sees the diff.
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const progress = await api<Schema["ExecutionProgress"]>(`${taskPath}/executions/${scope.executionId}/progress`, { signal: controller.signal });
        if (!controller.signal.aborted) setSummary(progress.agentSummary ?? null);
      } catch { /* the summary is optional; the diff and the decisions do not depend on it */ }
    }
    void load();
    return () => controller.abort();
  }, [taskPath, scope.executionId]);
  // Publication runs in the background right after the tests pass; keep asking until the changes are readable.
  const waiting = !!error && error.includes("ainda não foram publicadas");
  useEffect(() => {
    if (!waiting) return;
    const timer = setInterval(reload, 4000);
    return () => clearInterval(timer);
  }, [waiting, reload]);
  const close = () => { dialog.current?.close(); onClose(); };
  const feedback = formatFeedback(general, comments);
  const tooLong = feedback.length > feedbackLimit;

  async function sendFix() {
    if (!data) return;
    setBusy("fix"); setFailure(null);
    try {
      if (idempotency.current?.text !== feedback) idempotency.current = { text: feedback, key: crypto.randomUUID() };
      const input: Schema["ExecutionInput"] = { version: data.task.version, parentExecutionId: scope.executionId, feedback,
        maxDurationSeconds: 900, maxAttempts: 3, maxSteps: 50, maxCostUsd: 5, agentProfileId: null };
      await api<Schema["ExecutionDto"]>(`${taskPath}/executions/`, { method: "POST", headers: { "Idempotency-Key": idempotency.current.key }, body: JSON.stringify(input) });
      onChanged(); close();
    } catch (problem) { setFailure(errorMessage(problem)); setBusy(null); }
  }
  async function complete() {
    setBusy("complete"); setFailure(null);
    try {
      await api<Schema["ReviewStatus"]>(`${taskPath}/executions/${scope.executionId}/review/complete`, { method: "POST" });
      onChanged(); close();
    } catch (problem) { setFailure(errorMessage(problem)); setBusy(null); onChanged(); }
  }

  return <dialog ref={dialog} className="diff-modal" aria-label={`Revisão${data ? ` — ${data.task.title}` : ""}`}
    onKeyDown={event => { if (event.key === "Escape" && !busy) { event.preventDefault(); event.stopPropagation(); close(); } }}
    onCancel={event => { event.preventDefault(); if (!busy) close(); }}>
    <header className="diff-modal-head">
      <div><p className="eyebrow">Corrigir ou concluir</p><h2>{data ? data.task.title : "Carregando alterações…"}</h2></div>
      <button className="icon ghost" onClick={close} disabled={!!busy} aria-label="Fechar revisão" title="Fechar (Esc)"><Icon name="close" /></button>
    </header>
    <div className="diff-modal-body">
      {waiting ? <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Preparando as alterações para revisão… isso leva alguns segundos depois dos testes.</p>
        : error ? <section className="diff-empty" role="alert"><h2>Não foi possível abrir a revisão</h2><p>{error}</p><button onClick={reload}>Tentar novamente</button></section>
          : !data ? <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Buscando os arquivos e commits desta execução…</p>
            : <>
              {summary && <section className="agent-note" aria-label="Resumo do agente"><h3>O que o agente informou</h3><p>{summary}</p></section>}
              <p className="hint">Passe o mouse sobre uma linha e use <strong>+</strong> para dizer o que precisa mudar. Quando estiver tudo certo, conclua.</p>
              <DiffViewer data={data} commenting={{ comments, onAdd: comment => setComments(items => [...items, { ...comment, id: crypto.randomUUID() }]),
                onRemove: id => setComments(items => items.filter(item => item.id !== id)) }} />
            </>}
    </div>
    {data && <footer className="review-foot">
      {failure && <Alert>{failure}</Alert>}
      <label>Observação geral para a correção (opcional)<textarea rows={2} value={general} onChange={event => setGeneral(event.target.value)} maxLength={1000} disabled={!!busy}
        placeholder="Ex.: mantenha o estilo atual e rode os testes novamente." /></label>
      <div className="review-foot-row">
        <span className="count" role="status">{comments.length === 1 ? "1 comentário" : `${comments.length} comentários`}{tooLong ? ` · texto grande demais (${feedback.length}/${feedbackLimit})` : ""}</span>
        <span className="spacer" />
        <button disabled={!!busy || tooLong || (!comments.length && !general.trim())} onClick={() => void sendFix()}>
          {busy === "fix" ? <><span className="spinner" aria-hidden="true" />Enviando…</> : <><Icon name="refresh" size="sm" />Corrigir{comments.length ? ` (${comments.length})` : ""}</>}</button>
        <button className="primary" disabled={!!busy} onClick={() => void complete()}>
          {busy === "complete" ? <><span className="spinner" aria-hidden="true" />Concluindo…</> : <><Icon name="check" size="sm" />Concluir</>}</button>
      </div>
      {comments.length > 0 && <p className="hint">Concluir descarta os comentários ainda não enviados. Máximo de {commentLimit} caracteres por comentário.</p>}
    </footer>}
  </dialog>;
}
