"use client";

import { useEffect, useRef, useState } from "react";
import { ExecutionHistory } from "./ExecutionHistory";
import { SkillRunner } from "./SkillRunner";
import { isLocked, TaskEditor } from "./TaskEditor";
import { Alert, Icon } from "./ui";
import { stageLabels, type Schema, type TaskItem } from "./api";

export type DrawerTab = "details" | "executions";

export function TaskDrawer({ error, task, busy, path, runnable, canRun, tab, onTab, onSave, onRun, onCancelTask, onReview, onAnswer, onClose, onCancelExecution }: {
  error: string | null; task: TaskItem; busy: boolean; path: string; runnable: boolean; canRun: boolean;
  onRun: (skill: string | null) => Promise<void>; tab: DrawerTab; onTab: (tab: DrawerTab) => void;
  onSave: (input: Schema["TaskInput"]) => Promise<void>; onCancelTask: (task: TaskItem) => Promise<void>;
  onReview: () => void; onAnswer: () => void; onClose: () => void; onCancelExecution: (executionId: string) => Promise<void>;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const body = useRef<HTMLDivElement>(null);
  const [executions, setExecutions] = useState<number | null>(null);
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  useEffect(() => { body.current?.scrollTo({ top: 0 }); }, [tab]);
  // Closing while attached lets the browser return focus to the task card.
  const close = () => { dialog.current?.close(); onClose(); };
  const formId = "task-form";
  const locked = isLocked(task);
  const cancellable = task.allowedTransitions.includes("cancelled");
  return <dialog ref={dialog} className="drawer task-detail-dialog" aria-labelledby="drawer-title"
    onCancel={event => { event.preventDefault(); close(); }} onClick={event => { if (event.target === dialog.current) close(); }}>
    <header className="drawer-head">
      <div className="drawer-top">
        <div className="task-dialog-heading"><div className="task-dialog-kicker"><span>Tarefa</span>
          <span className="badge" data-stage={task.stage}>{stageLabels[task.stage]}</span></div>
          <h2 id="drawer-title">{task.title}</h2>
        </div>
        <button className="icon ghost" onClick={close} aria-label="Fechar detalhe" title="Fechar (Esc)"><Icon name="close" /></button>
      </div>
      <div className="tabs" role="tablist" aria-label="Seções da tarefa" onKeyDown={event => {
        if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
        event.preventDefault();
        const next = event.key === "Home" ? "details" : event.key === "End" ? "executions" : tab === "details" ? "executions" : "details";
        onTab(next); dialog.current?.querySelector<HTMLButtonElement>(`#tab-${next}`)?.focus();
      }}>
        <button role="tab" className="tab" id="tab-details" tabIndex={tab === "details" ? 0 : -1} aria-selected={tab === "details"} aria-controls="panel-details" onClick={() => onTab("details")}><Icon name="list" size="sm" />Detalhes</button>
        <button role="tab" className="tab" id="tab-executions" tabIndex={tab === "executions" ? 0 : -1} aria-selected={tab === "executions"} aria-controls="panel-executions" onClick={() => onTab("executions")}><Icon name="clock" size="sm" />Execuções{executions ? <span className="badge plain">{executions}</span> : null}</button>
      </div>
    </header>
    <div ref={body} className="drawer-body">
      {error && <Alert>{error}</Alert>}
      <div role="tabpanel" id="panel-details" aria-labelledby="tab-details" hidden={tab !== "details"}>
        {task.stage === "failed" && <div className="task-failure-hint"><Icon name="alert" size="sm" /><div><strong>A última execução não foi concluída</strong><p>Consulte o relatório para entender a falha ou inicie uma nova execução.</p></div>
          <button className="sm" onClick={() => onTab("executions")}>Ver execuções</button></div>}
        <div className="task-detail-layout">
          <section className="task-definition" aria-label="Descrição da tarefa"><h3>Descrição da tarefa</h3>
            <p className="hint">{locked ? "Objetivo e critérios usados nesta tarefa." : "Defina a mudança e como verificar o resultado."}</p>
            <TaskEditor key={`${task.id}:${task.version}`} formId={formId} task={task} busy={busy} onSave={onSave} />
          </section>
          <aside className="task-context" aria-label="Contexto e execução">
            <section className="task-location-summary"><h3><Icon name="folder" size="sm" />Pasta de trabalho</h3>
              {task.folderPath ? <><p className="task-folder-name">{task.folderPath.replace(/\\/g, "/").split("/").filter(Boolean).at(-1)}</p><p className="task-folder-path">{task.folderPath}</p>
                {task.baseBranch && <p className="task-branch"><Icon name="git" size="sm" />Branch <code>{task.baseBranch}</code></p>}</> : <p className="hint">Nenhuma pasta vinculada.</p>}
            </section>
            {runnable && <SkillRunner taskPath={`${path}/${task.id}`} busy={busy} canRun={canRun} onRun={onRun} retry={task.stage === "failed"} />}
            <p className="task-review-note"><Icon name="eye" size="sm" /><span>Você revisa as alterações antes de aplicá-las à pasta.</span></p>
          </aside>
        </div>
      </div>
      <div role="tabpanel" id="panel-executions" aria-labelledby="tab-executions" hidden={tab !== "executions"}>
        <ExecutionHistory key={`${task.id}:${task.version}`} path={`${path}/${task.id}/executions`} busy={busy} stage={task.stage} onCancel={onCancelExecution} onCount={setExecutions} onReview={onReview} onAnswer={task.kind === "analysis" ? onAnswer : undefined} />
      </div>
    </div>
    <footer className="drawer-foot">
      {tab === "details" && cancellable ? <button className="ghost danger sm" disabled={busy} onClick={() => void onCancelTask(task)}>Cancelar tarefa</button> : <span />}
      <div className="actions"><button onClick={close}>Fechar</button>{tab === "details" && !locked && <button className="primary" type="submit" form={formId} disabled={busy}>Salvar alterações</button>}</div>
    </footer>
  </dialog>;
}
