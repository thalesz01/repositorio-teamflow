"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { ExecutionHistory } from "./ExecutionHistory";
import { isLocked, TaskEditor } from "./TaskEditor";
import { Alert, Icon } from "./ui";
import { stageLabels, type Schema, type Stage, type TaskItem } from "./api";

export type DrawerTab = "details" | "executions";

/** Side panel with everything about one task: definition, stage change and executions. A native modal dialog gives focus trap and Escape for free. */
export function TaskDrawer({ error, task, tasks, members, busy, path, tab, onTab, onSave, onMove, onClose, onCancelExecution }: {
  error: string | null; task: TaskItem | null; tasks: TaskItem[]; members: Schema["MemberDto"][]; busy: boolean; path: string;
  tab: DrawerTab; onTab: (tab: DrawerTab) => void;
  onSave: (input: Schema["TaskInput"]) => Promise<void>; onMove: (task: TaskItem, stage: Stage) => Promise<void>;
  onClose: () => void; onCancelExecution: (executionId: string) => Promise<void>;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [executions, setExecutions] = useState<number | null>(null);
  useEffect(() => {
    const element = dialog.current;
    element?.showModal();
    // A new task starts with the cursor in the title; an existing one keeps the browser default (the close button).
    if (!task) element?.querySelector<HTMLInputElement>("input[name=title]")?.focus();
    return () => element?.close();
    // Opening happens once; switching from "new" to the saved task must not reopen or refocus the panel.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  // Close the dialog while it is still attached so the browser can restore focus to the element that opened it.
  const close = () => { dialog.current?.close(); onClose(); };
  const formId = "task-form";
  const locked = isLocked(task);
  const [focusRequest] = useState(1);
  async function submitMove(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!task) return;
    const stage = String(new FormData(event.currentTarget).get("stage")) as Stage;
    if (stage) await onMove(task, stage);
  }
  return <dialog ref={dialog} className="drawer" aria-labelledby="drawer-title" onCancel={event => { event.preventDefault(); close(); }}
    onClick={event => { if (event.target === dialog.current) close(); }}>
    <header className="drawer-head">
      <div className="drawer-top">
        <div style={{ flex: 1, minWidth: 0 }}>
          <p className="eyebrow">{task ? "Tarefa" : "Planeje o próximo passo"}</p>
          <h2 id="drawer-title">{task ? task.title : "Nova tarefa"}</h2>
        </div>
        <button className="icon ghost" onClick={close} aria-label="Fechar detalhe" title="Fechar (Esc)"><Icon name="close" /></button>
      </div>
      {task && <div className="drawer-meta">
        <span className="badge" data-stage={task.stage}>{stageLabels[task.stage]}</span>
        {task.allowedTransitions.length > 0 && <form className="move-inline" onSubmit={submitMove} aria-label="Mover tarefa">
          <label>Mover para<select name="stage" required disabled={busy} defaultValue="">
            <option value="" disabled>Escolha uma etapa</option>
            {task.allowedTransitions.map(stage => <option key={stage} value={stage}>{stageLabels[stage]}</option>)}
          </select></label>
          <button className="sm" disabled={busy}>Mover</button>
        </form>}
      </div>}
      {task && <div className="tabs" role="tablist" aria-label="Seções da tarefa">
        <button role="tab" className="tab" id="tab-details" aria-selected={tab === "details"} aria-controls="panel-details" onClick={() => onTab("details")}>Detalhes</button>
        <button role="tab" className="tab" id="tab-executions" aria-selected={tab === "executions"} aria-controls="panel-executions" onClick={() => onTab("executions")}>Execuções{executions ? <span className="badge plain">{executions}</span> : null}</button>
      </div>}
    </header>
    <div className="drawer-body">
      {error && <Alert>{error}</Alert>}
      <div role={task ? "tabpanel" : undefined} id="panel-details" aria-labelledby={task ? "tab-details" : undefined} hidden={tab !== "details"}>
        <div className="card"><TaskEditor key={task ? `${task.id}:${task.version}` : "new"} formId={formId} task={task} focusRequest={focusRequest} tasks={tasks} members={members} busy={busy} onSave={onSave} /></div>
      </div>
      {task && <div role="tabpanel" id="panel-executions" aria-labelledby="tab-executions" hidden={tab !== "executions"}>
        <ExecutionHistory key={`${task.id}:${task.version}`} path={`${path}/${task.id}/executions`} busy={busy} stage={task.stage} onCancel={onCancelExecution} onCount={setExecutions} />
      </div>}
    </div>
    {tab === "details" && !locked && <footer className="drawer-foot"><span className="hint">{task ? "As alterações só valem depois de salvas." : "A tarefa começa na etapa Entrada."}</span>
      <button className="primary" type="submit" form={formId} disabled={busy}>{task ? "Salvar alterações" : "Criar tarefa"}</button></footer>}
  </dialog>;
}
