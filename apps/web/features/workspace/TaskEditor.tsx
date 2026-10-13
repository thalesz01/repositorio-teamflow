"use client";

import { useEffect, useRef, type FormEvent } from "react";
import { Alert } from "./ui";
import type { Schema, TaskItem } from "./api";

/** Stages in which the task is owned by an execution or already closed: the definition cannot change. */
export const isLocked = (task: TaskItem | null) => task !== null && ["execution", "review", "awaitingMerge", "done", "cancelled"].includes(task.stage);

export function TaskEditor({ formId, task, tasks, members, busy, focusRequest, onSave }: {
  formId: string; task: TaskItem | null; tasks: TaskItem[]; members: Schema["MemberDto"][]; busy: boolean;
  focusRequest: number;
  onSave: (input: Schema["TaskInput"]) => Promise<void>;
}) {
  const titleInput = useRef<HTMLInputElement>(null);
  useEffect(() => { if (focusRequest && !task && !busy) titleInput.current?.focus(); }, [focusRequest, task, busy]);
  const readOnly = isLocked(task);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    await onSave({
      title: String(data.get("title")), description: String(data.get("description")),
      acceptanceCriteria: String(data.get("criteria")).split("\n").map(line => line.trim()).filter(Boolean),
      assigneeId: String(data.get("assignee")) || null, dependencyIds: data.getAll("dependencies").map(String),
    });
  }
  const assignedMemberPresent = !task?.assigneeId || members.some(member => member.userId === task.assigneeId);
  const candidates = tasks.filter(item => item.id !== task?.id);

  return <form id={formId} onSubmit={submit} aria-label={task ? "Detalhe da tarefa" : "Nova tarefa"}>
    {readOnly && <Alert tone="info">A definição desta tarefa não pode ser editada nesta etapa.</Alert>}
    <fieldset disabled={busy || readOnly}>
      <label>Título<input ref={titleInput} name="title" defaultValue={task?.title ?? ""} required maxLength={160} placeholder="Ex.: Corrigir o cálculo do frete" /></label>
      <label>Descrição<textarea name="description" defaultValue={task?.description ?? ""} required maxLength={10000} rows={4} placeholder="Qual é o problema ou a mudança desejada?" /></label>
      <label>Critérios de aceitação<textarea name="criteria" aria-describedby="criteria-hint" defaultValue={task?.acceptanceCriteria.join("\n") ?? ""} rows={4} placeholder={"Um critério por linha\nEx.: O total tem duas casas decimais"} /></label>
      <p id="criteria-hint" className="hint field-hint">Um critério por linha. Uma tarefa só fica pronta com pelo menos um critério.</p>
      <label>Responsável<select name="assignee" defaultValue={task?.assigneeId ?? ""}>
        <option value="">Não atribuído</option>
        {!assignedMemberPresent && <option value={task?.assigneeId ?? ""}>Membro removido — escolha outro responsável</option>}
        {members.map(member => <option key={member.userId} value={member.userId}>{member.email}</option>)}
      </select></label>
      <fieldset aria-label="Dependências"><legend className="field-legend">Depende de</legend>
        {candidates.length ? <div className="checklist">{candidates.map(item => <label key={item.id}><input type="checkbox" name="dependencies" value={item.id} defaultChecked={task?.dependencyIds.includes(item.id)} />{item.title}</label>)}</div>
          : <p className="hint">Não há outras tarefas neste projeto.</p>}
        <small>A tarefa só pode ser executada depois que suas dependências forem concluídas.</small>
      </fieldset>
    </fieldset>
  </form>;
}
