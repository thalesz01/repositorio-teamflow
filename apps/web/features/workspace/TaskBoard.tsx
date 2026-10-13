"use client";

import { useEffect, useState } from "react";
import { PlanningPanel } from "./PlanningPanel";
import { MemoryPanel } from "./MemoryPanel";
import { IntegrationPanel } from "./IntegrationPanel";
import { TaskDrawer, type DrawerTab } from "./TaskDrawer";
import { Alert, Avatar, EmptyState, Icon, PageHeader } from "./ui";
import { api, errorMessage, listAll, stageLabels, type Schema, type Stage, type TaskItem } from "./api";

export type View = "board" | "planning" | "memory" | "settings";
type Drawer = { kind: "new" } | { kind: "task"; id: string; tab: DrawerTab } | null;

// Main flow first; exception stages only appear when they hold work (or while dragging, so they stay reachable).
const flowStages: Stage[] = ["intake", "analysis", "ready", "execution", "review", "awaitingMerge", "done"];
const exceptionStages: Stage[] = ["blocked", "failed", "cancelled"];

export function TaskBoard({ project, members, canManageIntegrations, view, onViewChange }: {
  project: Schema["ProjectDto"]; members: Schema["MemberDto"][]; canManageIntegrations: boolean; view: View; onViewChange: (view: View) => void;
}) {
  const path = `/organizations/${project.organizationId}/projects/${project.id}/tasks`;
  const projectPath = `/organizations/${project.organizationId}/projects/${project.id}`;
  const [profileId, setProfileId] = useState("");
  const [tasks, setTasks] = useState<TaskItem[]>([]);
  const [drawer, setDrawer] = useState<Drawer>(null);
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dragging, setDragging] = useState<string | null>(null);
  const [dropStage, setDropStage] = useState<Stage | null>(null);
  const [keys] = useState(() => new Map<string, string>());
  useEffect(() => {
    const controller = new AbortController();
    listAll<TaskItem>(path + "/", controller.signal).then(setTasks)
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); })
      .finally(() => { if (!controller.signal.aborted) { setBusy(false); setLoaded(true); } });
    return () => controller.abort();
  }, [path]);
  const selected = drawer?.kind === "task" ? tasks.find(task => task.id === drawer.id) ?? null : null;
  const needle = query.trim().toLocaleLowerCase("pt-BR");
  const filtered = needle ? tasks.filter(task => `${task.title} ${task.description}`.toLocaleLowerCase("pt-BR").includes(needle)) : tasks;
  const count = (stages: Stage[]) => tasks.filter(task => stages.includes(task.stage)).length;
  const memberName = (id: string | null | undefined) => members.find(member => member.userId === id)?.email;

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
      setDrawer({ kind: "task", id: task.id, tab: "details" });
    });
  }

  async function move(task: TaskItem, stage: Stage) {
    await perform(async () => {
      if (stage === "execution") {
        const versionKey = `${task.id}:${task.version}:${profileId}`;
        const key = keys.get(versionKey) ?? crypto.randomUUID();
        keys.set(versionKey, key);
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

  const stages = [...flowStages, ...exceptionStages.filter(stage => dragging || tasks.some(task => task.stage === stage))];
  const inReview = count(["review", "awaitingMerge"]);

  return <>
    <section aria-label="Quadro de tarefas" hidden={view !== "board"} className="view">
      <PageHeader eyebrow={project.name} title="Quadro" description="Da primeira ideia ao código aprovado. Arraste um cartão ou abra-o para ver os detalhes."
        actions={<>
          <label className="search"><span className="sr-only">Buscar tarefas</span><Icon name="search" size="sm" /><input type="search" placeholder="Buscar tarefas…" value={query} onChange={event => setQuery(event.target.value)} /></label>
          <button className="icon" aria-label="Atualizar quadro" title="Atualizar quadro" onClick={() => perform(async () => {})} disabled={busy}><Icon name="refresh" /></button>
          <button className="primary" onClick={() => setDrawer({ kind: "new" })} disabled={busy}><Icon name="plus" size="sm" />Nova tarefa</button>
        </>} />
      <div className="board-summary" aria-label="Resumo do projeto">
        <span><strong>{loaded ? tasks.length : "—"}</strong> tarefa(s)</span>
        <span><strong>{loaded ? count(["execution"]) : "—"}</strong> em execução</span>
        <span><strong>{loaded ? inReview : "—"}</strong> em revisão e merge</span>
        <span><strong>{loaded ? count(["done"]) : "—"}</strong> concluída(s)</span>
        <span className="sync" role="status" aria-live="polite">{busy ? <><span className="spinner" aria-hidden="true" />Sincronizando…</> : needle ? `${filtered.length} de ${tasks.length} no filtro` : ""}</span>
      </div>
      {error && <Alert>{error}</Alert>}
      {loaded && !tasks.length ? <div className="card"><EmptyState icon="board" title="Seu quadro está vazio" action={<button className="primary" onClick={() => setDrawer({ kind: "new" })}><Icon name="plus" size="sm" />Criar a primeira tarefa</button>}>
        Crie uma tarefa para começar, ou use <strong>Demandas</strong> para transformar um problema em uma tarefa com critérios de aceitação.</EmptyState></div>
        : <div className="board" aria-label="Etapas do fluxo — role horizontalmente para ver todas" tabIndex={0} aria-busy={busy}>
          {stages.map(stage => {
            const items = filtered.filter(task => task.stage === stage);
            const collapsed = loaded && !items.length && !dragging;
            return <section className={`lane${dropStage === stage ? " drop-target" : ""}${collapsed ? " collapsed" : ""}`} data-stage={stage} key={stage} aria-label={stageLabels[stage]}
              onDragOver={event => { event.preventDefault(); setDropStage(stage); }} onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node)) setDropStage(null); }}
              onDrop={event => {
                event.preventDefault(); setDropStage(null); setDragging(null);
                if (busy) return;
                const task = tasks.find(item => item.id === event.dataTransfer.getData("text/plain"));
                if (task && task.stage !== stage) void move(task, stage);
              }}>
              <div className="lane-head"><h2><span className="lane-dot" aria-hidden="true" />{stageLabels[stage]}</h2><span className="lane-count" aria-label={`${items.length} tarefa(s)`}>{items.length}</span></div>
              <div className={`lane-body${!loaded ? " loading-lanes" : ""}`}>
                {!loaded && <><div className="skeleton" /><div className="skeleton" /></>}
                {loaded && !items.length && <p className="lane-empty">{needle ? "Nada neste filtro" : dragging ? "Solte aqui" : "Nenhuma tarefa"}</p>}
                {items.map(task => <TaskCard key={task.id} task={task} busy={busy} selected={selected?.id === task.id} assignee={memberName(task.assigneeId)}
                  draggable={!busy && task.allowedTransitions.length > 0} isDragging={dragging === task.id}
                  onDragStart={() => setDragging(task.id)} onDragEnd={() => { setDragging(null); setDropStage(null); }}
                  onOpen={() => setDrawer({ kind: "task", id: task.id, tab: "details" })}
                  onReview={() => setDrawer({ kind: "task", id: task.id, tab: "executions" })}
                  onRun={() => void move(task, "execution")} />)}
              </div>
            </section>;
          })}
        </div>}
    </section>
    <section className="view" aria-label="Demandas" hidden={view !== "planning"}>
      <PageHeader eyebrow="Planejamento" title="Analisar demandas" description="Transforme um problema em uma proposta de tarefa usando o contexto validado do projeto." />
      <PlanningPanel path={projectPath} profileId={profileId} onCreated={task => { setTasks(items => [...items, task]); setDrawer({ kind: "task", id: task.id, tab: "details" }); onViewChange("board"); }} />
    </section>
    <section className="view" aria-label="Biblioteca de memória" hidden={view !== "memory"}>
      <PageHeader eyebrow="Conhecimento da equipe" title="Memória do projeto" description="Guarde decisões, valide fontes e reutilize o que a equipe já aprendeu." />
      <MemoryPanel path={projectPath} />
    </section>
    <section className="view" aria-label="Configurações" hidden={view !== "settings"}>
      <PageHeader eyebrow="Ambiente de trabalho" title="Configurações do projeto" description="Escolha a pasta do código, confira o Git e acompanhe o Codex instalado no computador." />
      <IntegrationPanel path={projectPath} canManage={canManageIntegrations} selectedProfile={profileId} onSelect={setProfileId} />
    </section>
    {drawer && <TaskDrawer error={error} task={selected} tasks={tasks} members={members} busy={busy} path={path}
      tab={drawer.kind === "task" ? drawer.tab : "details"} onTab={tab => drawer.kind === "task" && setDrawer({ ...drawer, tab })}
      onSave={save} onMove={move} onClose={() => setDrawer(null)}
      onCancelExecution={executionId => perform(async () => { await api<Schema["ExecutionDto"]>(`${path}/${selected?.id}/executions/${executionId}/cancel`, { method: "POST" }); })} />}
  </>;
}

