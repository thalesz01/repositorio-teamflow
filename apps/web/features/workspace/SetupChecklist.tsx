"use client";

import { useEffect, useState } from "react";
import { Icon } from "./ui";
import { api, type Schema } from "./api";

type Check = "loading" | "ready" | "pending";
type Step = { id: string; title: string; state: Check; detail: string; command?: string };

/** What has to be running before a task can execute. The folder is chosen per task, so it is no longer part of the setup. */
export function SetupChecklist({ organizationId, projectId, active, onCodex }: {
  organizationId: string; projectId: string; active: boolean; onCodex: (ready: boolean) => void;
}) {
  const path = `/organizations/${organizationId}/projects/${projectId}`;
  const [machine, setMachine] = useState<Schema["CodexMachineStatus"] | undefined>();
  const [runner, setRunner] = useState<Schema["RunnerStatusDto"] | undefined>();
  const [copied, setCopied] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [tick, setTick] = useState(0);
  useEffect(() => {
    if (!active) return;
    const controller = new AbortController();
    async function refresh(signal: AbortSignal) {
      const [codex, runners] = await Promise.allSettled([
        api<Schema["CodexMachineStatus"]>(`${path}/profiles/machine`, { signal }),
        api<Schema["RunnerStatusDto"]>(`${path}/runners/status`, { signal }),
      ]);
      if (signal.aborted) return;
      const status = codex.status === "fulfilled" ? codex.value : undefined;
      setMachine(status);
      setRunner(runners.status === "fulfilled" ? runners.value : undefined);
      onCodex(!!status && status.installed && status.authenticated && status.enabled);
      setRefreshing(false);
    }
    void refresh(controller.signal);
    const timer = setInterval(() => { void refresh(controller.signal); }, 15000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [active, path, tick, onCodex]);

  const known = machine !== undefined && runner !== undefined;
  const setup = `./scripts/Setup-Runner.ps1 -OrganizationId ${organizationId} -ProjectId ${projectId}`;
  const codexReady = !!machine && machine.installed && machine.authenticated && machine.enabled;
  const steps: Step[] = [
    { id: "codex", title: "Codex da máquina", state: machine === undefined ? "loading" : codexReady ? "ready" : "pending",
      detail: !machine ? "Verificando…" : !machine.installed ? "Codex não encontrado neste computador." : !machine.authenticated ? "Falta entrar com `codex login`." : !machine.enabled ? "Execução desabilitada no servidor (Codex:EnableRequests)." : "Instalado, autenticado e habilitado." },
    { id: "runner", title: "Runner", state: runner === undefined ? "loading" : runner.online ? "ready" : "pending",
      detail: !runner ? "Verificando…" : runner.online ? "Ativo: pronto para executar tarefas." : runner.configured ? "Configurado, mas parado. Inicie tudo com um comando." : "Ainda não configurado. Ele executa as tarefas em uma cópia isolada da sua pasta.",
      command: runner?.online ? undefined : runner?.configured ? "./scripts/Start-TeamFlow.ps1" : setup },
  ];
  const ready = known && steps.every(step => step.state === "ready");
  async function copy(text: string) {
    try { await navigator.clipboard.writeText(text); setCopied(text); setTimeout(() => setCopied(current => current === text ? null : current), 2000); }
    catch { setCopied(null); }
  }
  if (!active && !known) return null;
  if (ready) return <p className="env-strip" role="status"><span className="badge success">Ambiente pronto</span>Codex e runner funcionando. Você já pode executar tarefas.</p>;
  return <section className="card setup" aria-label="Primeiros passos">
    <div className="card-header"><div><h2>Primeiros passos</h2><p>Falta pouco para executar tarefas.</p></div>
      <button className="sm" style={{ marginLeft: "auto" }} onClick={() => { setRefreshing(true); setTick(value => value + 1); }} disabled={refreshing}>{refreshing ? <span className="spinner" aria-hidden="true" /> : <Icon name="refresh" size="sm" />}Verificar de novo</button></div>
    <ol className="setup-steps">{steps.map((step, index) => <li key={step.id} data-state={step.state}>
      <span className="setup-mark" aria-hidden="true">{step.state === "ready" ? <Icon name="check" size="sm" /> : step.state === "loading" ? <span className="spinner" /> : index + 1}</span>
      <div><h3>{step.title} <span className="sr-only">{step.state === "ready" ? "(pronto)" : step.state === "loading" ? "(verificando)" : "(pendente)"}</span></h3><p className="hint">{step.detail}</p>
        {step.command && <div className="command"><code>{step.command}</code><button className="sm" onClick={() => void copy(step.command!)}>{copied === step.command ? "Copiado" : "Copiar comando"}</button></div>}
        {step.id === "runner" && step.command === setup && <p className="hint">Depois, rode <code>./scripts/Start-TeamFlow.ps1</code> para iniciar a API, o web e o runner.</p>}</div>
    </li>)}</ol>
  </section>;
}
