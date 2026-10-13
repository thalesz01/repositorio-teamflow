"use client";

import { useEffect, useRef } from "react";
import { stageLabels } from "../workspace/api";
import { Icon } from "../workspace/ui";
import { DiffViewer, useDiff, type DiffScope } from "./DiffViewer";
import "./review.css";

/** Modal with the differences of one execution, opened from the review panel. */
export function DiffDialog({ scope, onClose }: { scope: DiffScope; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const { data, error, reload } = useDiff(scope);
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  // Close while still attached so the browser can return focus to the button that opened the modal.
  const close = () => { dialog.current?.close(); onClose(); };
  return <dialog ref={dialog} className="diff-modal" aria-label={`Diferenças da execução${data ? ` — ${data.task.title}` : ""}`}
    // Chrome closes a nested dialog and its parent together on one Escape; handling the key here closes only this modal.
    onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); } }}
    onCancel={event => { event.preventDefault(); close(); }} onClick={event => { if (event.target === dialog.current) close(); }}>
    <header className="diff-modal-head">
      <div><p className="eyebrow">Diferenças da execução</p><h2>{data ? data.task.title : "Carregando alterações…"}</h2></div>
      {data && <span className="badge info">{["review", "awaitingMerge"].includes(data.task.stage) ? "Pronta para revisão" : stageLabels[data.task.stage]}</span>}
      <button className="icon ghost" onClick={close} aria-label="Fechar diferenças" title="Fechar (Esc)"><Icon name="close" /></button>
    </header>
    <div className="diff-modal-body">
      {error ? <section className="diff-empty" role="alert"><h2>Não foi possível abrir as diferenças</h2><p>{error}</p><button onClick={reload}>Tentar novamente</button></section>
        : !data ? <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Buscando os arquivos e commits desta execução…</p>
          : <DiffViewer data={data} />}
    </div>
    <footer className="diff-modal-foot"><span className="hint">Para aprovar ou pedir ajustes, feche esta janela e use o painel da tarefa.</span><button onClick={close}>Fechar</button></footer>
  </dialog>;
}
