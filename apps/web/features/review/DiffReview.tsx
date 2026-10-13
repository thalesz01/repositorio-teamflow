"use client";

import Link from "next/link";
import { stageLabels } from "../workspace/api";
import { ReviewPanel } from "../workspace/ReviewPanel";
import { BrandMark, Icon } from "../workspace/ui";
import { DiffViewer, useDiff, type DiffScope } from "./DiffViewer";
import "./review.css";

/** Full-page version of the diff review; the board opens the same viewer in a modal. */
export function DiffReview(scope: DiffScope) {
  const { data, error, reload } = useDiff(scope);
  const executionPath = `/organizations/${scope.organizationId}/projects/${scope.projectId}/tasks/${scope.taskId}/executions/${scope.executionId}`;
  return <div className="review-page">
    <header className="review-topbar"><Link className="sidebar-brand" href="/"><BrandMark size={28} /><span>TeamFlow</span></Link>
      <span className="crumb" aria-hidden="true">/</span><span className="crumb">Revisão de código</span>
      <Link className="back" href="/"><Icon name="arrowLeft" size="sm" />Voltar ao quadro</Link></header>
    <main id="workspace" className="diff-review">
      {error ? <section className="diff-empty" role="alert"><h1>Não foi possível abrir a revisão</h1><p>{error}</p><button onClick={reload}>Tentar novamente</button><Link href="/">Entrar ou selecionar outro projeto</Link></section>
        : !data ? <section className="diff-empty" role="status"><h1>Carregando alterações…</h1><p>Buscando os commits e arquivos desta execução.</p></section> : <>
          <header className="diff-heading"><div><p className="eyebrow">Alterações da execução</p><h1>{data.task.title}</h1><p className="page-description">Confira o que mudou antes de aprovar este commit.</p></div><span className={`badge ${data.task.stage === "done" ? "success" : "info"}`}>{data.task.stage === "done" ? "Concluída" : ["review", "awaitingMerge"].includes(data.task.stage) ? "Pronta para revisão" : stageLabels[data.task.stage]}</span></header>
          <DiffViewer data={data} />
          <section className="diff-decision"><div><p className="eyebrow">Próxima etapa</p><h2>Revise, aprove ou solicite ajustes</h2><p className="hint">Branch: <code>{data.receipt.branch}</code>. A pasta original recebe as alterações após o merge.</p></div><ReviewPanel path={executionPath} /></section>
        </>}
    </main>
  </div>;
}
