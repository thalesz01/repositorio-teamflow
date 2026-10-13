"use client";

import { useEffect, useRef, useState } from "react";
import { api, ApiError, errorMessage, type Schema } from "./api";
import { Icon } from "./ui";

export function DirectoryPicker({ path, onSelect, onClose }: { path: string; onSelect: (directory: string) => void; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [directory, setDirectory] = useState<string | null>(null);
  const [listing, setListing] = useState<Schema["LocalDirectoryListing"] | null>(null);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notConfigured, setNotConfigured] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  useEffect(() => {
    const controller = new AbortController();
    api<Schema["LocalDirectoryListing"]>(`${path}/repository/local/directories${directory ? `?path=${encodeURIComponent(directory)}` : ""}`, { signal: controller.signal })
      .then(setListing).catch(failure => { if (!controller.signal.aborted) { setNotConfigured(failure instanceof ApiError && failure.code === "integration_not_configured"); setError(errorMessage(failure)); } })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [path, directory, retry]);
  function close() { dialog.current?.close(); onClose(); }
  function navigate(next: string | null) { setBusy(true); setError(null); setListing(null); setDirectory(next); }
  return <dialog className="modal modal-wide" ref={dialog} aria-labelledby="directory-picker-title" onCancel={event => { event.preventDefault(); close(); }}>
    <div className="modal-inner">
      <div className="modal-head"><span className="icon-tile" aria-hidden="true"><Icon name="folder" /></span>
        <div><h2 id="directory-picker-title">Escolher repositório local</h2><p className="hint">Navegue pelas pastas autorizadas neste computador. Nenhum arquivo é enviado.</p></div>
        <button type="button" className="icon ghost sm" aria-label="Fechar" onClick={close}><Icon name="close" size="sm" /></button></div>
      {!error && <div className="actions"><button type="button" className="sm" disabled={busy || directory === null} onClick={() => navigate(null)}>Unidades e pastas</button>
        <button type="button" className="sm" disabled={busy || !listing?.parentPath} onClick={() => navigate(listing?.parentPath ?? null)}><Icon name="arrowLeft" size="sm" />Voltar uma pasta</button></div>}
      {directory && <p className="dir-location">{directory}</p>}
      {busy && <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Carregando pastas…</p>}
      {error && <div role="alert" className="alert danger"><Icon name="alert" size="sm" /><div><strong>{notConfigured ? "Nenhuma pasta liberada para este projeto" : "Não foi possível carregar as pastas"}</strong>
        <p>{notConfigured ? "A permissão de acesso ao computador é configurada por equipe e projeto. Criar um projeto não copia essa permissão de outro workspace." : error}</p>
        <p><button type="button" className="sm" onClick={() => { setError(null); setBusy(true); setRetry(value => value + 1); }}>Tentar novamente</button></p></div></div>}
      {!busy && listing && <>
        <ul className="dir-list">{(directory === null ? listing.roots.map(root => ({ name: root, path: root })) : listing.directories).map(item =>
          <li key={item.path}><button type="button" onClick={() => navigate(item.path)}><Icon name="folder" size="sm" />{item.name}<span className="chev" aria-hidden="true"><Icon name="chevronRight" size="sm" /></span></button></li>)}</ul>
        {directory && !listing.directories.length && <p className="hint">Esta pasta não possui subpastas disponíveis.</p>}
        {listing.truncated && <p className="hint">Mostrando as primeiras 200 pastas.</p>}
        {directory && <p className="hint">{listing.canConnect ? "Repositório Git disponível para conexão neste projeto." : listing.isRepository ? "Você pode selecionar esta pasta, mas a conexão exige autorização neste projeto." : "Você pode usar esta pasta mesmo sem Git. A base será preparada ao iniciar uma tarefa."}</p>}
        <div className="modal-actions"><button type="button" onClick={close}>Cancelar</button><button className="primary" type="button" disabled={!listing.currentPath} onClick={() => { if (listing.currentPath) { dialog.current?.close(); onSelect(listing.currentPath); } }}>Usar esta pasta</button></div>
      </>}
    </div>
  </dialog>;
}
