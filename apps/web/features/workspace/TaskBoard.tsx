"use client";

import { useCallback, useEffect, useState } from "react";
import { ReviewDialog } from "../review/ReviewDialog";
import { MemoryPanel } from "./MemoryPanel";
import { NewTaskDialog, type NewTask } from "./NewTaskDialog";
import { SetupChecklist } from "./SetupChecklist";
import { TaskDrawer, type DrawerTab } from "./TaskDrawer";
import { Alert, EmptyState, Icon, PageHeader } from "./ui";
import { api, errorMessage, listAll, stageLabels, type Schema, type Stage, type TaskItem } from "./api";

export type View = "board" | "memory";
type Lane = "created" | "running" | "fix" | "done" | "cancelled";
type Drawer = { id: string; tab: DrawerTab } | null;
type Review = { taskId: string; executionId: string } | null;

// Four steps for the person (create, run, fix, conclude); the stages behind them keep their rules in the API.
const lanes: { id: Lane; stage: Stage; label: string; hint: string }[] = [
  { id: "created", stage: "ready", label: "Criadas", hint: "Prontas para executar" },
  { id: "running", stage: "execution", label: "Executando", hint: "O agente está trabalhando" },
  { id: "fix", stage: "review", label: "Corrigir", hint: "Revise, comente e conclua" },
  { id: "done", stage: "done", label: "Concluídas", hint: "Alterações aplicadas na pasta" },
  { id: "cancelled", stage: "cancelled", label: "Canceladas", hint: "" },
];
const laneOf = (stage: Stage): Lane => stage === "execution" ? "running" : stage === "review" || stage === "awaitingMerge" ? "fix"
  : stage === "done" ? "done" : stage === "cancelled" ? "cancelled" : "created";
// Tasks that predate the four-step flow may still sit before "ready"; running them walks the remaining stages.
const towardsReady: Partial<Record<Stage, Stage>> = { intake: "analysis", analysis: "ready", blocked: "analysis", failed: "ready" };
const folderName = (path: string | null | undefined) => path ? path.split(/[\\/]/).filter(Boolean).pop() ?? path : null;

