"use client";

import { useEffect, useState } from "react";
import { api, ApiError, errorMessage, type TaskItem, type Schema } from "../workspace/api";
import { parseUnifiedDiff, type DiffFile } from "./unified-diff";
import "./review.css";

export type DiffScope = { organizationId: string; projectId: string; taskId: string; executionId: string };
export type DiffData = { task: TaskItem; receipt: Schema["PublishedPullRequest"]; files: DiffFile[] };
const labels = { added: "Novo", removed: "Removido", modified: "Alterado", renamed: "Renomeado" };

/** Loads the task, the publication receipt and the unified diff of one local review. */
export function useDiff({ organizationId, projectId, taskId, executionId }: DiffScope) {
  const [data, setData] = useState<DiffData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  const projectPath = `/organizations/${organizationId}/projects/${projectId}`;
  const taskPath = `${projectPath}/tasks/${taskId}`;
  const executionPath = `${taskPath}/executions/${executionId}`;
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      const ids = [organizationId, projectId, taskId, executionId];
      if (ids.some(id => !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id))) throw new Error("Endereço de revisão inválido.");
      const [task, publication] = await Promise.all([
        api<TaskItem>(taskPath, { signal: controller.signal }),
        api<Schema["PublicationStatus"]>(`${executionPath}/publication`, { signal: controller.signal }),
      ]);
      if (!publication.pullRequest || publication.state !== "published") throw new Error("As alterações desta execução ainda não foram publicadas.");
      if (!publication.pullRequest.url.startsWith(`/api/v1${projectPath}/repository/local/reviews/${executionId}`)) throw new Error("Esta página exibe revisões de repositórios locais.");
      const response = await fetch(`/api/v1${projectPath}/repository/local/reviews/${executionId}`, { credentials: "same-origin", cache: "no-store", signal: controller.signal });
      if (!response.ok) {
        const problem: { code?: string; title?: string } = await response.json().catch(() => ({}));
        throw new ApiError(response.status, problem.code ?? "diff_unavailable", problem.title ?? "Não foi possível carregar as alterações.");
      }
      const files = parseUnifiedDiff(await response.text());
      if (!controller.signal.aborted) { setData({ task, receipt: publication.pullRequest, files }); setError(null); }
    }
    void load().catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => controller.abort();
  }, [organizationId, projectId, taskId, executionId, projectPath, taskPath, executionPath, retry]);
  return { data, error, reload: () => { setError(null); setRetry(value => value + 1); } };
}

/** Totals, goal, file list and the code reader. Shared by the full page and the modal. */
export function DiffViewer({ data }: { data: DiffData }) {
  const [selected, setSelected] = useState(0);
  const [search, setSearch] = useState("");
  const [changesOnly, setChangesOnly] = useState(false);
  const file = data.files[selected];
  const additions = data.files.reduce((sum, item) => sum + item.additions, 0);
  const deletions = data.files.reduce((sum, item) => sum + item.deletions, 0);
  const matches = (path: string) => path.toLocaleLowerCase().includes(search.toLocaleLowerCase());
  return <>
    <div className="diff-summary"><div><span>Arquivos alterados</span><strong>{data.files.length}</strong></div><div><span>Linhas adicionadas</span><strong className="diff-positive">+{additions}</strong></div><div><span>Linhas removidas</span><strong className="diff-negative">−{deletions}</strong></div><div className="diff-commits"><span>Base → Alterações</span><code title={`${data.receipt.baseCommit} → ${data.receipt.headCommit}`}>{data.receipt.baseCommit.slice(0, 8)} → {data.receipt.headCommit.slice(0, 8)}</code></div></div>
    <details className="diff-context"><summary>Objetivo e critérios de aceitação</summary><p>{data.task.description}</p><ul>{data.task.acceptanceCriteria.map((criterion, index) => <li key={index}>{criterion}</li>)}</ul></details>
    <div className="diff-layout"><aside className="diff-files"><div className="diff-files-heading"><h2>Arquivos</h2><span>{data.files.length}</span></div><label className="diff-search">Filtrar arquivos<input type="search" placeholder="Buscar pelo nome…" value={search} onChange={event => setSearch(event.target.value)} /></label><nav aria-label="Arquivos alterados">
      {data.files.map((item, index) => matches(item.path) && <button key={index} aria-current={index === selected ? "true" : undefined} onClick={() => setSelected(index)}><span className={`diff-file-status ${item.status}`} aria-label={labels[item.status]}>{item.status === "added" ? "+" : item.status === "removed" ? "−" : "◈"}</span><span className="diff-file-name" title={item.path}>{item.path}</span><span className="diff-file-count"><span className="diff-positive">+{item.additions}</span><span className="diff-negative">−{item.deletions}</span></span></button>)}
      {!data.files.some(item => matches(item.path)) && <p className="hint">Nenhum arquivo encontrado.</p>}
    </nav></aside><section className="diff-reader" aria-label="Comparação de código">
      {file ? <><header className="diff-file-heading"><div><span className={`diff-status-label ${file.status}`}>{labels[file.status]}</span><h2>{file.path}</h2></div><div className="diff-navigation"><button aria-label="Arquivo anterior" disabled={selected === 0} onClick={() => setSelected(selected - 1)}>←</button><span>{selected + 1} / {data.files.length}</span><button aria-label="Próximo arquivo" disabled={selected === data.files.length - 1} onClick={() => setSelected(selected + 1)}>→</button></div></header><div className="diff-toolbar"><span><span className="diff-positive">+ Adições</span> <span className="diff-negative">− Remoções</span></span><label><input type="checkbox" checked={changesOnly} onChange={event => setChangesOnly(event.target.checked)} />Somente alterações</label></div>
        {file.binary ? <p className="diff-empty">Arquivo binário alterado. A comparação textual não está disponível.</p> : !file.lines.length ? <p className="diff-empty">Alteração de nome, permissões ou arquivo vazio; sem linhas modificadas.</p> : <div className="diff-code-scroll"><table className="diff-code" aria-label={`Diff de ${file.path}`}><thead><tr><th scope="col">Antes</th><th scope="col">Depois</th><th scope="col" aria-label="Tipo de alteração" /><th scope="col">Código</th></tr></thead><tbody>{file.lines.filter(line => !changesOnly || line.kind !== "context").map((line, index) => line.kind === "hunk" || line.kind === "meta" ? <tr key={index} className={`diff-line-${line.kind}`}><td colSpan={4}><code>{line.text}</code></td></tr> : <tr key={index} className={`diff-line-${line.kind}`}><td className="diff-line-number">{line.before}</td><td className="diff-line-number">{line.after}</td><td className="diff-line-sign" aria-label={line.kind === "added" ? "Adição" : line.kind === "removed" ? "Remoção" : "Contexto"}>{line.kind === "added" ? "+" : line.kind === "removed" ? "−" : " "}</td><td><code>{line.text || " "}</code></td></tr>)}</tbody></table></div>}
      </> : <div className="diff-empty"><h2>Nenhum diff textual encontrado</h2><p>Confira a publicação e o commit desta execução.</p></div>}
    </section></div>
  </>;
}
