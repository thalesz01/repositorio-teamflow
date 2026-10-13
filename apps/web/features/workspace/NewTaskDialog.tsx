"use client";

import { useEffect, useRef, useState } from "react";
import { DirectoryPicker } from "./DirectoryPicker";
import { Alert, Icon } from "./ui";
import { api, errorMessage, type Schema } from "./api";

export type NewTask = Pick<Schema["TaskInput"], "title" | "description" | "acceptanceCriteria" | "folderPath" | "baseBranch">;

const folderKey = "teamflow.lastFolder";
const defaultCriterion = "A tarefa descrita foi concluída e os testes passam.";
const readLastFolder = () => { try { return localStorage.getItem(folderKey) ?? ""; } catch { return ""; } };

/** First step of the flow: say what to do and where. The folder travels with the task. */
export function NewTaskDialog({ projectPath, canRun, onCreate, onClose }: {
  projectPath: string; canRun: boolean; onCreate: (task: NewTask, run: boolean) => Promise<void>; onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const form = useRef<HTMLFormElement>(null);
  const [folder, setFolder] = useState(readLastFolder);
  const [inspection, setInspection] = useState<Schema["LocalFolderInspection"] | null>(null);
  const [branch, setBranch] = useState("");
  const [picker, setPicker] = useState(false);
  const [busy, setBusy] = useState(false);
  const [inspected, setInspected] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [folderError, setFolderError] = useState<string | null>(null);
  useEffect(() => { const element = dialog.current; element?.showModal(); element?.querySelector<HTMLInputElement>("input[name=title]")?.focus(); return () => element?.close(); }, []);
  useEffect(() => {
    if (!folder) return;
    const controller = new AbortController();
    async function inspect() {
      try {
        const result = await api<Schema["LocalFolderInspection"]>(`${projectPath}/repository/local/inspect`, { method: "POST", signal: controller.signal, body: JSON.stringify({ path: folder }) });
        if (controller.signal.aborted) return;
        setInspection(result); setFolderError(null); setBranch(result.currentBranch ?? result.branches[0] ?? "");
      } catch (failure) { if (!controller.signal.aborted) { setInspection(null); setFolderError(errorMessage(failure)); } }
      finally { if (!controller.signal.aborted) setInspected(folder); }
    }
    void inspect();
    return () => controller.abort();
  }, [folder, projectPath]);
  function close() { if (!busy) { dialog.current?.close(); onClose(); } }
  async function submit(run: boolean) {
    if (!folder || !inspection) { setError("Escolha a pasta do projeto onde o código será alterado."); return; }
    const data = new FormData(form.current ?? undefined);
    const criteria = String(data.get("criteria")).split("\n").map(line => line.trim()).filter(Boolean);
    setBusy(true); setError(null);
    try {
      await onCreate({ title: String(data.get("title")), description: String(data.get("description")), acceptanceCriteria: criteria.length ? criteria : [defaultCriterion],
        folderPath: inspection.path, baseBranch: inspection.hasGit && branch ? branch : null }, run);
      try { localStorage.setItem(folderKey, inspection.path); } catch { /* the remembered folder is a convenience only */ }
      dialog.current?.close(); onClose();
    } catch (failure) { setError(errorMessage(failure)); setBusy(false); }
  }
  // Derived on every render, so a click right after choosing the folder cannot slip in before the check starts.
  const inspecting = folder !== "" && inspected !== folder;
  const gitNote = inspecting ? "Verificando a pasta…" : !inspection ? null
    : !inspection.hasGit ? "Esta pasta ainda não é um repositório Git. O TeamFlow prepara a base na primeira execução, sem alterar seus arquivos."
      : inspection.branches.length === 0 ? "Repositório sem commits: a base será preparada na primeira execução." : null;
  return <>
    <dialog ref={dialog} className="modal modal-wide" aria-labelledby="new-task-title" onCancel={event => { event.preventDefault(); close(); }}>
      <div className="modal-inner">
        <div className="modal-head"><span className="icon-tile" aria-hidden="true"><Icon name="plus" /></span>
          <div><h2 id="new-task-title">Nova tarefa</h2><p className="hint">Diga o que fazer e em qual pasta. O agente trabalha em uma cópia e você aprova antes de alterar a pasta.</p></div>
          <button type="button" className="icon ghost sm" aria-label="Fechar janela" onClick={close} disabled={busy}><Icon name="close" size="sm" /></button></div>
        <form ref={form} aria-label="Nova tarefa" onSubmit={event => { event.preventDefault(); void submit(false); }}><fieldset disabled={busy}>
          <label>Título<input name="title" required maxLength={160} placeholder="Ex.: Corrigir o cálculo do frete" autoComplete="off" /></label>
          <label>O que fazer<textarea name="description" required maxLength={10000} rows={4} placeholder="Qual é o problema ou a mudança desejada?" /></label>
          <label>Resultado esperado (opcional)<textarea name="criteria" rows={2} aria-describedby="criteria-help" placeholder={"Um item por linha\nEx.: O total tem duas casas decimais"} /></label>
          <p id="criteria-help" className="hint field-hint">Sem isso, a tarefa só exige que a mudança seja concluída e que os testes passem.</p>
          <div className="field"><label htmlFor="task-folder">Pasta do projeto</label>
            <div className="select-row"><input id="task-folder" name="folder" value={folder} readOnly placeholder="Escolha a pasta com o código" aria-describedby="folder-help" />
              <button type="button" onClick={() => setPicker(true)}><Icon name="folder" size="sm" />{folder ? "Trocar pasta" : "Escolher pasta"}</button></div>
            <p id="folder-help" className="hint field-hint">{gitNote ?? "A pasta fica gravada na tarefa. Você pode usar uma pasta diferente em cada tarefa."}</p></div>
          {folderError && <Alert>{folderError}</Alert>}
          {inspection?.hasGit && inspection.branches.length > 1 && <label>Branch de partida<select value={branch} onChange={event => setBranch(event.target.value)}>
            {inspection.branches.map(name => <option key={name} value={name}>{name}{name === inspection.currentBranch ? " (atual)" : ""}</option>)}</select></label>}
          {error && <Alert>{error}</Alert>}
          <div className="modal-actions"><button type="button" onClick={close}>Cancelar</button>
            <button type="button" disabled={!canRun || inspecting} title={canRun ? undefined : "Ative o Codex da máquina para executar"} onClick={() => { if (form.current?.reportValidity()) void submit(true); }}>Criar e executar</button>
            <button className="primary" type="submit" disabled={inspecting}>{busy ? "Criando…" : "Criar tarefa"}</button></div>
        </fieldset></form>
      </div>
    </dialog>
    {picker && <DirectoryPicker path={projectPath} onClose={() => setPicker(false)} onSelect={directory => { setPicker(false); setFolder(directory); }} />}
  </>;
}
