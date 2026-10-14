"use client";

import { useEffect, useState } from "react";
import { api, errorMessage, type Schema } from "./api";

type Agent = Schema["CliAgent"];
export type Choice = Schema["AgentChoice"];
const effortLabels: Record<string, string> = { low: "Leve", medium: "Médio", high: "Alto", xhigh: "Muito alto", max: "Máximo" };

export function AiSelection({ projectPath, value, onChange, onReady }: { projectPath: string; value: Choice; onChange: (choice: Choice, ready: boolean) => void; onReady: (ready: boolean) => void }) {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    api<Agent[]>(`${projectPath}/profiles/agents`, { signal: controller.signal }).then(setAgents)
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => controller.abort();
  }, [projectPath]);
  const agent = agents.find(item => item.id === value.provider);
  const model = agent?.models.find(item => item.id === value.model);
  const ready = !!agent?.installed && agent.authenticated && agent.enabled;
  useEffect(() => { onReady(ready); }, [ready, onReady]);
  function choose(choice: Choice) { const next = agents.find(item => item.id === choice.provider); onChange(choice, !!next?.installed && next.authenticated && next.enabled); }
  return <section className="new-task-ai" aria-label="Configuração da IA">
    <div className="new-task-ai-heading"><h3>Agente de IA</h3><span className={`badge ${ready ? "success" : "neutral"}`}>{ready ? "Disponível na máquina" : !agent ? "Verificando…" : !agent.installed ? "Não instalado" : !agent.authenticated ? "Login necessário" : "Desabilitado"}</span></div>
    <div className="new-task-ai-fields">
      <label>IA<select value={value.provider ?? "codex"} disabled={!agents.length} onChange={event => choose({ provider: event.target.value, model: "default", effort: null })}>{agents.length ? agents.map(item => <option key={item.id} value={item.id}>{item.name}</option>) : <option value="codex">Codex</option>}</select></label>
      <label>Modelo<select value={value.model ?? "default"} disabled={!agent} onChange={event => choose({ ...value, model: event.target.value, effort: null })}>{agent?.models.map(item => <option key={item.id} value={item.id}>{item.id === "default" ? "Automático do CLI" : item.id}</option>) ?? <option value="default">Automático do CLI</option>}</select></label>
      <label>Esforço de raciocínio<select value={value.effort ?? ""} disabled={!model?.efforts.length} onChange={event => choose({ ...value, effort: event.target.value || null })}><option value="">Automático</option>{model?.efforts.map(effort => <option key={effort} value={effort}>{effortLabels[effort] ?? effort}</option>)}</select></label>
    </div>
    <p className="hint field-hint">{error ?? (!agent?.authenticated && agent?.installed ? `Faça login pelo comando ${agent.id === "claude" ? "claude auth login" : "codex login"}.` : "Esforço maior prioriza profundidade; esforço leve prioriza rapidez. Os limites dependem do modelo e da sua conta.")}</p>
  </section>;
}
