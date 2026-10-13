"use client";

import { type FormEvent } from "react";
import { Alert } from "./ui";
import type { Schema, TaskItem } from "./api";

/** Stages in which the task is owned by an execution or already closed: the definition cannot change. */
export const isLocked = (task: TaskItem | null) => task !== null && ["execution", "review", "awaitingMerge", "done", "cancelled"].includes(task.stage);

export function TaskEditor({ formId, task, busy, onSave }: {
  formId: string; task: TaskItem; busy: boolean; onSave: (input: Schema["TaskInput"]) => Promise<void>;
}) {
  const readOnly = isLocked(task);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    // Assignee and dependencies are not edited in this flow, but saving must not erase what the API already holds.
    await onSave({
      title: String(data.get("title")), description: String(data.get("description")),
      acceptanceCriteria: String(data.get("criteria")).split("\n").map(line => line.trim()).filter(Boolean),
      assigneeId: task.assigneeId, dependencyIds: task.dependencyIds,
    });
  }
  return <form id={formId} onSubmit={submit} aria-label="Detalhe da tarefa">
    {readOnly && <Alert tone="info">A definição desta tarefa não pode ser editada nesta etapa.</Alert>}
    <fieldset disabled={busy || readOnly}>
      <label>Título<input name="title" defaultValue={task.title} required maxLength={160} /></label>
      <label>O que fazer<textarea name="description" defaultValue={task.description} required maxLength={10000} rows={5} /></label>
      <label>Resultado esperado<textarea name="criteria" defaultValue={task.acceptanceCriteria.join("\n")} rows={3} required /></label>
      <p className="hint field-hint">Um item por linha.</p>
    </fieldset>
    {task.folderPath && <p className="hint">Pasta: <code>{task.folderPath}</code>{task.baseBranch ? <> · branch <code>{task.baseBranch}</code></> : null}</p>}
  </form>;
}
