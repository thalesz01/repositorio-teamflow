"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { errorMessage } from "./api";

export function CreateWorkspaceItem({ kind, teamName, onCreate, onClose }: {
  kind: "team" | "project"; teamName?: string; onCreate: (name: string) => Promise<void>; onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const title = kind === "team" ? "Nova equipe" : "Novo projeto";
  useEffect(() => { const element = dialog.current; element?.showModal(); input.current?.focus(); return () => element?.close(); }, []);
  function close() { if (!busy) { dialog.current?.close(); onClose(); } }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const name = input.current?.value.trim() ?? "";
    if (!name) { setError("Informe um nome para continuar."); input.current?.focus(); return; }
    setBusy(true); setError(null);
    try { await onCreate(name); dialog.current?.close(); onClose(); }
    catch (failure) { setError(errorMessage(failure)); setBusy(false); }
  }
  return <dialog ref={dialog} className="create-context-dialog" aria-labelledby="create-context-title" aria-describedby="create-context-description"
    onCancel={event => { event.preventDefault(); close(); }}>
    <div className="dialog-heading"><span className="context-symbol" aria-hidden="true">{kind === "team" ? "E" : "P"}</span><button type="button" aria-label="Fechar janela" onClick={close} disabled={busy}>×</button></div>
    <h2 id="create-context-title">{kind === "team" ? "Criar equipe" : "Criar projeto"}</h2>
    <p id="create-context-description">{kind === "team" ? "Reúna as pessoas e os projetos em um espaço compartilhado." : <>Organize tarefas, código e memória dentro da equipe <strong>{teamName}</strong>.</>}</p>
    <form onSubmit={submit}><label>{title}<input ref={input} name="name" required maxLength={120} disabled={busy} placeholder={kind === "team" ? "Ex.: Engenharia de produto" : "Ex.: Plataforma de licitações"} autoComplete="off" /></label>
      {error && <p role="alert" className="error">{error}</p>}
      {busy && <p role="status" className="hint">{kind === "team" ? "Criando equipe…" : "Criando projeto…"}</p>}
      <div className="dialog-actions"><button type="button" onClick={close} disabled={busy}>Cancelar</button><button className="primary" disabled={busy}>{kind === "team" ? "Criar equipe" : "Criar projeto"}</button></div>
    </form>
  </dialog>;
}
