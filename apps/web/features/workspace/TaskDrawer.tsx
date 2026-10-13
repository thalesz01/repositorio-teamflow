"use client";

import { useEffect, useRef, useState } from "react";
import { ExecutionHistory } from "./ExecutionHistory";
import { isLocked, TaskEditor } from "./TaskEditor";
import { Alert, Icon } from "./ui";
import { stageLabels, type Schema, type TaskItem } from "./api";

export type DrawerTab = "details" | "executions";

/** Side panel with everything about one task: its definition and its executions. A native modal dialog gives focus trap and Escape for free. */
export function TaskDrawer({ error, task, busy, path, tab, onTab, onSave, onCancelTask, onReview, onClose, onCancelExecution }: {
  error: string | null; task: TaskItem; busy: boolean; path: string;
  tab: DrawerTab; onTab: (tab: DrawerTab) => void;
  onSave: (input: Schema["TaskInput"]) => Promise<void>; onCancelTask: (task: TaskItem) => Promise<void>;
  onReview: () => void; onClose: () => void; onCancelExecution: (executionId: string) => Promise<void>;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [executions, setExecutions] = useState<number | null>(null);
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  // Close the dialog while it is still attached so the browser can restore focus to the element that opened it.
  const close = () => { dialog.current?.close(); onClose(); };
  const formId = "task-form";
  const locked = isLocked(task);
  const cancellable = task.allowedTransitions.includes("cancelled");
  return <dialog ref={dialog} className="drawer" aria-labelledby="drawer-title" onCancel={event => { event.preventDefault(); close(); }}
    onClick={event => { if (event.target === dialog.current) close(); }}>
    <header className="drawer-head">
      <div className="drawer-top">
        <div style={{ flex: 1, minWidth: 0 }}>
          <p className="eyebrow">Tarefa</p>
          <h2 id="drawer-title">{task.title}</h2>
        </div>
        <button className="icon ghost" onClick={close} aria-label="Fechar detalhe" title="Fechar (Esc)"><Icon name="close" /></button>
      </div>
      <div className="drawer-meta"><span className="badge" data-stage={task.stage}>{stageLabels[task.stage]}</span></div>
      <div className="tabs" role="tablist" aria-label="Seções da tarefa">
        <button role="tab" className="tab" id="tab-details" aria-selected={tab === "details"} aria-controls="panel-details" onClick={() => onTab("details")}>Detalhes</button>
        <button role="tab" className="tab" id="tab-executions" aria-selected={tab === "executions"} aria-controls="panel-executions" onClick={() => onTab("executions")}>Execuções{executions ? <span className="badge plain">{executions}</span> : null}</button>
      </div>
    </header>
    <div className="drawer-body">
      {error && <Alert>{error}</Alert>}
      <div role="tabpanel" id="panel-details" aria-labelledby="tab-details" hidden={tab !== "details"}>
        <div className="card"><TaskEditor key={`${task.id}:${task.version}`} formId={formId} task={task} busy={busy} onSave={onSave} /></div>
      </div>
      <div role="tabpanel" id="panel-executions" aria-labelledby="tab-executions" hidden={tab !== "executions"}>
        <ExecutionHistory key={`${task.id}:${task.version}`} path={`${path}/${task.id}/executions`} busy={busy} stage={task.stage} onCancel={onCancelExecution} onCount={setExecutions} onReview={onReview} />
      </div>
    </div>
    {tab === "details" && (!locked || cancellable) && <footer className="drawer-foot">
      {cancellable ? <button className="danger sm" disabled={busy} onClick={() => void onCancelTask(task)}>Cancelar tarefa</button> : <span />}
      {!locked && <button className="primary" type="submit" form={formId} disabled={busy}>Salvar alterações</button>}</footer>}
  </dialog>;
}
