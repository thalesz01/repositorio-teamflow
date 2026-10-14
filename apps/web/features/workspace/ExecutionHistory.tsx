"use client";

import { useEffect, useState } from "react";
import { ExecutionReportDialog } from "./ExecutionReportDialog";
import { ExecutionProgress } from "./ExecutionProgress";
import { Alert, EmptyState, Icon } from "./ui";
import { errorMessage, listAll, type Schema } from "./api";

type ExecutionState = Schema["ExecutionState"];
export const executionStates: Record<ExecutionState, { label: string; tone: string }> = {
  queued: { label: "Na fila", tone: "info" }, leased: { label: "Em execução", tone: "brand" }, succeeded: { label: "Finalizada", tone: "success" },
  failed: { label: "Falhou", tone: "danger" }, cancelled: { label: "Cancelada", tone: "" },
};

export function ExecutionHistory({ path, busy, stage, onCancel, onCount, onReview, onAnswer }: { path: string; busy: boolean; stage?: Schema["TaskStage"]; onCancel: (id: string) => Promise<void>; onCount?: (count: number) => void; onReview?: () => void; onAnswer?: () => void }) {
  const [reportId, setReportId] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [executions, setExecutions] = useState<Schema["ExecutionDto"][]>([]);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    let refreshing = false;
    async function refresh() {
      if (refreshing) return;
      refreshing = true;
      try {
        const result = await listAll<Schema["ExecutionDto"]>(path + "/", controller.signal);
        if (!controller.signal.aborted) { setExecutions(result); setLoaded(true); setError(null); onCount?.(result.length); }
      } catch (failure) { if (!controller.signal.aborted) setError(errorMessage(failure)); }
      finally { refreshing = false; }
    }
    void refresh();
    const timer = setInterval(() => { void refresh(); }, 5000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [path, onCount]);
  const latest = executions[0];
  const review = latest?.state === "succeeded" ? latest : null;
  return <div className="stack">
    {error && <Alert>{error}</Alert>}
    {review && onAnswer && <section className="review-callout" aria-label="Resposta da análise"><header><Icon name="check" /><div><h3>Análise respondida</h3><p className="hint">O agente leu os arquivos e registrou a resposta. Nada foi alterado na pasta.</p></div><button className="primary sm" onClick={onAnswer}><Icon name="eye" size="sm" />Ver resposta</button></header></section>}
    {review && !onAnswer && <section className="review-callout" aria-label="Revisão da execução mais recente">
      <header><Icon name="check" /><div><h3>{stage === "done" ? "Concluída" : "Pronta para revisão"}</h3>
        <p className="hint">{stage === "done" ? "As alterações foram aplicadas na pasta do projeto." : "Revise o que mudou, comente o que precisa de ajuste e conclua. A pasta só recebe o código ao concluir."}</p></div>
        {onReview && ["review", "awaitingMerge"].includes(stage ?? "") && <button className="primary sm" onClick={onReview}><Icon name="eye" size="sm" />Revisar e concluir</button>}</header>
    </section>}
    {loaded && !executions.length && <div className="card"><EmptyState icon="play" title="Nenhuma execução registrada">Use <strong>Executar</strong> no cartão da tarefa para começar.</EmptyState></div>}
    {reportId && <ExecutionReportDialog path={`${path}/${reportId}/report`} onClose={() => setReportId(null)} />}
    <div className="run-list">
      {executions.map(item => {
        const state = executionStates[item.state];
        return <div className="card run" key={item.id}>
          <div className="run-head">
            <span className={`badge ${state.tone}`}>{state.label}</span>
            <span>Tentativa {item.attempt} de {item.maxAttempts}</span>
            {item.cancellationRequested && <span className="badge warning">Cancelamento solicitado</span>}
            {item.skill && <span className="badge info">Skill: {item.skill}</span>}
            <span className="when">{item.id === latest?.id ? "Execução mais recente" : "Execução anterior"} · {new Date(item.createdAt).toLocaleString("pt-BR")}</span>
            <div className="run-actions">
              {["queued", "leased"].includes(item.state) && <button className="danger sm" disabled={busy || item.cancellationRequested} onClick={() => onCancel(item.id)}>Cancelar execução</button>}
              <button className="sm" onClick={() => setReportId(item.id)}>Relatório da execução</button>
              <button className="sm" onClick={() => setExpanded(current => current === item.id ? null : item.id)} aria-expanded={expanded === item.id}>Ver progresso</button>
            </div>
          </div>
          {item.state === "queued" && item.attempt === 0 && !item.cancellationRequested && <div style={{ marginTop: 12 }}><Alert tone="info">Esta execução só começa quando houver um <strong>runner ativo</strong>. Rode <code>./scripts/Start-TeamFlow.ps1</code>; veja <code>docs/runner-local.md</code>.</Alert></div>}
          {expanded === item.id && <ExecutionProgress key={item.id} path={`${path}/${item.id}`} />}
        </div>;
      })}
    </div>
  </div>;
}