export function TaskBoard({ project, view }: { project: Schema["ProjectDto"]; view: View }) {
  const projectPath = `/organizations/${project.organizationId}/projects/${project.id}`;
  const path = `${projectPath}/tasks`;
  const [profileId, setProfileId] = useState("");
  const [codexReady, setCodexReady] = useState(false);
  const [tasks, setTasks] = useState<TaskItem[]>([]);
  const [drawer, setDrawer] = useState<Drawer>(null);
  const [creating, setCreating] = useState(false);
  const [review, setReview] = useState<Review>(null);
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [keys] = useState(() => new Map<string, string>());

  const refresh = useCallback(async () => { setTasks(await listAll<TaskItem>(path + "/")); }, [path]);
  useEffect(() => {
    const controller = new AbortController();
    listAll<TaskItem>(path + "/", controller.signal).then(setTasks)
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); })
      .finally(() => { if (!controller.signal.aborted) { setBusy(false); setLoaded(true); } });
    return () => controller.abort();
  }, [path]);
  // Work finishes while nobody is looking: poll quietly while something is running.
  const running = tasks.some(task => task.stage === "execution");
  useEffect(() => {
    if (!running) return;
    const timer = setInterval(() => { void refresh().catch(() => undefined); }, 5000);
    return () => clearInterval(timer);
  }, [running, refresh]);
  // The Codex binding gives executions their model profile; it needs the machine checks of the checklist to pass first.
  useEffect(() => {
    if (!codexReady) return;
    const controller = new AbortController();
    api<Schema["ProfileDto"]>(`${projectPath}/profiles/machine`, { method: "POST", signal: controller.signal })
      .then(binding => { if (!controller.signal.aborted) setProfileId(binding.id); })
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => controller.abort();
  }, [codexReady, projectPath]);

  const selected = drawer ? tasks.find(task => task.id === drawer.id) ?? null : null;
  const needle = query.trim().toLocaleLowerCase("pt-BR");
  const filtered = needle ? tasks.filter(task => `${task.title} ${task.description}`.toLocaleLowerCase("pt-BR").includes(needle)) : tasks;
  const count = (lane: Lane) => tasks.filter(task => laneOf(task.stage) === lane).length;

  async function perform(action: () => Promise<void>) {
    setBusy(true); setError(null);
    try { await action(); }
    catch (failure) { setError(errorMessage(failure)); }
    finally {
      try { await refresh(); }
      catch (failure) { setError(previous => previous ?? errorMessage(failure)); }
      setBusy(false);
    }
  }

  async function runTask(task: TaskItem) {
    let current = task;
    for (let next = towardsReady[current.stage]; next; next = towardsReady[current.stage]) {
      current = await api<TaskItem>(`${path}/${current.id}/transition`, { method: "POST", body: JSON.stringify({ stage: next, version: current.version } satisfies Schema["TransitionInput"]) });
    }
    const versionKey = `${current.id}:${current.version}:${profileId}`;
    const key = keys.get(versionKey) ?? crypto.randomUUID();
    keys.set(versionKey, key);
    const input: Schema["ExecutionInput"] = { version: current.version, maxDurationSeconds: 900, maxAttempts: 3, maxSteps: 50, maxCostUsd: 5, agentProfileId: profileId || null };
    await api<Schema["ExecutionDto"]>(`${path}/${current.id}/executions/`, { method: "POST", headers: { "Idempotency-Key": key }, body: JSON.stringify(input) });
  }

  async function create(input: NewTask, run: boolean) {
    // The dialog shows failures itself, so this does not swallow them.
    setError(null);
    try {
      const task = await api<TaskItem>(path + "/", { method: "POST", body: JSON.stringify({ ...input, assigneeId: null, dependencyIds: [], stage: "ready" } satisfies Schema["TaskInput"]) });
      if (run) await runTask(task);
    } finally { try { await refresh(); } catch (failure) { setError(errorMessage(failure)); } }
  }

  async function save(input: Schema["TaskInput"]) {
    if (!selected) return;
    await perform(async () => {
      await api<TaskItem>(`${path}/${selected.id}`, { method: "PUT", body: JSON.stringify({ ...input, version: selected.version } satisfies Schema["TaskUpdate"]) });
    });
  }

  async function cancelTask(task: TaskItem) {
    await perform(async () => {
      await api<TaskItem>(`${path}/${task.id}/transition`, { method: "POST", body: JSON.stringify({ stage: "cancelled", version: task.version } satisfies Schema["TransitionInput"]) });
    });
  }

  async function openReview(task: TaskItem) {
    setError(null);
    try {
      const latest = await api<Schema["PageResultOfExecutionDto"]>(`${path}/${task.id}/executions/?page=1&pageSize=1`);
      const execution = latest.items[0];
      if (!execution) throw new Error("Esta tarefa ainda não tem execução para revisar.");
      setReview({ taskId: task.id, executionId: execution.id });
    } catch (failure) { setError(errorMessage(failure)); }
  }

  return <>
    <section aria-label="Quadro de tarefas" hidden={view !== "board"} className="view">
      <PageHeader eyebrow="Suas tarefas" title="Quadro" description="Crie, execute, corrija e conclua. Cada tarefa leva a pasta onde o código será alterado."
        actions={<>
          <label className="search"><span className="sr-only">Buscar tarefas</span><Icon name="search" size="sm" /><input type="search" placeholder="Buscar tarefas…" value={query} onChange={event => setQuery(event.target.value)} /></label>
          <button className="icon" aria-label="Atualizar quadro" title="Atualizar quadro" onClick={() => perform(async () => {})} disabled={busy}><Icon name="refresh" /></button>
          <button className="primary" onClick={() => setCreating(true)} disabled={busy}><Icon name="plus" size="sm" />Nova tarefa</button>
        </>} />
      <div className="board-summary" aria-label="Resumo">
        <span><strong>{loaded ? tasks.length : "—"}</strong> tarefa(s)</span>
        <span><strong>{loaded ? count("running") : "—"}</strong> executando</span>
        <span><strong>{loaded ? count("fix") : "—"}</strong> para corrigir ou concluir</span>
        <span><strong>{loaded ? count("done") : "—"}</strong> concluída(s)</span>
        <span className="sync" role="status" aria-live="polite">{busy ? <><span className="spinner" aria-hidden="true" />Sincronizando…</> : needle ? `${filtered.length} de ${tasks.length} no filtro` : ""}</span>
      </div>
      <SetupChecklist organizationId={project.organizationId} projectId={project.id} active={view === "board"} onCodex={setCodexReady} />
      {error && <Alert>{error}</Alert>}
      {loaded && !tasks.length ? <div className="card"><EmptyState icon="board" title="Nenhuma tarefa ainda" action={<button className="primary" onClick={() => setCreating(true)}><Icon name="plus" size="sm" />Criar a primeira tarefa</button>}>
        Descreva o que precisa mudar e escolha a pasta do código. O agente faz o trabalho e você revisa antes de concluir.</EmptyState></div>
        : <div className="board" aria-label="Etapas do fluxo — role horizontalmente para ver todas" tabIndex={0} aria-busy={busy}>
          {lanes.filter(lane => lane.id !== "cancelled" || count("cancelled") > 0).map(lane => {
            const items = filtered.filter(task => laneOf(task.stage) === lane.id);
            return <section className="lane" data-stage={lane.stage} key={lane.id} aria-label={lane.label}>
              <div className="lane-head"><h2><span className="lane-dot" aria-hidden="true" />{lane.label}</h2><span className="lane-count" aria-label={`${items.length} tarefa(s)`}>{items.length}</span></div>
              <div className={`lane-body${!loaded ? " loading-lanes" : ""}`}>
                {!loaded && <><div className="skeleton" /><div className="skeleton" /></>}
                {loaded && !items.length && <p className="lane-empty">{needle ? "Nada neste filtro" : lane.hint || "Nenhuma tarefa"}</p>}
                {items.map(task => <TaskCard key={task.id} task={task} busy={busy} canRun={codexReady}
                  onOpen={() => setDrawer({ id: task.id, tab: "details" })}
                  onProgress={() => setDrawer({ id: task.id, tab: "executions" })}
                  onRun={() => void perform(() => runTask(task))}
                  onReview={() => void openReview(task)} />)}
              </div>
            </section>;
          })}
        </div>}
    </section>
    <section className="view" aria-label="Biblioteca de memória" hidden={view !== "memory"}>
      <PageHeader eyebrow="Conhecimento" title="Memória do projeto" description="Guarde decisões, valide fontes e reutilize o que já foi aprendido." />
      <MemoryPanel path={projectPath} />
    </section>
    {creating && <NewTaskDialog projectPath={projectPath} canRun={codexReady && !!profileId} onCreate={create} onClose={() => setCreating(false)} />}
    {drawer && selected && <TaskDrawer error={error} task={selected} busy={busy} path={path}
      tab={drawer.tab} onTab={tab => setDrawer({ ...drawer, tab })}
      onSave={save} onCancelTask={cancelTask} onReview={() => { setDrawer(null); void openReview(selected); }} onClose={() => setDrawer(null)}
      onCancelExecution={executionId => perform(async () => { await api<Schema["ExecutionDto"]>(`${path}/${selected.id}/executions/${executionId}/cancel`, { method: "POST" }); })} />}
    {review && <ReviewDialog scope={{ organizationId: project.organizationId, projectId: project.id, taskId: review.taskId, executionId: review.executionId }}
      onClose={() => setReview(null)} onChanged={() => void refresh().catch(failure => setError(errorMessage(failure)))} />}
  </>;
}

