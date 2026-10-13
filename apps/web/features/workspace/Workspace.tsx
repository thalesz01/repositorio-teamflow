"use client";

import { useEffect, useState, type ReactNode } from "react";
import { AuthForm } from "./AuthForm";
import { TaskBoard, type View } from "./TaskBoard";
import { CreateWorkspaceItem } from "./CreateWorkspaceItem";
import { Shell } from "./Shell";
import { Alert, Avatar, EmptyState, Icon, type IconName } from "./ui";
import { api, ApiError, errorMessage, listAll, type Schema } from "./api";

const navigation: { id: View; label: string; icon: IconName }[] = [
  { id: "board", label: "Quadro", icon: "board" },
  { id: "planning", label: "Demandas", icon: "demand" },
  { id: "memory", label: "Memória", icon: "memory" },
  { id: "settings", label: "Configurações", icon: "settings" },
];

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
  if (session === undefined) return <main id="workspace" className="stage"><p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Carregando sessão…</p></main>;
  return <>{error && <div className="stage-error"><Alert>{error}</Alert></div>}
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
  const teamControl = <div className="field"><label htmlFor="team-select">Equipe</label><div className="select-row"><select id="team-select" value={organizationId} onChange={event => setOrganizationId(event.target.value)} disabled={busy}>
    {!organizations.length && <option value="">Escolha sua equipe</option>}
    {organizations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
  </select><button className="icon ghost sm" title="Adicionar equipe" aria-label="Adicionar equipe" onClick={() => setCreating(true)} disabled={busy}><Icon name="plus" size="sm" /></button></div></div>;
  const accountControl = <div className="sidebar-foot"><Avatar name={session.email} size={32} /><div className="who"><strong title={session.email}>{session.email}</strong><small>{organization?.role === "owner" ? "Proprietária(o) da equipe" : "Membro"}</small></div>
    <button className="icon ghost sm" onClick={logout} disabled={busy} aria-label="Sair" title="Sair"><Icon name="logout" size="sm" /></button></div>;
  return <>
    {organization ? <OrganizationWorkspace key={organization.id} organization={organization} teamControl={teamControl} accountControl={accountControl} />
      : <Shell sidebar={() => <><div className="switcher" aria-label="Equipe e projeto">{teamControl}</div>{accountControl}</>}>
        {error && <Alert>{error}</Alert>}
        {busy && !error && <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Carregando equipes…</p>}
        {!busy && !error && <div className="card"><EmptyState icon="users" title="Crie o espaço da sua equipe"
          action={<button className="primary" onClick={() => setCreating(true)}><Icon name="plus" size="sm" />Criar minha primeira equipe</button>}>
          Uma equipe reúne os projetos, as pessoas e o conhecimento compartilhado.</EmptyState></div>}
      </Shell>}
    {creating && <CreateWorkspaceItem kind="team" onCreate={createOrganization} onClose={() => setCreating(false)} />}
  </>;
}

function OrganizationWorkspace({ organization, teamControl, accountControl }: {
  organization: Schema["OrganizationDto"]; teamControl: ReactNode; accountControl: ReactNode;
}) {
  const [projects, setProjects] = useState<Schema["ProjectDto"][]>([]);
  const [members, setMembers] = useState<Schema["MemberDto"][]>([]);
  const [projectId, setProjectId] = useState("");
  const [view, setView] = useState<View>("board");
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
  const owner = organization.role === "owner";
  async function createProject(name: string) {
    const created = await api<Schema["ProjectDto"]>(`${base}/projects/`, { method: "POST", body: JSON.stringify({ name } satisfies Schema["ProjectInput"]) });
    setProjects(items => [...items, created]); setProjectId(created.id);
  }
  return <>
    <Shell sidebar={closeMenu => <>
      <div className="switcher" aria-label="Equipe e projeto">{teamControl}
        <div className="field"><label htmlFor="project-select">Projeto</label><div className="select-row"><select id="project-select" value={projectId} onChange={event => setProjectId(event.target.value)} disabled={busy}>
          {!projects.length && <option value="">{busy ? "Carregando projetos…" : "Escolha um projeto"}</option>}
          {projects.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select>{owner && <button className="icon ghost sm" title="Adicionar projeto" aria-label="Adicionar projeto" onClick={() => setCreating(true)} disabled={busy}><Icon name="plus" size="sm" /></button>}</div></div>
      </div>
      {project && <nav className="sidebar-nav" aria-label="Navegação do projeto"><p className="nav-label">{project.name}</p>
        {navigation.map(item => <button key={item.id} className="nav-item" aria-current={view === item.id ? "page" : undefined} onClick={() => { setView(item.id); closeMenu(); }}>
          <Icon name={item.icon} />{item.label}</button>)}
      </nav>}
      {accountControl}
    </>}>
      {error && <Alert>{error}</Alert>}
      {busy && !error && <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Carregando projetos…</p>}
      {project ? <TaskBoard key={project.id} project={project} members={members} canManageIntegrations={owner} view={view} onViewChange={setView} />
        : !busy && !error && <div className="card"><EmptyState icon="folder" title={owner ? "Dê um lugar ao seu próximo projeto" : "Nenhum projeto nesta equipe"}
          action={owner ? <button className="primary" onClick={() => setCreating(true)}><Icon name="plus" size="sm" />Criar primeiro projeto</button> : undefined}>
          Cada projeto da equipe {organization.name} tem seu próprio quadro, repositório e memória.</EmptyState></div>}
    </Shell>
    {creating && <CreateWorkspaceItem kind="project" teamName={organization.name} onCreate={createProject} onClose={() => setCreating(false)} />}
  </>;
}
