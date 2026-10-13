"use client";

import { useRef, useState, type FormEvent } from "react";
import { Alert, EmptyState, Icon } from "./ui";
import { api, errorMessage, type Schema, type TaskItem } from "./api";

const analysisLabels: Record<string, { label: string; tone: string }> = {
  calling: { label: "Analisando", tone: "info" }, completed: { label: "Proposta pronta", tone: "success" }, needs_information: { label: "Informações necessárias", tone: "warning" },
  failed: { label: "Análise falhou", tone: "danger" }, uncertain: { label: "Resultado incerto", tone: "warning" },
};

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
      profileId: profileId || "00000000-0000-0000-0000-000000000000", maxCostUsd: Number(data.get("budget")) };
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
  const state = analysis ? analysisLabels[analysis.state] ?? { label: analysis.state, tone: "" } : null;
  return <div className="split split-form">
    <div className="stack">
      {error && <Alert>{error}</Alert>}
      <section className="card">
        <div className="card-header"><span className="step" aria-hidden="true"><Icon name="sparkle" /></span><div><h2>Analisar demanda</h2><p>Descreva o problema. O Codex propõe uma tarefa com critérios, citando as fontes validadas.</p></div></div>
        <div className="card-body"><form onSubmit={analyze} aria-label="Analisar demanda"><fieldset disabled={busy}>
          <label>Problema ou demanda<textarea name="demand" required maxLength={4000} rows={4} placeholder="Ex.: O cálculo do frete arredonda errado em pedidos com desconto." /></label>
          <label>Comportamento esperado<textarea name="expected" aria-describedby="expected-hint" maxLength={2000} rows={3} placeholder="Como você saberá que está resolvido?" /></label>
          <p id="expected-hint" className="hint field-hint">Sem isso, a análise devolve perguntas em vez de uma proposta.</p>
          <details><summary className="hint" style={{ cursor: "pointer" }}>Opções avançadas</summary>
            <label style={{ marginTop: 12, maxWidth: 220 }}>Orçamento da análise em US$<input name="budget" type="number" min="0.01" max="5" step="0.01" defaultValue="0.50" required /></label></details>
          <div className="actions"><button className="primary">{busy ? <><span className="spinner" aria-hidden="true" />Analisando…</> : <><Icon name="sparkle" size="sm" />Analisar e propor tarefa</>}</button></div>
        </fieldset><p className="hint">Propostas exigem revisão antes da execução; fontes recuperadas não autorizam ferramentas.</p></form></div>
      </section>
      <section className="card">
        <div className="card-header"><span className="step" aria-hidden="true"><Icon name="search" /></span><div><h2>Consultar contexto</h2><p>Procure nas fontes validadas da memória do projeto.</p></div></div>
        <div className="card-body"><form onSubmit={find} aria-label="Buscar memória"><fieldset disabled={busy}>
          <label>Consulta de contexto<input name="query" required maxLength={1000} placeholder="Ex.: arredondamento" /></label>
          <div className="actions"><button>Buscar fontes validadas</button></div>
        </fieldset></form></div>
      </section>
    </div>
    <div className="stack">
      {!analysis && !context && <section className="card"><EmptyState icon="demand" title="A proposta aparece aqui">Analise uma demanda ou consulte a memória para ver propostas e fontes citadas.</EmptyState></section>}
      {analysis && state && <section className="card" aria-label="Proposta da análise">
        <div className="card-header"><div><h2>Proposta da análise</h2><p>Revise e ajuste antes de criar a tarefa.</p></div>
          <span className={`badge ${state.tone}`}>Estado da análise: {state.label}</span></div>
        <div className="card-body stack">
          <p className="hint">Custo {analysis.estimatedCostUsd == null ? "desconhecido" : `estimado: US$ ${analysis.estimatedCostUsd}`}</p>
          {analysis.failureCode && <Alert>{errorMessage({ code: analysis.failureCode })}</Alert>}
          {analysis.plan?.questions?.length ? <div><h3>O Codex precisa de mais informações</h3><ul className="question-list" style={{ marginTop: 8 }}>{analysis.plan.questions.map((question, i) => <li key={i}>{question}</li>)}</ul></div> : null}
          {analysis.state === "calling" && <div><button disabled={busy} onClick={() => void perform(async () => setAnalysis(await api<Schema["AnalysisDto"]>(`${path}/memory/analyses/${analysis.id}`)))}>Atualizar análise</button></div>}
          {analysis.state === "uncertain" && <Alert tone="warning">O resultado ou consumo ficou incerto. Confira o provedor antes de solicitar outra análise.</Alert>}
          {analysis.state === "completed" && analysis.plan && <form key={analysis.id} onSubmit={accept} aria-label="Revisar proposta"><fieldset disabled={busy}>
            <label>Título proposto<input name="title" required maxLength={160} defaultValue={analysis.plan.title} /></label>
            <label>Descrição proposta<textarea name="description" required maxLength={10000} rows={4} defaultValue={analysis.plan.description} /></label>
            <label>Critérios propostos<textarea name="criteria" aria-describedby="proposed-hint" required rows={4} defaultValue={analysis.plan.acceptanceCriteria.join("\n")} /></label>
            <p id="proposed-hint" className="hint field-hint">Um critério por linha.</p>
            <div className="actions"><button className="primary">Criar tarefa revisada na entrada</button></div>
          </fieldset></form>}
          <div><button className="ghost sm" disabled={busy} onClick={() => { request.current = null; setAnalysis(null); setContext(null); }}>Preparar nova análise</button></div>
        </div>
      </section>}
      {context && <section className="card" aria-label="Fontes recuperadas">
        <div className="card-header"><div><h2>Fontes citadas</h2><p>Trechos validados usados como contexto.</p></div></div>
        <div className="card-body stack">
          {context.warnings.map((warning, i) => <Alert key={i} tone="info">{warning}</Alert>)}
          {context.sources.map(source => <article className="source-cite" key={`${source.indexId}:${source.sequence}`}>
            <a href={`/api/v1${path}/memory/${source.itemId}/versions/${source.version}`} target="_blank" rel="noreferrer"><strong>{source.title}</strong></a>
            <span className="source-key">{source.key} · versão {source.version}</span>
            <p>{source.content}</p><small>Trecho {source.sequence} · hash {source.contentHash.slice(0, 12)}</small>
          </article>)}
          {!context.sources.length && !context.warnings.length && <p className="hint">Nenhuma fonte validada encontrada.</p>}
        </div>
      </section>}
    </div>
  </div>;
}