function TaskCard({ task, busy, selected, assignee, draggable, isDragging, onDragStart, onDragEnd, onOpen, onReview, onRun }: {
  task: TaskItem; busy: boolean; selected: boolean; assignee?: string; draggable: boolean; isDragging: boolean;
  onDragStart: () => void; onDragEnd: () => void; onOpen: () => void; onReview: () => void; onRun: () => void;
}) {
  const criteria = task.acceptanceCriteria.length;
  const dependencies = task.dependencyIds.length;
  return <article className={`task-card${isDragging ? " dragging" : ""}`} data-stage={task.stage} data-selected={selected} draggable={draggable}
    onDragStart={event => { event.dataTransfer.setData("text/plain", task.id); event.dataTransfer.effectAllowed = "move"; onDragStart(); }} onDragEnd={onDragEnd}>
    <button className="task-card-main" onClick={onOpen} aria-label={`Abrir tarefa ${task.title}`}>
      <span className="task-title">{task.title}</span>
      {task.description && <span className="task-desc">{task.description}</span>}
    </button>
    <div className="task-card-meta">
      <span className="chip" title={`${criteria} critério(s) de aceitação`}><Icon name="list" size="sm" />{criteria}</span>
      {dependencies > 0 && <span className="chip" title={`${dependencies} dependência(s)`}><Icon name="link" size="sm" />{dependencies}</span>}
      {assignee && <Avatar name={assignee} size={22} />}
    </div>
    {task.stage === "ready" && <div className="task-card-actions"><button className="sm" onClick={onRun} disabled={busy}><Icon name="play" size="sm" />Executar</button></div>}
    {["review", "awaitingMerge"].includes(task.stage) && <div className="task-card-actions"><button className="primary sm" onClick={onReview} disabled={busy}><Icon name="eye" size="sm" />Revisar alterações</button></div>}
  </article>;
}
