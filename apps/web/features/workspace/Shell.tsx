"use client";

import Link from "next/link";
import { useEffect, useState, type ReactNode } from "react";
import { BrandMark, Icon } from "./ui";

/** Sidebar + content frame. Below 1024px the sidebar becomes an off-canvas menu opened from a top bar. */
export function Shell({ sidebar, children }: { sidebar: (closeMenu: () => void) => ReactNode; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [narrow, setNarrow] = useState(false);
  useEffect(() => {
    const query = window.matchMedia("(max-width: 1023px)");
    const update = () => { setNarrow(query.matches); if (!query.matches) setOpen(false); };
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);
  useEffect(() => {
    if (!open) return;
    // A dialog opened from the menu handles Escape itself; closing the menu too would drop focus.
    const onKey = (event: KeyboardEvent) => { if (event.key === "Escape" && !document.querySelector("dialog[open]")) setOpen(false); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);
  const closeMenu = () => setOpen(false);
  return <div className="shell" data-nav-open={open}>
    <aside className="sidebar" id="app-sidebar" aria-label="Navegação principal" inert={narrow && !open}>
      <div className="sidebar-top"><Link className="sidebar-brand" href="/"><BrandMark size={30} /><span>TeamFlow</span></Link>
        <button className="icon ghost sidebar-close" aria-label="Fechar menu" onClick={closeMenu}><Icon name="close" /></button></div>
      {sidebar(closeMenu)}
    </aside>
    <div className="content-col" style={{ minWidth: 0 }}>
      <div className="mobile-bar">
        <Link className="sidebar-brand" href="/"><BrandMark size={28} /><span>TeamFlow</span></Link>
        <button className="icon ghost" aria-label="Abrir menu" aria-expanded={open} aria-controls="app-sidebar" onClick={() => setOpen(true)}><Icon name="menu" /></button>
      </div>
      <main id="workspace" className="main"><div className="main-inner">{children}</div></main>
    </div>
    <div className="scrim" onClick={closeMenu} aria-hidden="true" />
  </div>;
}
