"use client";

import { useEffect, useState, type ReactNode } from "react";
import { AuthForm } from "./AuthForm";
import { TaskBoard } from "./TaskBoard";
import { CreateWorkspaceItem } from "./CreateWorkspaceItem";
import { api, ApiError, errorMessage, listAll, type Schema } from "./api";

export function Workspace() {
  const [session, setSession] = useState<Schema["SessionDto"] | null>();
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    api<Schema["SessionDto"]>("/auth/session", { signal: controller.signal }).then(setSession).catch(failure => {
      if (controller.signal.aborted) return;
      setSession(null);
      if (!(failure instanceof ApiError && failure.status === 401)) setError(errorMessage(failure));
    });
    return () => controller.abort();
  }, []);
  if (session === undefined) return <p role="status">Carregando sessão…</p>;
  return <>{error && <p role="alert" className="error">{error}</p>}
    {session ? <TeamWorkspace key={session.userId} session={session} onLogout={() => setSession(null)} /> : <AuthForm onAuthenticated={setSession} />}
  </>;
}

function TeamWorkspace({ session, onLogout }: { session: Schema["SessionDto"]; onLogout: () => void }) {
  const [organizations, setOrganizations] = useState<Schema["OrganizationDto"][]>([]);
  const [organizationId, setOrganizationId] = useState("");
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    listAll<Schema["OrganizationDto"]>("/organizations/", controller.signal).then(items => {
      if (!controller.signal.aborted) { setOrganizations(items); setOrganizationId(items[0]?.id ?? ""); }
    }).catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, []);
  const organization = organizations.find(item => item.id === organizationId);
  async function createOrganization(name: string) {
    const created = await api<Schema["OrganizationDto"]>("/organizations/", { method: "POST", body: JSON.stringify({ name } satisfies Schema["OrganizationInput"]) });
    setOrganizations(items => [...items, created]); setOrganizationId(created.id);
  }
  async function logout() {
    setBusy(true); setError(null);
    try { await api<void>("/auth/logout", { method: "POST" }); onLogout(); }
    catch (failure) { setError(errorMessage(failure)); setBusy(false); }
  }
  const teamControl = <div className="context-choice"><span className="context-symbol" aria-hidden="true">{organization?.name.slice(0, 1).toLocaleUpperCase("pt-BR") ?? "E"}</span>
    <label>Equipe<select value={organizationId} onChange={event => setOrganizationId(event.target.value)} disabled={busy}>
      {!organizations.length && <option value="">Escolha sua equipe</option>}
      {organizations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
    </select></label><button className="add-context" title="Adicionar equipe" aria-label="Adicionar equipe" onClick={() => setCreating(true)} disabled={busy}>+</button>
  </div>;
  const accountControl = <div className="context-account"><span title={session.email}>{session.email}</span><button onClick={logout} disabled={busy}>Sair</button></div>;
  return <>
    {organization ? <OrganizationWorkspace key={organization.id} organization={organization} teamControl={teamControl} accountControl={accountControl} /> : <>
      <div className="workspace-context" aria-label="Equipe e projeto">{teamControl}{accountControl}</div>
      {!busy && !error && <section className="context-empty"><p className="eyebrow">Primeiro passo</p><h1>Crie o espaço da sua equipe</h1><p>Uma equipe reúne os projetos, as pessoas e o conhecimento compartilhado.</p><button className="primary" onClick={() => setCreating(true)}>Criar minha primeira equipe</button></section>}
    </>}
    {error && <p role="alert" className="error">{error}</p>}{busy && <p role="status">Carregando equipes…</p>}
    {creating && <CreateWorkspaceItem kind="team" onCreate={createOrganization} onClose={() => setCreating(false)} />}
  </>;
}

function OrganizationWorkspace({ organization, teamControl, accountControl }: {
  organization: Schema["OrganizationDto"]; teamControl: ReactNode; accountControl: ReactNode;
}) {
  const [projects, setProjects] = useState<Schema["ProjectDto"][]>([]);
  const [members, setMembers] = useState<Schema["MemberDto"][]>([]);
  const [projectId, setProjectId] = useState("");
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const base = `/organizations/${organization.id}`;
  useEffect(() => {
    const controller = new AbortController();
    Promise.all([listAll<Schema["ProjectDto"]>(`${base}/projects/`, controller.signal), listAll<Schema["MemberDto"]>(`${base}/members`, controller.signal)])
      .then(([items, accounts]) => { if (!controller.signal.aborted) { setProjects(items); setMembers(accounts); setProjectId(items[0]?.id ?? ""); } })
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [base]);
  const project = projects.find(item => item.id === projectId);
  async function createProject(name: string) {
    const created = await api<Schema["ProjectDto"]>(`${base}/projects/`, { method: "POST", body: JSON.stringify({ name } satisfies Schema["ProjectInput"]) });
    setProjects(items => [...items, created]); setProjectId(created.id);
  }
  return <>
    <div className="workspace-context" aria-label="Equipe e projeto">{teamControl}<span className="context-divider" aria-hidden="true">/</span>
      <div className="context-choice"><span className="project-symbol" aria-hidden="true">▱</span><label>Projeto<select value={projectId} onChange={event => setProjectId(event.target.value)} disabled={busy}>
        {!projects.length && <option value="">{busy ? "Carregando projetos…" : "Escolha um projeto"}</option>}
        {projects.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
      </select></label>{organization.role === "owner" && <button className="add-context" title="Adicionar projeto" aria-label="Adicionar projeto" onClick={() => setCreating(true)} disabled={busy}>+</button>}</div>
      {accountControl}
    </div>
    {error && <p role="alert" className="error">{error}</p>}{busy && <p role="status">Carregando projetos…</p>}
    {project ? <TaskBoard key={project.id} project={project} members={members} canManageIntegrations={organization.role === "owner"} /> : !busy && !error && <section className="context-empty"><p className="eyebrow">Equipe {organization.name}</p><h1>{organization.role === "owner" ? "Dê um lugar ao seu próximo projeto" : "Nenhum projeto nesta equipe"}</h1><p>Cada projeto tem seu próprio quadro, repositório e memória.</p>{organization.role === "owner" && <button className="primary" onClick={() => setCreating(true)}>Criar primeiro projeto</button>}</section>}
    {creating && <CreateWorkspaceItem kind="project" teamName={organization.name} onCreate={createProject} onClose={() => setCreating(false)} />}
  </>;
}
