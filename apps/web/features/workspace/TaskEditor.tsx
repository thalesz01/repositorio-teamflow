"use client";

import { useEffect, useRef, type FormEvent } from "react";
import type { Schema, TaskItem } from "./api";

export function TaskEditor({ task, tasks, members, busy, focusRequest, onSave }: {
  task: TaskItem | null; tasks: TaskItem[]; members: Schema["MemberDto"][]; busy: boolean;
  focusRequest: number;
  onSave: (input: Schema["TaskInput"]) => Promise<void>;
}) {
  const titleInput = useRef<HTMLInputElement>(null);
  useEffect(() => { if (focusRequest && !task && !busy) titleInput.current?.focus(); }, [focusRequest, task, busy]);
  const readOnly = task !== null && ["execution", "review", "awaitingMerge", "done", "cancelled"].includes(task.stage);
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

  return <section className="detail-panel" aria-label={task ? "Detalhe da tarefa" : "Nova tarefa"}>
    <h2>{task ? "Detalhe da tarefa" : "Nova tarefa"}</h2>
    <form onSubmit={submit}>
      <fieldset disabled={busy || readOnly}>
        <label>Título<input ref={titleInput} name="title" defaultValue={task?.title ?? ""} required maxLength={160} /></label>
        <label>Descrição<textarea name="description" defaultValue={task?.description ?? ""} required maxLength={10000} rows={4} /></label>
        <label>Critérios de aceitação<textarea name="criteria" defaultValue={task?.acceptanceCriteria.join("\n") ?? ""} rows={3} /></label>
        <p className="hint">Um critério por linha.</p>
        <label>Responsável<select name="assignee" defaultValue={task?.assigneeId ?? ""}>
          <option value="">Não atribuído</option>
          {!assignedMemberPresent && <option value={task?.assigneeId ?? ""}>Membro removido — escolha outro responsável</option>}
          {members.map(member => <option key={member.userId} value={member.userId}>{member.email}</option>)}
        </select></label>
        <label>Dependências<select name="dependencies" multiple defaultValue={task?.dependencyIds ?? []} size={4}>
          {tasks.filter(item => item.id !== task?.id).map(item => <option key={item.id} value={item.id}>{item.title}</option>)}
        </select></label>
        <p className="hint">No teclado, use Ctrl ou Command para selecionar mais de uma dependência.</p>
        {!readOnly && <button className="primary" disabled={busy}>{task ? "Salvar alterações" : "Criar tarefa"}</button>}
      </fieldset>
    </form>
    {readOnly && <p className="hint">A edição está indisponível nesta etapa.</p>}
  </section>;
}
