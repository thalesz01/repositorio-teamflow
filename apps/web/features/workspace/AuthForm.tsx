"use client";

import { useState, type FormEvent } from "react";
import { api, errorMessage, type Schema } from "./api";
import { Alert, BrandMark, Icon } from "./ui";

export function AuthForm({ onAuthenticated }: { onAuthenticated: (session: Schema["SessionDto"]) => void }) {
  const [register, setRegister] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setBusy(true);
    setError(null);
    try {
      const input: Schema["Credentials"] = { email: String(data.get("email")), password: String(data.get("password")) };
      onAuthenticated(await api<Schema["SessionDto"]>(register ? "/auth/register" : "/auth/login", { method: "POST", body: JSON.stringify(input) }));
    } catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }

  return <div className="auth">
    <aside className="auth-story" aria-label="Sobre o TeamFlow">
      <div className="brand"><BrandMark size={34} />TeamFlow</div>
      <div>
        <h2>Menos contexto perdido. Mais trabalho entregue.</h2>
        <p className="lede">Organize as demandas, execute com o contexto da equipe e revise cada alteração antes de ela chegar ao código.</p>
      </div>
      <ul className="auth-points">
        <li><Icon name="board" /><div><strong>Do pedido à tarefa pronta</strong><span>Analise demandas e transforme-as em tarefas com critérios claros.</span></div></li>
        <li><Icon name="git" /><div><strong>Execução rastreável</strong><span>Cada execução registra alterações, testes e pendências.</span></div></li>
        <li><Icon name="check" /><div><strong>Você aprova</strong><span>Uma tarefa só termina depois de revisão independente e merge confirmado.</span></div></li>
      </ul>
    </aside>
    <main className="auth-panel" id="workspace">
      <div className="auth-card">
        <div className="mobile-brand"><BrandMark size={30} /><span>TeamFlow</span></div>
        <header>
          <h1>{register ? "Crie sua conta" : "Entre no workspace"}</h1>
          <p className="hint">{register ? "Comece a organizar o trabalho da sua equipe." : "Acesse os projetos e continue de onde parou."}</p>
        </header>
        <form onSubmit={submit}>
          <label>E-mail<input name="email" type="email" autoComplete="username" maxLength={254} required disabled={busy} placeholder="voce@empresa.com" /></label>
          <label>Senha<input name="password" type="password" autoComplete={register ? "new-password" : "current-password"}
            minLength={register ? 12 : undefined} maxLength={128} required disabled={busy} /></label>
          {register && <p className="hint field-hint">Use pelo menos 12 caracteres, com maiúscula, minúscula, número e símbolo.</p>}
          {error && <Alert>{error}</Alert>}
          <button className="primary" disabled={busy}>{busy ? <><span className="spinner" aria-hidden="true" />Aguarde…</> : register ? "Cadastrar" : "Entrar"}</button>
        </form>
        <p className="auth-switch">{register ? "Já tem uma conta? " : "Ainda não tem conta? "}
          <button className="link" disabled={busy} onClick={() => { setRegister(!register); setError(null); }}>{register ? "Já tenho uma conta" : "Criar uma conta"}</button></p>
      </div>
    </main>
  </div>;
}
