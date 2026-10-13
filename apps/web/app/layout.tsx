import type { Metadata } from "next";
import Link from "next/link";
import "./globals.css";
import "./workspace.css";

export const metadata: Metadata = { title: "TeamFlow", description: "Trabalho em equipe com agentes de IA" };

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="pt-BR"><body><a className="skip-link" href="#workspace">Ir para o conteúdo</a><header className="app-header"><Link className="brand" href="/"><span className="brand-mark" aria-hidden="true">↗</span>TeamFlow</Link><span className="header-caption">Código, contexto e equipe. No mesmo fluxo.</span></header><main id="workspace">{children}</main></body></html>;
}
