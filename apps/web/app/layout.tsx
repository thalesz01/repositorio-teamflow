import type { Metadata, Viewport } from "next";
import "./globals.css";
import "./app.css";

export const metadata: Metadata = { title: "TeamFlow", description: "Trabalho em equipe com agentes de IA" };
export const viewport: Viewport = { width: "device-width", initialScale: 1, themeColor: [{ media: "(prefers-color-scheme: light)", color: "#f6f7f9" }, { media: "(prefers-color-scheme: dark)", color: "#0c0e19" }] };

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="pt-BR"><body><a className="skip-link" href="#workspace">Ir para o conteúdo</a>{children}</body></html>;
}