function TaskCard({ task, busy, canRun, onOpen, onProgress, onRun, onReview }: {
  task: TaskItem; busy: boolean; canRun: boolean; onOpen: () => void; onProgress: () => void; onRun: () => void; onReview: () => void;
}) {
  const folder = folderName(task.folderPath);
  const failed = task.stage === "failed";
  return <article className="task-card" data-stage={task.stage}>
    <button className="task-card-main" onClick={onOpen} aria-label={`Abrir tarefa ${task.title}`}>
      <span className="task-title">{task.title}</span>
      {task.description && <span className="task-desc">{task.description}</span>}
    </button>
    <div className="task-card-meta">
      {folder && <span className="chip" title={task.folderPath ?? undefined}><Icon name="folder" size="sm" />{folder}</span>}
      {(failed || task.stage === "blocked" || task.stage === "awaitingMerge") && <span className={`badge ${failed ? "danger" : "warning"}`}>{stageLabels[task.stage]}</span>}
    </div>
    {laneOf(task.stage) === "created" && <div className="task-card-actions"><button className="primary sm" onClick={onRun} disabled={busy || !canRun}
      title={canRun ? undefined : "Ative o Codex da máquina (veja Primeiros passos)"}><Icon name="play" size="sm" />{failed ? "Tentar de novo" : "Executar"}</button></div>}
    {task.stage === "execution" && <div className="task-card-actions"><button className="sm" onClick={onProgress}><Icon name="clock" size="sm" />Ver progresso</button></div>}
    {laneOf(task.stage) === "fix" && <div className="task-card-actions"><button className="primary sm" onClick={onReview} disabled={busy}><Icon name="eye" size="sm" />Revisar e concluir</button></div>}
  </article>;
}
