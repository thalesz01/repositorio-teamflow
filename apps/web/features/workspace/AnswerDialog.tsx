"use client";

import { useEffect, useRef, useState } from "react";
import { Alert, Icon } from "./ui";
import { api, errorMessage, type Schema, type TaskItem } from "./api";

/** The answer of an analysis task: the closing message of its latest execution, shown as plain text. */
export function AnswerDialog({ taskPath, task, onClose }: { taskPath: string; task: TaskItem; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [answer, setAnswer] = useState<string | null | undefined>();
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const latest = await api<Schema["PageResultOfExecutionDto"]>(`${taskPath}/${task.id}/executions/?page=1&pageSize=1`, { signal: controller.signal });
        const execution = latest.items[0];
        if (!execution) { setAnswer(null); return; }
        const progress = await api<Schema["ExecutionProgress"]>(`${taskPath}/${task.id}/executions/${execution.id}/progress`, { signal: controller.signal });
        if (!controller.signal.aborted) setAnswer(progress.agentSummary ?? null);
      } catch (failure) { if (!controller.signal.aborted) setError(errorMessage(failure)); }
    }
    void load();
    return () => controller.abort();
  }, [taskPath, task.id]);
  const close = () => { dialog.current?.close(); onClose(); };
  async function copy() {
    if (!answer) return;
    try { await navigator.clipboard.writeText(answer); setCopied(true); setTimeout(() => setCopied(false), 2000); } catch { setCopied(false); }
  }
  return <dialog ref={dialog} className="modal modal-wide answer-modal" aria-labelledby="answer-title"
    onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); } }} onCancel={event => { event.preventDefault(); close(); }}>
    <div className="modal-inner">
      <div className="modal-head"><span className="icon-tile" aria-hidden="true"><Icon name="search" /></span>
        <div><p className="eyebrow">Resposta da análise</p><h2 id="answer-title">{task.title}</h2></div>
        <button type="button" className="icon ghost sm" aria-label="Fechar resposta" onClick={close}><Icon name="close" size="sm" /></button></div>
      <section aria-label="Pergunta" className="answer-question"><h3>Pergunta</h3><p>{task.description}</p>
        {task.folderPath && <p className="hint">Pasta: <code>{task.folderPath}</code></p>}</section>
      {error ? <Alert>{error}</Alert>
        : answer === undefined ? <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Carregando a resposta…</p>
          : answer === null ? <Alert tone="info">Esta análise ainda não tem uma resposta registrada.</Alert>
            : <section aria-label="Resposta" className="answer-text"><h3>Resposta</h3><div className="answer-body">{answer}</div></section>}
      <p className="hint">A resposta é gerada pela IA a partir dos arquivos lidos e pode conter erros; confira nos arquivos citados antes de tomar decisões.</p>
      <div className="modal-actions"><button type="button" onClick={() => void copy()} disabled={!answer}>{copied ? "Copiado" : "Copiar resposta"}</button><button className="primary" type="button" onClick={close}>Fechar</button></div>
    </div>
  </dialog>;
}
