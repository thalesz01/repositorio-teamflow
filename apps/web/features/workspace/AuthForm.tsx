"use client";

import { useState, type FormEvent } from "react";
import { api, errorMessage, type Schema } from "./api";

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

  return <div className="auth-layout"><aside className="auth-story" aria-label="Sobre o TeamFlow">
    <div><p className="eyebrow">Seu próximo fluxo de trabalho</p><h2>Menos contexto perdido.<br />Mais trabalho entregue.</h2><p>Conecte seu código, compartilhe o conhecimento da equipe e acompanhe cada tarefa até a revisão.</p></div>
    <div className="auth-steps"><div><span>01</span><strong>Organize as demandas</strong></div><div><span>02</span><strong>Execute com contexto compartilhado</strong></div><div><span>03</span><strong>Revise o código e os testes</strong></div></div>
    <p className="hint">A equipe mantém o controle de cada aprovação.</p>
  </aside><section className="auth-panel">
    <p className="eyebrow">Bem-vindo ao TeamFlow</p>
    <h1>{register ? "Crie sua conta" : "Entre no workspace"}</h1>
    <p className="hint">{register ? "Comece a organizar o trabalho da sua equipe." : "Acesse os projetos e continue de onde parou."}</p>
    <form onSubmit={submit}>
      <label>E-mail<input name="email" type="email" autoComplete="username" maxLength={254} required disabled={busy} /></label>
      <label>Senha<input name="password" type="password" autoComplete={register ? "new-password" : "current-password"}
        minLength={register ? 12 : undefined} maxLength={128} required disabled={busy} /></label>
      {register && <p className="hint">Use pelo menos 12 caracteres, com maiúscula, minúscula, número e símbolo.</p>}
      {error && <p role="alert" className="error">{error}</p>}
      <button className="primary" disabled={busy}>{busy ? "Aguarde…" : register ? "Cadastrar" : "Entrar"}</button>
    </form>
    <button className="text-button" disabled={busy} onClick={() => { setRegister(!register); setError(null); }}>
      {register ? "Já tenho uma conta" : "Criar uma conta"}
    </button>
  </section></div>;
}
