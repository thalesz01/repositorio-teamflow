"use client";
import { useEffect, useState, type FormEvent } from "react";
import { DirectoryPicker } from "./DirectoryPicker";
import { Alert, Icon } from "./ui";
import { api, ApiError, errorMessage, listAll, type Schema } from "./api";

export function IntegrationPanel({ path, canManage, onSelect }: { path: string; canManage: boolean; selectedProfile: string; onSelect: (id: string) => void }) {
  const [repository, setRepository] = useState<Schema["GitDto"] | null>(null);
  const [folder, setFolder] = useState<Schema["LocalFolderInspection"] | null>(null);
  const [machine, setMachine] = useState<Schema["CodexMachineStatus"] | null>(null);
  const [directory, setDirectory] = useState("");
  const [branch, setBranch] = useState("");
  const [picker, setPicker] = useState(false);
  const [busy, setBusy] = useState(true);
  const [machineBusy, setMachineBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [machineError, setMachineError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    api<Schema["GitDto"]>(`${path}/repository/`, { signal: controller.signal }).then(async connection => {
      if (controller.signal.aborted) return;
      setRepository(connection); setDirectory(connection.remoteUrl); setBranch(connection.baseBranch);
      if (canManage && connection.provider === "local") {
        const result = await api<Schema["LocalFolderInspection"]>(`${path}/repository/local/inspect`, { method: "POST", signal: controller.signal, body: JSON.stringify({ path: connection.remoteUrl }) });
        if (!controller.signal.aborted) setFolder(result);
      }
    }).catch(failure => { if (!controller.signal.aborted && !(failure instanceof ApiError && failure.status === 404)) setError(errorMessage(failure)); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [path, canManage]);
  useEffect(() => {
    const controller = new AbortController();
    void check(controller.signal);
    return () => controller.abort();
    async function check(signal: AbortSignal) {
      try {
        const status = await api<Schema["CodexMachineStatus"]>(`${path}/profiles/machine`, { signal });
        if (signal.aborted) return;
        setMachine(status);
        if (status.installed && status.authenticated && status.enabled && canManage) {
          const binding = await api<Schema["ProfileDto"]>(`${path}/profiles/machine`, { method: "POST", signal });
          if (!signal.aborted) onSelect(binding.id);
        } else if (!canManage) {
          const profiles = await listAll<Schema["ProfileDto"]>(`${path}/profiles/`, signal);
          if (!signal.aborted) onSelect(profiles.find(profile => profile.name === "Codex da máquina" && !profile.revokedAt)?.id ?? "");
        }
      } catch (failure) { if (!signal.aborted) setMachineError(errorMessage(failure)); }
      finally { if (!signal.aborted) setMachineBusy(false); }
    }
  }, [path, canManage, onSelect]);
  async function inspect(next: string) {
    setDirectory(next); setFolder(null); setBranch(""); setSaved(false); setBusy(true); setError(null);
    try {
      const result = await api<Schema["LocalFolderInspection"]>(`${path}/repository/local/inspect`, { method: "POST", body: JSON.stringify({ path: next }) });
      const current = await api<Schema["GitDto"]>(`${path}/repository/`).catch(failure => {
        if (failure instanceof ApiError && failure.status === 404) return null; throw failure;
      });
      setRepository(current); setFolder(result); setBranch(result.currentBranch && result.branches.includes(result.currentBranch) ? result.currentBranch : "");
    } catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!folder) return;
    setBusy(true); setError(null); setSaved(false);
    try {
      setRepository(await api<Schema["GitDto"]>(`${path}/repository/local/folder`, { method: "POST", body: JSON.stringify({ path: folder.path, baseBranch: branch || null, version: repository?.version ?? null }) })); setSaved(true);
    } catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  async function checkMachine() {
    setMachineBusy(true); setMachineError(null);
    try {
      const status = await api<Schema["CodexMachineStatus"]>(`${path}/profiles/machine`); setMachine(status);
      if (canManage && status.installed && status.authenticated && status.enabled) onSelect((await api<Schema["ProfileDto"]>(`${path}/profiles/machine`, { method: "POST" })).id);
      else onSelect("");
    } catch (failure) { setMachineError(errorMessage(failure)); }
    finally { setMachineBusy(false); }
  }
  const ready = machine?.installed && machine.authenticated && machine.enabled;
  const fact = (ok: boolean | undefined, yes: string, no: string) => <span className={`badge ${ok ? "success" : "warning"}`}>{ok ? yes : no}</span>;
  return <section className="settings-sections" aria-label="Integrações do projeto">
    <section className="card" aria-label="Pasta do projeto"><div className="card-header"><span className="step" aria-hidden="true"><Icon name="folder" /></span><div><h2>Pasta do projeto</h2><p>Escolha onde o código fica no computador. A pasta pode estar vazia.</p></div><span className={`badge ${repository ? "success" : ""}`}>{repository ? "Configurada" : "Não configurada"}</span></div>
      {canManage ? <form onSubmit={save}><div className="card-body stack">
        <label>Caminho do repositório<span className="path-field"><input value={directory} onChange={event => { setDirectory(event.target.value); setFolder(null); setSaved(false); }} maxLength={1024} placeholder="Escolha a pasta do projeto" required disabled={busy} /></span></label>
        <div className="actions"><button type="button" disabled={busy} onClick={() => setPicker(true)}><Icon name="folder" size="sm" />Explorar pastas do computador</button><button type="button" disabled={busy || !directory.trim()} onClick={() => void inspect(directory.trim())}>Verificar pasta</button>
          {busy && <span role="status" className="hint loading-line" style={{ padding: 0 }}><span className="spinner" aria-hidden="true" />Verificando configuração…</span>}</div>
        {error && <Alert>{error}</Alert>}
        {folder && <div className="folder-result"><h3><Icon name="git" size="sm" />{folder.hasGit ? "Git detectado" : "Git ainda não iniciado"}</h3><p className="hint">{!folder.hasGit ? "Você pode salvar e executar tarefas nesta pasta. O Git será preparado automaticamente ao iniciar a primeira execução." : !folder.branches.length ? "O Git já existe. A base inicial será preparada automaticamente ao iniciar uma tarefa." : "Selecione uma branch local como base para as próximas execuções."}</p>
          {!!folder.branches.length && <label>Branch base<select value={branch} onChange={event => { setBranch(event.target.value); setSaved(false); }} disabled={busy}><option value="">Configurar somente a pasta por enquanto</option>{folder.branches.map(name => <option key={name} value={name}>{name}</option>)}</select></label>}
        </div>}
        {saved && <p role="status" className="settings-success">Configuração da pasta salva.</p>}
      </div>
        <div className="card-footer"><p className="hint">{folder && (!folder.hasGit || !branch) ? "A base necessária será preparada automaticamente ao iniciar uma tarefa." : "Salvar a configuração não altera os arquivos da pasta."}</p><button className="primary" disabled={busy || !folder}>Salvar configuração</button></div>
      </form> : <div className="card-body"><p className="hint">{repository ? repository.remoteUrl : "Um proprietário pode configurar a pasta deste projeto."}</p></div>}
    </section>
    <section className="card" aria-label="Codex da máquina"><div className="card-header"><span className="step" aria-hidden="true"><Icon name="terminal" /></span><div><h2>Codex da máquina</h2><p>A instalação e o login do computador são usados pelos projetos. Nenhum perfil precisa ser criado.</p></div>
      {!machineBusy && machine && <span className={`badge ${ready ? "success" : "warning"}`}>{ready ? "Pronto" : "Requer atenção"}</span>}</div>
      <div className="card-body stack">
        {machineBusy ? <p role="status" className="loading-line" style={{ padding: 0 }}><span className="spinner" aria-hidden="true" />Verificando o Codex instalado…</p> : machine && <dl className="status-grid" style={{ margin: 0 }}>
          <div className="status-tile"><dt>Instalação</dt><dd>{fact(machine.installed, "Codex encontrado", "Codex não encontrado")}</dd></div>
          <div className="status-tile"><dt>Autenticação</dt><dd>{fact(machine.authenticated, "Login ativo na máquina", "Login necessário")}</dd></div>
          <div className="status-tile"><dt>Execução</dt><dd>{fact(machine.enabled, "Habilitada na máquina", "Desabilitada na máquina")}</dd></div></dl>}
        {machineError && <Alert>{machineError}</Alert>}
        {machine && !machine.authenticated && <p className="hint">No computador que executa o TeamFlow, abra o terminal e execute <code>codex login</code>. Depois, verifique novamente.</p>}
      </div>
      <div className="card-footer"><p className="hint">Modelo e limites são definidos na máquina. Credenciais não são copiadas para o projeto.</p><button type="button" disabled={machineBusy} onClick={() => void checkMachine()}><Icon name="refresh" size="sm" />Verificar Codex</button></div>
    </section>
    {picker && <DirectoryPicker path={path} onClose={() => setPicker(false)} onSelect={next => { setPicker(false); void inspect(next); }} />}
  </section>;
}
