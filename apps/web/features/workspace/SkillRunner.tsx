"use client";

import { useEffect, useState } from "react";
import { Alert, Icon } from "./ui";
import { api, errorMessage, type Schema } from "./api";

type Skill = Schema["SkillInfo"];

/** Runs the task with a skill (a SKILL.md from the task folder or from the user's own skills). The text is sent to the agent as guidance. */
export function SkillRunner({ taskPath, busy, canRun, onRun, retry = false }: {
  taskPath: string; busy: boolean; canRun: boolean; retry?: boolean; onRun: (skill: string | null) => Promise<void>;
}) {
  const [skills, setSkills] = useState<Skill[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    api<Skill[]>(`${taskPath}/skills`, { signal: controller.signal }).then(setSkills)
      .catch(failure => { if (!controller.signal.aborted) { setSkills([]); setError(errorMessage(failure)); } });
    return () => controller.abort();
  }, [taskPath]);
  const skill = skills?.find(item => item.id === selected);
  return <section className="card skill-runner" aria-label="Executar com skill">
    <div className="card-header"><div><h3>{retry ? "Nova execução" : "Execução"}</h3><p>O agente implementa e testa a mudança.</p></div></div>
    <div className="card-body stack">
      {error && <Alert>{error}</Alert>}
      <label>Skill (opcional)<select value={selected} onChange={event => setSelected(event.target.value)} disabled={busy || skills === null}>
        <option value="">Execução padrão</option>
        {skills?.map(item => <option key={item.id} value={item.id}>{item.name} · {item.source === "project" ? "do projeto" : "pessoal"}</option>)}
      </select></label>
      {skills !== null && !skills.length && !error && <p className="hint">Nenhuma skill disponível. Você pode executar normalmente.</p>}
      {skill && <div className="skill-detail"><p>{skill.description || "Sem descrição."}</p>
        <details><summary className="hint">Ver o começo do texto enviado ({skill.bytes} bytes no total)</summary><pre>{skill.preview}</pre></details></div>}
      <div className="actions"><button className="primary" disabled={busy || !canRun} title={canRun ? undefined : "Ative uma IA para executar (veja Primeiros passos)"} onClick={() => void onRun(selected || null)}>
        <Icon name="play" size="sm" />{selected ? "Executar com a skill" : retry ? "Executar novamente" : "Executar"}</button></div>
    </div>
  </section>;
}
