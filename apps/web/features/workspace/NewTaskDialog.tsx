"use client";

import { useEffect, useRef, useState } from "react";
import { DirectoryPicker } from "./DirectoryPicker";
import { AiSelection, type Choice } from "./AiSelection";
import { Alert, Icon } from "./ui";
import { api, errorMessage, type Schema } from "./api";

export type NewTask = Pick<Schema["TaskInput"], "title" | "description" | "acceptanceCriteria" | "folderPath" | "baseBranch" | "ai" | "kind" | "previousTaskId">;
export type PreviousStep = { id: string; title: string; folderPath: string; baseBranch: string | null };

const branchOrder = new Map([["main", 0], ["master", 1], ["develop", 2]]);
const folderKey = "teamflow.lastFolder";
const defaultCriterion = "A tarefa descrita foi concluída e os testes passam.";
const analysisCriterion = "A pergunta foi respondida com base nos arquivos consultados, citando onde a regra está.";
const readLastFolder = () => { try { return localStorage.getItem(folderKey) ?? ""; } catch { return ""; } };

/** First step of the flow: say what to do and where. The folder travels with the task. */
export function NewTaskDialog({ projectPath, canRun, steps, onCreate, onClose }: {
  projectPath: string; canRun: boolean; steps: PreviousStep[]; onCreate: (task: NewTask, run: boolean) => Promise<void>; onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const form = useRef<HTMLFormElement>(null);
  const [criteriaText, setCriteriaText] = useState("");
  const [mode, setMode] = useState<"change" | "analysis">("change");
  const [folder, setFolder] = useState(readLastFolder);
  const [previousId, setPreviousId] = useState("");
  const [ai, setAi] = useState<Choice>({ provider: "codex", model: "default", effort: null });
  const [agentReady, setAgentReady] = useState(canRun);
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
        const branches = result.branches.toSorted((a, b) => (branchOrder.get(a) ?? 3) - (branchOrder.get(b) ?? 3));
        setInspection({ ...result, branches }); setFolderError(null); setBranch(result.currentBranch ?? branches[0] ?? "");
      } catch (failure) { if (!controller.signal.aborted) { setInspection(null); setFolderError(errorMessage(failure)); } }
      finally { if (!controller.signal.aborted) setInspected(folder); }
    }
    void inspect();
    return () => controller.abort();
  }, [folder, projectPath]);
  function close() { if (!busy) { dialog.current?.close(); onClose(); } }
  const analysis = mode === "analysis";
  // A continuation works on the folder and branch of the step it follows.
  const previous = analysis ? undefined : steps.find(step => step.id === previousId);
  function continueFrom(id: string) { setPreviousId(id); const step = steps.find(item => item.id === id); if (step) setFolder(step.folderPath); }
  async function submit(run: boolean) {
    if (!folder || !inspection) { setError(analysis ? "Escolha a pasta do projeto que será analisada." : "Escolha a pasta do projeto onde o código será alterado."); return; }
    const data = new FormData(form.current ?? undefined);
    const criteria = String(data.get("criteria")).split("\n").map(line => line.trim()).filter(Boolean);
    setBusy(true); setError(null);
    try {
      await onCreate({ title: String(data.get("title")), description: String(data.get("description")), acceptanceCriteria: analysis ? [analysisCriterion] : criteria.length ? criteria : [defaultCriterion],
        folderPath: inspection.path, baseBranch: previous ? previous.baseBranch : inspection.hasGit && branch ? branch : null, ai, kind: analysis ? "analysis" : undefined, previousTaskId: previous?.id }, run);
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
    <dialog ref={dialog} className="modal new-task-modal" aria-labelledby="new-task-title" aria-describedby="new-task-description" onCancel={event => { event.preventDefault(); close(); }}>
      <div className="modal-inner new-task-inner">
        <div className="modal-head"><span className="icon-tile" aria-hidden="true"><Icon name="plus" /></span>
          <div><p className="eyebrow">Prepare o próximo passo</p><h2 id="new-task-title">Nova tarefa</h2><p id="new-task-description" className="hint">{analysis ? "Entenda o código com uma análise baseada nos arquivos do projeto." : "Defina a mudança, escolha a pasta e configure o agente."}</p></div>
          <button type="button" className="icon ghost sm" aria-label="Fechar janela" onClick={close} disabled={busy}><Icon name="close" size="sm" /></button></div>
        <form ref={form} aria-label="Nova tarefa" onSubmit={event => { event.preventDefault(); void submit(false); }}><fieldset disabled={busy} className="new-task-fields">
          <div className="task-kind" role="radiogroup" aria-label="Tipo de tarefa">
            <label className="kind-option" data-selected={!analysis}><input type="radio" name="kind" value="change" checked={!analysis} onChange={() => setMode("change")} /><span><strong>Alterar o código</strong><small>Implementação com testes e revisão.</small></span></label>
            <label className="kind-option" data-selected={analysis}><input type="radio" name="kind" value="analysis" checked={analysis} onChange={() => setMode("analysis")} /><span><strong>Analisar o projeto</strong><small>Perguntas sobre o código, somente leitura.</small></span></label>
          </div>
          <section className="new-task-section" aria-labelledby="task-details-heading"><div className="new-task-section-heading"><span aria-hidden="true">01</span><div><h3 id="task-details-heading">{analysis ? "Qual é a sua pergunta?" : "O que vamos fazer?"}</h3><p className="hint">Um objetivo claro ajuda o agente a entregar a mudança certa.</p></div></div>
          <label>{analysis ? "Assunto" : "Título"}<input name="title" required maxLength={160} placeholder={analysis ? "Ex.: Regra de desconto em POST /orders" : "Ex.: Corrigir o cálculo do frete"} autoComplete="off" /></label>
          <label>{analysis ? "Pergunta" : "O que fazer"}<textarea name="description" required maxLength={10000} rows={4} placeholder={analysis ? "Ex.: Qual é a regra de desconto da rota POST /orders e onde ela é aplicada?" : "Qual é o problema ou a mudança desejada?"} /></label>
          {!analysis && <div><label>Resultado esperado (opcional)<textarea name="criteria" value={criteriaText} onChange={event => setCriteriaText(event.target.value)} rows={2} aria-describedby="criteria-help" placeholder={"Ex.: O total tem duas casas decimais\nO cálculo considera o desconto informado"} /></label>
          <p id="criteria-help" className="hint field-hint">Um resultado por linha. Se deixar em branco, a tarefa exige a mudança concluída e os testes aprovados.</p></div>}
          {!analysis && steps.length > 0 && <label>Continuação de (opcional)<select value={previousId} onChange={event => continueFrom(event.target.value)} aria-describedby="sequence-help">
            <option value="">Tarefa independente</option>{steps.map(step => <option key={step.id} value={step.id}>{step.title}</option>)}</select>
            <span id="sequence-help" className="hint field-hint">Parte do resultado da tarefa escolhida e usa a mesma pasta. Ao executar uma etapa, todas rodam em ordem.</span></label>}</section>
          <div className="new-task-settings"><section className="new-task-section new-task-location" aria-labelledby="task-location-heading"><div className="new-task-section-heading"><span aria-hidden="true">02</span><div><h3 id="task-location-heading">{analysis ? "Qual repositório?" : "Onde vamos trabalhar?"}</h3><p className="hint">Escolha a pasta que pertence a esta tarefa.</p></div></div>
          <div className="field"><label htmlFor="task-folder">Pasta do projeto</label>
            <div className="select-row"><input id="task-folder" name="folder" value={folder} readOnly placeholder="Escolha a pasta com o código" aria-describedby="folder-help" />
              <button type="button" disabled={!!previous} onClick={() => setPicker(true)}><Icon name="folder" size="sm" />{folder ? "Trocar pasta" : "Escolher pasta"}</button></div>
            <p id="folder-help" className="hint field-hint">{gitNote ?? "A pasta fica gravada nesta tarefa. Outras tarefas podem usar pastas diferentes."}</p></div>
          {folderError && <Alert>{folderError}</Alert>}
          {inspection?.hasGit && inspection.branches.length > 1 && <label>Branch de partida<select value={branch} onChange={event => setBranch(event.target.value)}>
            {inspection.branches.map(name => <option key={name} value={name}>{name}{name === inspection.currentBranch ? " (atual)" : ""}</option>)}</select></label>}
          <p className="new-task-assurance"><Icon name="check" size="sm" />{analysis ? "Somente leitura: nada na pasta é alterado." : "Você revisa as alterações antes de aplicá-las à pasta."}</p></section>
          <AiSelection projectPath={projectPath} value={ai} onReady={setAgentReady} onChange={(choice, ready) => { setAi(choice); setAgentReady(ready); }} /></div>
          {error && <Alert>{error}</Alert>}
        </fieldset><footer className="new-task-footer"><p className="hint">{analysis ? "Salve a pergunta no quadro ou comece a análise agora." : "Salve no quadro ou comece a execução agora."}</p><div className="modal-actions"><button type="button" className="ghost" disabled={busy} onClick={close}>Cancelar</button>
            <button type="button" className="primary" disabled={busy || !agentReady || inspecting} title={agentReady ? undefined : "Instale, autentique e habilite a IA escolhida"} onClick={() => { if (form.current?.reportValidity()) void submit(true); }}><Icon name="play" size="sm" />{analysis ? "Criar e analisar" : previous ? "Criar e executar sequência" : "Criar e executar"}</button>
            <button type="submit" disabled={busy || inspecting}>{busy ? "Criando…" : analysis ? "Criar análise" : "Criar tarefa"}</button></div></footer></form>
      </div>
    </dialog>
    {picker && <DirectoryPicker path={projectPath} onClose={() => setPicker(false)} onSelect={directory => { setPicker(false); setFolder(directory); }} />}
  </>;
}
