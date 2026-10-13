"use client";
import { ExecutionProgress } from "./ExecutionProgress";
import { PublicationPanel } from "./PublicationPanel";
import { PlanningPanel } from "./PlanningPanel";
import { MemoryPanel } from "./MemoryPanel";
import { IntegrationPanel } from "./IntegrationPanel";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { TaskEditor } from "./TaskEditor";
import { api, errorMessage, listAll, stageLabels, type Schema, type Stage, type TaskItem } from "./api";

export function TaskBoard({ project, members, canManageIntegrations }: { project: Schema["ProjectDto"]; members: Schema["MemberDto"][]; canManageIntegrations: boolean }) {
  const path = `/organizations/${project.organizationId}/projects/${project.id}/tasks`;
  const [profileId, setProfileId] = useState("");
  const [tasks, setTasks] = useState<TaskItem[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const [editorFocus, setEditorFocus] = useState(0);
  const [editorOpen, setEditorOpen] = useState(false);
  const [view, setView] = useState<"board" | "planning" | "memory" | "settings">("board");
  const editorTrigger = useRef<HTMLButtonElement | null>(null);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const keys = useRef(new Map<string, string>());
  useEffect(() => {
    if (editorOpen && view === "board") document.getElementById("task-inspector")?.scrollIntoView({ behavior: "instant", block: "start" });
  }, [editorOpen, selectedId, view]);
  useEffect(() => {
    const controller = new AbortController();
    listAll<TaskItem>(path + "/", controller.signal).then(setTasks)
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [path]);
  const selected = tasks.find(task => task.id === selectedId) ?? null;
  const filtered = tasks.filter(task => `${task.title} ${task.description}`.toLocaleLowerCase("pt-BR").includes(query.toLocaleLowerCase("pt-BR")));
  const currentPage = Math.min(page, Math.max(0, Math.ceil(filtered.length / 20) - 1));
  const visible = filtered.slice(currentPage * 20, (currentPage + 1) * 20);

  async function perform(action: () => Promise<void>) {
    setBusy(true); setError(null);
    try { await action(); }
    catch (failure) { setError(errorMessage(failure)); }
    finally {
      try { setTasks(await listAll<TaskItem>(path + "/")); }
      catch (failure) { setError(previous => previous ?? errorMessage(failure)); }
      setBusy(false);
    }
  }

  async function save(input: Schema["TaskInput"]) {
    await perform(async () => {
      const task = await api<TaskItem>(selected ? `${path}/${selected.id}` : path + "/", {
        method: selected ? "PUT" : "POST",
        body: JSON.stringify(selected ? { ...input, version: selected.version } satisfies Schema["TaskUpdate"] : input),
      });
      setSelectedId(task.id);
    });
  }

  async function move(task: TaskItem, stage: Stage) {
    await perform(async () => {
      if (stage === "execution") {
        const versionKey = `${task.id}:${task.version}:${profileId}`;
        const key = keys.current.get(versionKey) ?? crypto.randomUUID();
        keys.current.set(versionKey, key);
        const input: Schema["ExecutionInput"] = { version: task.version, maxDurationSeconds: 900, maxAttempts: 3, maxSteps: 50, maxCostUsd: 5, agentProfileId: profileId || null };
        await api<Schema["ExecutionDto"]>(`${path}/${task.id}/executions/`, {
          method: "POST", headers: { "Idempotency-Key": key }, body: JSON.stringify(input),
        });
      } else {
        await api<TaskItem>(`${path}/${task.id}/transition`, {
          method: "POST", body: JSON.stringify({ stage, version: task.version } satisfies Schema["TransitionInput"]),
        });
      }
    });
  }

  function closeEditor() {
    setEditorOpen(false);
    editorTrigger.current?.focus();
  }

  const projectPath = `/organizations/${project.organizationId}/projects/${project.id}`;
  const navigation = [
    { id: "board", label: "Quadro", icon: "M3 3h7v18H3zM14 3h7v10h-7zM14 17h7v4h-7z" },
    { id: "planning", label: "Demandas", icon: "M12 3v18M3 12h18M5 5l14 14M19 5L5 19" },
    { id: "memory", label: "Memória", icon: "M3 4h7a2 2 0 0 1 2 2v15M21 4h-7a2 2 0 0 0-2 2M3 4v15h7a2 2 0 0 1 2 2 2 2 0 0 1 2-2h7V4" },
    { id: "settings", label: "Configurações", icon: "M4 7h16M4 17h16M8 4v6M16 14v6" },
  ] as const;

  return <div className="project-shell"><nav className="project-nav" aria-label="Navegação do projeto">
    <p className="eyebrow">Workspace</p><p className="nav-project">{project.name}</p>
    {navigation.map(item => <button key={item.id} aria-current={view === item.id ? "page" : undefined} onClick={() => setView(item.id)}>
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true"><path d={item.icon} /></svg>{item.label}
    </button>)}
    <div className="nav-note"><strong>Um espaço para cada etapa</strong><p>Organize no quadro. Prepare em demandas. Reutilize na memória.</p></div>
  </nav><div className="project-content"><section aria-label="Quadro de tarefas" hidden={view !== "board"}>
    <div className="toolbar board-heading"><div><p className="eyebrow">{project.name} / Workspace</p><h1>Quadro de tarefas</h1><p className="hint">Da primeira ideia ao código aprovado. Acompanhe cada etapa.</p></div>
      <button onClick={() => perform(async () => {})} disabled={busy}>Atualizar quadro</button><button className="primary" onClick={event => { editorTrigger.current = event.currentTarget; setSelectedId(null); setEditorOpen(true); setEditorFocus(value => value + 1); }} disabled={busy}>Nova tarefa</button>
    </div>
    <div className="overview" aria-label="Resumo do projeto">{[
      { label: "Total de tarefas", count: tasks.length },
      { label: "Em execução", count: tasks.filter(task => task.stage === "execution").length },
      { label: "Revisão e merge", count: tasks.filter(task => ["review", "awaitingMerge"].includes(task.stage)).length },
      { label: "Concluídas", count: tasks.filter(task => task.stage === "done").length },
    ].map(metric => <div className="metric" key={metric.label}><span>{metric.label}</span><strong>{busy ? "—" : metric.count}</strong></div>)}</div>
    {error && <p role="alert" className="error">{error}</p>}
    <div className="board-tools"><label>Buscar tarefas<input type="search" placeholder="Buscar por título ou descrição…" value={query} onChange={event => { setQuery(event.target.value); setPage(0); }} /></label><button className="profile-shortcut" onClick={() => setView("settings")}>Codex da máquina</button><p role="status" aria-live="polite">{busy ? "Sincronizando quadro…" : `${filtered.length} de ${tasks.length} tarefa(s)`}</p></div>
    <div className="board" aria-label="Etapas do fluxo — role horizontalmente para ver todas" tabIndex={0} aria-busy={busy}>
      {(Object.keys(stageLabels) as Stage[]).map(stage => <section className="lane" data-stage={stage} key={stage} aria-label={stageLabels[stage]}
        onDragOver={event => event.preventDefault()} onDrop={event => {
          event.preventDefault();
          if (busy) return;
          const task = tasks.find(item => item.id === event.dataTransfer.getData("text/plain"));
          if (task) void move(task, stage);
        }}>
        <h2>{stageLabels[stage]} <span>{visible.filter(task => task.stage === stage).length}</span></h2>
        {!visible.some(task => task.stage === stage) && <p className="lane-empty">{busy ? "Carregando…" : "Nenhuma tarefa nesta etapa"}</p>}
        {visible.filter(task => task.stage === stage).map(task => <article className="task-card" key={task.id}
          draggable={!busy && task.allowedTransitions.length > 0} onDragStart={event => event.dataTransfer.setData("text/plain", task.id)}>
          <button className="task-title" onClick={event => { editorTrigger.current = event.currentTarget; setSelectedId(task.id); setEditorOpen(true); }} disabled={busy}>{task.title}</button>
          <p>{task.description.slice(0, 100)}</p>
          <p className="hint task-meta">{task.dependencyIds.length ? `${task.dependencyIds.length} dependência(s)` : "Sem dependências"}</p>
          {["review", "awaitingMerge"].includes(task.stage) && <button className="primary" onClick={event => { editorTrigger.current = event.currentTarget; setSelectedId(task.id); setEditorOpen(true); }} disabled={busy}>Revisar alterações</button>}
          {task.allowedTransitions.length > 0 && <MoveForm task={task} busy={busy} onMove={move} />}
        </article>)}
      </section>)}
    </div>
    <div className="toolbar pagination"><button disabled={busy || currentPage === 0} onClick={() => setPage(currentPage - 1)}>Página anterior</button>
      <span>Página {currentPage + 1} de {Math.max(1, Math.ceil(filtered.length / 20))}</span>
      <button disabled={busy || (currentPage + 1) * 20 >= filtered.length} onClick={() => setPage(currentPage + 1)}>Próxima página</button>
    </div>
    {editorOpen && <section className="task-inspector" id="task-inspector" aria-label="Painel da tarefa" onKeyDown={event => { if (event.key === "Escape") closeEditor(); }}>
    <div className="inspector-heading"><div><p className="eyebrow">{selected ? "Tarefa selecionada" : "Planeje o próximo passo"}</p><h2>{selected ? selected.title : "Criar uma tarefa"}</h2></div><button onClick={closeEditor}>Fechar detalhe</button></div>
    <div className="task-workspace" id="task-editor">
    <TaskEditor key={selected ? `${selected.id}:${selected.version}` : "new"} task={selected} focusRequest={editorFocus} tasks={tasks} members={members} busy={busy} onSave={save} />
    {selected && <ExecutionHistory key={`${selected.id}:${selected.version}`} path={`${path}/${selected.id}/executions`} busy={busy}
      onCancel={executionId => perform(async () => { await api<Schema["ExecutionDto"]>(`${path}/${selected.id}/executions/${executionId}/cancel`, { method: "POST" }); })} />}
    </div></section>}
  </section>
  <section className="workspace-view" aria-label="Demandas" hidden={view !== "planning"}><p className="eyebrow">Planejamento</p><h1>Analisar demandas</h1><p className="view-description">Transforme um problema em uma proposta de tarefa usando o contexto do projeto.</p><p className="hint">A análise usa o Codex autenticado nesta máquina.</p><PlanningPanel path={projectPath} profileId={profileId} onCreated={task => { setTasks(items => [...items, task]); setSelectedId(task.id); setEditorOpen(true); setView("board"); }} /></section>
  <section className="workspace-view" aria-label="Biblioteca de memória" hidden={view !== "memory"}><p className="eyebrow">Conhecimento da equipe</p><h1>Memória do projeto</h1><p className="view-description">Guarde decisões, valide fontes e reutilize o que a equipe já aprendeu.</p><MemoryPanel path={projectPath} /></section>
  <section className="workspace-view" aria-label="Configurações" hidden={view !== "settings"}><p className="eyebrow">Ambiente de trabalho</p><h1>Configurações do projeto</h1><p className="view-description">Escolha a pasta do código, confira o Git e acompanhe o Codex instalado no computador.</p><IntegrationPanel path={projectPath} canManage={canManageIntegrations} selectedProfile={profileId} onSelect={setProfileId} /></section>
  </div></div>;
}

function MoveForm({ task, busy, onMove }: { task: TaskItem; busy: boolean; onMove: (task: TaskItem, stage: Stage) => Promise<void> }) {
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const stage = String(new FormData(event.currentTarget).get("stage")) as Stage;
    await onMove(task, stage);
  }
  return <form onSubmit={submit} className="move-form"><label>Mover para<select aria-label={`Mover ${task.title}`} name="stage" required disabled={busy} defaultValue="">
    <option value="" disabled>Escolha uma etapa</option>
    {task.allowedTransitions.map(stage => <option key={stage} value={stage}>{stageLabels[stage]}</option>)}
  </select></label><button disabled={busy}>Mover</button></form>;
}

function ExecutionHistory({ path, busy, onCancel }: { path: string; busy: boolean; onCancel: (id: string) => Promise<void> }) {
  const [expanded, setExpanded] = useState<string | null>(null);
  const [executions, setExecutions] = useState<Schema["ExecutionDto"][]>([]);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    let refreshing = false;
    async function refresh() {
      if (refreshing) return;
      refreshing = true;
      try {
        const result = await listAll<Schema["ExecutionDto"]>(path + "/", controller.signal);
        if (!controller.signal.aborted) { setExecutions(result); setError(null); }
      } catch (failure) { if (!controller.signal.aborted) setError(errorMessage(failure)); }
      finally { refreshing = false; }
    }
    void refresh();
    const timer = setInterval(() => { void refresh(); }, 5000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [path]);
  const latest = executions[0];
  const review = latest?.state === "succeeded" ? latest : null;
  const states: Record<Schema["ExecutionState"], string> = { queued: "Na fila", leased: "Reivindicada", succeeded: "Finalizada", failed: "Falhou", cancelled: "Cancelada" };
  return <section className="detail-panel"><h2>Execuções</h2>{error && <p role="alert" className="error">{error}</p>}
    {review && <section className="review-ready" aria-label="Revisão da execução mais recente">
      <h3>Pronta para revisão</h3>
      <p className="hint">As alterações estão na branch da execução. A pasta original recebe o código após aprovação e merge.</p>
      <PublicationPanel key={review.id} path={`${path}/${review.id}`} />
    </section>}
    {!executions.length && <p>Nenhuma execução registrada.</p>}
    {executions.map(item => <div key={item.id}><p className="hint">{item.id === latest?.id ? "Execução mais recente" : "Execução anterior"} · {new Date(item.createdAt).toLocaleString("pt-BR")}</p><div className="toolbar"><p>{states[item.state]} · tentativa {item.attempt} de {item.maxAttempts}{item.cancellationRequested && " · cancelamento solicitado"}</p>
      {["queued", "leased"].includes(item.state) && <button disabled={busy || item.cancellationRequested} onClick={() => onCancel(item.id)}>Cancelar execução</button>}
      <button onClick={() => setExpanded(current => current === item.id ? null : item.id)} aria-expanded={expanded === item.id}>Ver progresso</button>
    </div>{expanded === item.id && <ExecutionProgress key={item.id} path={`${path}/${item.id}`} showPublication={item.id !== review?.id} />}</div>)}
  </section>;
}
