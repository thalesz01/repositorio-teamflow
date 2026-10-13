"use client";

import { useEffect, useRef, useState } from "react";
import { api, ApiError, errorMessage, type Schema } from "./api";

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
  return <dialog className="directory-picker" ref={dialog} aria-labelledby="directory-picker-title" onCancel={event => { event.preventDefault(); close(); }}>
    <div className="inspector-heading"><h2 id="directory-picker-title">Escolher repositório local</h2><button type="button" onClick={close}>Fechar</button></div>
    <p className="hint">Navegue pelas pastas autorizadas neste computador. Nenhum arquivo é enviado.</p>
    {!error && <div className="toolbar"><button type="button" disabled={busy || directory === null} onClick={() => navigate(null)}>Unidades e pastas</button>
      <button type="button" disabled={busy || !listing?.parentPath} onClick={() => navigate(listing?.parentPath ?? null)}>Voltar uma pasta</button></div>}
    {directory && <p className="directory-location">{directory}</p>}
    {busy && <p role="status">Carregando pastas…</p>}
    {error && <div role="alert" className="error"><strong>{notConfigured ? "Nenhuma pasta liberada para este projeto" : "Não foi possível carregar as pastas"}</strong><p>{notConfigured ? "A permissão de acesso ao computador é configurada por equipe e projeto. Criar um projeto não copia essa permissão de outro workspace." : error}</p><button type="button" onClick={() => { setError(null); setBusy(true); setRetry(value => value + 1); }}>Tentar novamente</button></div>}
    {!busy && listing && <><ul className="directory-list">{(directory === null ? listing.roots.map(root => ({ name: root, path: root })) : listing.directories).map(item =>
      <li key={item.path}><button type="button" onClick={() => navigate(item.path)}><span aria-hidden="true">▱</span>{item.name}<span aria-hidden="true">›</span></button></li>)}</ul>
      {directory && !listing.directories.length && <p className="hint">Esta pasta não possui subpastas disponíveis.</p>}
      {listing.truncated && <p className="hint">Mostrando as primeiras 200 pastas.</p>}
      {directory && <p className="hint">{listing.canConnect ? "Repositório Git disponível para conexão neste projeto." : listing.isRepository ? "Você pode selecionar esta pasta, mas a conexão exige autorização neste projeto." : "Você pode usar esta pasta mesmo sem Git. A base será preparada ao iniciar uma tarefa."}</p>}
      <button className="primary" type="button" disabled={!listing.currentPath} onClick={() => { if (listing.currentPath) { dialog.current?.close(); onSelect(listing.currentPath); } }}>Usar esta pasta</button>
    </>}
  </dialog>;
}
