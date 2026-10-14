"use client";

import { useEffect, useRef, useState } from "react";
import { api, errorMessage } from "./api";
import { Alert, Icon } from "./ui";

export function ExecutionReportDialog({ path, onClose }: { path: string; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const content = useRef<HTMLTextAreaElement>(null);
  const [report, setReport] = useState<string | null>(null);
  const [failureMessage, setFailureMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const element = dialog.current; element?.showModal();
    return () => element?.close();
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    void api<Record<string, unknown> & { failure?: { message?: string } | null }>(path, { signal: controller.signal }).then(result => {
      if (!controller.signal.aborted) { setReport(JSON.stringify(result, null, 2)); setFailureMessage(typeof result.failure?.message === "string" ? result.failure.message : null); setError(null); }
    }).catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => controller.abort();
  }, [path, retry]);
  function close() { dialog.current?.close(); onClose(); }
  async function copy() {
    if (!report) return;
    try { await navigator.clipboard.writeText(report); setCopied(true); }
    catch { content.current?.focus(); content.current?.select(); setError("Texto selecionado. Use Ctrl+C para copiar."); }
  }
  return <dialog ref={dialog} className="drawer execution-report-modal" aria-labelledby="execution-report-title"
    onCancel={event => { event.preventDefault(); event.stopPropagation(); close(); }}
    onClick={event => { if (event.target === dialog.current) close(); }}>
    <header className="drawer-head"><div className="drawer-top"><div>
      <p className="eyebrow">Histórico e diagnóstico</p><h2 id="execution-report-title">Relatório da execução</h2>
      <p className="hint">Etapas, resultado e saídas de teste disponíveis. Selecione o texto ou copie o relatório completo.</p>
    </div><button className="icon ghost" aria-label="Fechar relatório" onClick={close}><Icon name="close" /></button></div></header>
    <div className="drawer-body">
      {error && <Alert>{error}</Alert>}
      {failureMessage && <Alert>{failureMessage}</Alert>}
      {!report && !error && <p role="status">Carregando relatório…</p>}
      {!report && error && <button onClick={() => { setError(null); setRetry(value => value + 1); }}>Tentar novamente</button>}
      {report && <textarea ref={content} className="execution-report-content" aria-label="Conteúdo do relatório" value={report} readOnly spellCheck={false} />}
    </div>
    <footer className="drawer-foot"><span role="status" className="hint">{copied ? "Relatório copiado." : "O histórico anterior é preservado."}</span>
      <div className="actions"><button onClick={close}>Fechar</button><button className="primary" disabled={!report} onClick={() => void copy()}>{copied ? "Copiado" : "Copiar relatório"}</button></div>
    </footer>
  </dialog>;
}
