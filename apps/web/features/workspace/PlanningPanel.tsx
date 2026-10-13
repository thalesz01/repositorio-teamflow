"use client";

import { useRef, useState, type FormEvent } from "react";
import { api, errorMessage, type Schema, type TaskItem } from "./api";

const analysisLabels: Record<string, string> = { calling: "Analisando", completed: "Proposta pronta", needs_information: "Informações necessárias", failed: "Análise falhou", uncertain: "Resultado incerto" };

export function PlanningPanel({ path, profileId, onCreated }: { path: string; profileId: string; onCreated: (task: TaskItem) => void }) {
  const [analysis, setAnalysis] = useState<Schema["AnalysisDto"] | null>(null);
  const [context, setContext] = useState<Schema["ContextBundle"] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const request = useRef<{ payload: string; key: string } | null>(null);
  async function perform(action: () => Promise<void>) {
    setBusy(true); setError(null);
    try { await action(); } catch (failure) { setError(errorMessage(failure)); } finally { setBusy(false); }
  }
  async function find(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const query = String(new FormData(event.currentTarget).get("query"));
    await perform(async () => { setContext(await api<Schema["ContextBundle"]>(`${path}/memory/search`, { method: "POST", body: JSON.stringify({ query, maxBytes: 8192 }) })); });
  }
  async function analyze(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const data = new FormData(event.currentTarget);
    const input = { demand: String(data.get("demand")), expectedBehavior: String(data.get("expected")) || null,
      profileId: profileId || "00000000-0000-0000-0000-000000000000", maxCostUsd: Number(data.get("budget")), semantic: data.has("semantic") };
    const payload = JSON.stringify(input);
    if (request.current?.payload !== payload) request.current = { payload, key: crypto.randomUUID() };
    const key = request.current.key;
    await perform(async () => {
      const result = await api<Schema["AnalysisDto"]>(`${path}/memory/analyses`, { method: "POST", body: JSON.stringify({ ...input, idempotencyKey: key }) });
      setAnalysis(result); setContext(result.context ?? null);
    });
  }
  async function accept(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!analysis || analysis.state !== "completed") return;
    const data = new FormData(event.currentTarget);
    const input: Schema["TaskInput"] = { title: String(data.get("title")), description: String(data.get("description")),
      acceptanceCriteria: String(data.get("criteria")).split("\n").map(s => s.trim()).filter(Boolean), assigneeId: null, dependencyIds: [], analysisId: analysis.id };
    await perform(async () => { onCreated(await api<TaskItem>(`${path}/tasks/`, { method: "POST", body: JSON.stringify(input) })); setAnalysis(null); });
  }
  return <details className="detail-panel" open><summary>Analisar demanda e consultar contexto</summary>
    {error && <p role="alert" className="error">{error}</p>}
    <form onSubmit={find} aria-label="Buscar memória"><fieldset disabled={busy}>
      <label>Consulta de contexto<input name="query" required maxLength={1000} /></label><button>Buscar fontes validadas</button>
    </fieldset></form>
    <form onSubmit={analyze} aria-label="Analisar demanda"><fieldset disabled={busy}>
      <label>Problema ou demanda<textarea name="demand" required maxLength={4000} rows={3} /></label>
      <label>Comportamento esperado<textarea name="expected" maxLength={2000} rows={2} /></label>
      <label>Orçamento da análise em US$<input name="budget" type="number" min="0.01" max="5" step="0.01" defaultValue="0.50" required /></label>
      <label><input name="semantic" type="checkbox" /> Usar também busca semântica (embedding pago da consulta)</label>
      <button>Analisar e propor tarefa</button>
    </fieldset><p className="hint">A análise usa o perfil selecionado no projeto. Informações essenciais ausentes geram perguntas. Propostas exigem revisão antes da execução; fontes recuperadas não autorizam ferramentas.</p></form>
    {analysis && <section aria-label="Proposta da análise"><p>Estado da análise: {analysisLabels[analysis.state] ?? analysis.state} · custo {analysis.estimatedCostUsd == null ? "desconhecido" : `US$ ${analysis.estimatedCostUsd}`}</p>
      {analysis.failureCode && <p role="alert">{errorMessage({ code: analysis.failureCode })}</p>}
      {analysis.plan?.questions?.length ? <ul>{analysis.plan.questions.map((question, i) => <li key={i}>{question}</li>)}</ul> : null}
      {analysis.state === "calling" && <button disabled={busy} onClick={() => void perform(async () => setAnalysis(await api<Schema["AnalysisDto"]>(`${path}/memory/analyses/${analysis.id}`)))}>Atualizar análise</button>}
      {analysis.state === "uncertain" && <p>O resultado ou consumo ficou incerto. Confira o provedor antes de solicitar outra análise.</p>}
      {analysis.state === "completed" && analysis.plan && <form key={analysis.id} onSubmit={accept} aria-label="Revisar proposta"><fieldset disabled={busy}>
        <label>Título proposto<input name="title" required maxLength={160} defaultValue={analysis.plan.title} /></label>
        <label>Descrição proposta<textarea name="description" required maxLength={10000} rows={4} defaultValue={analysis.plan.description} /></label>
        <label>Critérios propostos<textarea name="criteria" required rows={4} defaultValue={analysis.plan.acceptanceCriteria.join("\n")} /></label>
        <button>Criar tarefa revisada na entrada</button>
      </fieldset></form>}
    </section>}
    {analysis && <button disabled={busy} onClick={() => { request.current = null; setAnalysis(null); setContext(null); }}>Preparar nova análise</button>}
    {context && <section aria-label="Fontes recuperadas"><h3>Fontes citadas</h3>
      {context.warnings.map((warning, i) => <p key={i}>{warning}</p>)}
      <ul>{context.sources.map(source => <li key={`${source.indexId}:${source.sequence}`}>
        <a href={`/api/v1${path}/memory/${source.itemId}/versions/${source.version}`} target="_blank" rel="noreferrer">{source.title} · {source.key} · versão {source.version}</a>
        <p>{source.content}</p><small>Trecho {source.sequence} · hash {source.contentHash}</small>
      </li>)}</ul>
    </section>}
  </details>;
}
