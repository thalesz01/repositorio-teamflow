"use client";

import { useEffect, useState } from "react";
import { AuthForm } from "./AuthForm";
import { TaskBoard, type View } from "./TaskBoard";
import { Shell } from "./Shell";
import { Alert, Avatar, Icon, type IconName } from "./ui";
import { api, ApiError, errorMessage, type Schema } from "./api";

const navigation: { id: View; label: string; icon: IconName }[] = [
  { id: "board", label: "Quadro", icon: "board" },
  { id: "memory", label: "Memória", icon: "memory" },
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
    {session ? <PersonalWorkspace key={session.userId} session={session} onLogout={() => setSession(null)} /> : <AuthForm onAuthenticated={setSession} />}
  </>;
}

// Teams and projects still isolate the data on the server; the person only sees their own space (ADR 0010).
function PersonalWorkspace({ session, onLogout }: { session: Schema["SessionDto"]; onLogout: () => void }) {
  const [project, setProject] = useState<Schema["ProjectDto"] | null>(null);
  const [view, setView] = useState<View>("board");
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const workspace = await api<Schema["WorkspaceDto"]>("/workspace", { method: "POST", signal: controller.signal });
        const found = await api<Schema["ProjectDto"]>(`/organizations/${workspace.organizationId}/projects/${workspace.projectId}`, { signal: controller.signal });
        if (!controller.signal.aborted) setProject(found);
      } catch (failure) { if (!controller.signal.aborted) setError(errorMessage(failure)); }
      finally { if (!controller.signal.aborted) setBusy(false); }
    }
    void load();
    return () => controller.abort();
  }, []);
  async function logout() {
    setBusy(true); setError(null);
    try { await api<void>("/auth/logout", { method: "POST" }); onLogout(); }
    catch (failure) { setError(errorMessage(failure)); setBusy(false); }
  }
  return <Shell sidebar={closeMenu => <>
    <nav className="sidebar-nav" aria-label="Navegação principal">
      {navigation.map(item => <button key={item.id} className="nav-item" aria-current={view === item.id ? "page" : undefined} onClick={() => { setView(item.id); closeMenu(); }}>
        <Icon name={item.icon} />{item.label}</button>)}
    </nav>
    <div className="sidebar-foot"><Avatar name={session.email} size={32} /><div className="who"><strong title={session.email}>{session.email}</strong><small>Seu espaço de trabalho</small></div>
      <button className="icon ghost sm" onClick={logout} disabled={busy} aria-label="Sair" title="Sair"><Icon name="logout" size="sm" /></button></div>
  </>}>
    {error && <Alert>{error}</Alert>}
    {busy && !error && <p role="status" className="loading-line"><span className="spinner" aria-hidden="true" />Preparando seu espaço…</p>}
    {project && <TaskBoard key={project.id} project={project} view={view} />}
  </Shell>;
}
