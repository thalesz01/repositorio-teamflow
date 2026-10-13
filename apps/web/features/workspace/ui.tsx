import type { ReactNode } from "react";

// Stroke icons (24px grid). Decorative by default; pass `label` when an icon is the only content.
const paths = {
  board: <><rect x="3" y="3" width="18" height="18" rx="3" /><path d="M8 7.5v8M12 7.5v4M16 7.5v6" /></>,
  demand: <><path d="M9 18h6M10 21.5h4" /><path d="M15 14.5c.3-1 .9-1.8 1.6-2.5A5.5 5.5 0 1 0 7.4 12c.7.7 1.3 1.5 1.6 2.5" /></>,
  memory: <><path d="M3 4.5h6a3 3 0 0 1 3 3V21a2.5 2.5 0 0 0-2.5-2.5H3z" /><path d="M21 4.5h-6a3 3 0 0 0-3 3V21a2.5 2.5 0 0 1 2.5-2.5H21z" /></>,
  settings: <><path d="M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1.5 14h5M9.5 8h5M17.5 16h5" /></>,
  plus: <path d="M12 5v14M5 12h14" />,
  search: <><circle cx="11" cy="11" r="7" /><path d="m20.5 20.5-4.2-4.2" /></>,
  close: <path d="M18 6 6 18M6 6l12 12" />,
  check: <path d="m5 12.5 4.5 4.5L19 7.5" />,
  chevronDown: <path d="m6 9 6 6 6-6" />,
  chevronRight: <path d="m9 6 6 6-6 6" />,
  arrowLeft: <path d="M19 12H5m6-6-6 6 6 6" />,
  logout: <><path d="M9 21H5.5A2.5 2.5 0 0 1 3 18.5v-13A2.5 2.5 0 0 1 5.5 3H9" /><path d="m16 17 5-5-5-5M21 12H9" /></>,
  menu: <path d="M4 6.5h16M4 12h16M4 17.5h16" />,
  play: <path d="M7 4.5v15l12-7.5z" />,
  eye: <><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></>,
  refresh: <><path d="M20.5 12a8.5 8.5 0 0 1-14.6 5.9L3.5 15.5M3.5 12A8.5 8.5 0 0 1 18.1 6.1l2.4 2.4" /><path d="M20.5 3.5v5h-5M3.5 20.5v-5h5" /></>,
  folder: <path d="M3 6.5A2.5 2.5 0 0 1 5.5 4h3.9a2 2 0 0 1 1.6.8l.9 1.2a2 2 0 0 0 1.6.8H18.5A2.5 2.5 0 0 1 21 9.3v8.2a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 17.5z" />,
  terminal: <><path d="m5 7.5 5 4.5-5 4.5" /><path d="M13 17h6" /></>,
  git: <><circle cx="6" cy="5.5" r="2.2" /><circle cx="6" cy="18.5" r="2.2" /><circle cx="18" cy="9" r="2.2" /><path d="M6 7.7v8.6M18 11.2c0 3.5-3.5 3.5-8 5" /></>,
  list: <path d="M9 6.5h11M9 12h11M9 17.5h11M4 6.5h.01M4 12h.01M4 17.5h.01" />,
  link: <><path d="M10 13.5a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1 1" /><path d="M14 10.5a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1-1" /></>,
  alert: <><path d="M10.3 3.9 2.4 17.5A2 2 0 0 0 4.1 20.5h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" /><path d="M12 9.5v4M12 17h.01" /></>,
  info: <><circle cx="12" cy="12" r="9" /><path d="M12 11v5M12 8h.01" /></>,
  sparkle: <><path d="M12 3.5 14 9l5.5 2L14 13l-2 5.5L10 13l-5.5-2L10 9z" /><path d="M19 3v3M20.5 4.5h-3" /></>,
  clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
  user: <><circle cx="12" cy="8" r="4" /><path d="M4.5 20.5a7.5 7.5 0 0 1 15 0" /></>,
  users: <><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20a6.5 6.5 0 0 1 13 0M16 4.7a3.5 3.5 0 0 1 0 6.6M18 14.4a6.5 6.5 0 0 1 3.5 5.6" /></>,
  merge: <><circle cx="6" cy="5.5" r="2.2" /><circle cx="6" cy="18.5" r="2.2" /><circle cx="18" cy="14" r="2.2" /><path d="M6 7.7v8.6M6 7.7c0 4 12 1 12 4.1" /></>,
} as const;

export type IconName = keyof typeof paths;

export function Icon({ name, size, label }: { name: IconName; size?: "sm" | "lg"; label?: string }) {
  return <svg className={`icon-svg${size ? ` ${size}` : ""}`} viewBox="0 0 24 24" role={label ? "img" : undefined} aria-label={label} aria-hidden={label ? undefined : true} focusable="false">{paths[name]}</svg>;
}

/** Two stacked flow lines forming a "T": the TeamFlow mark. */
export function BrandMark({ size = 32 }: { size?: number }) {
  return <svg className="brand-mark" width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
    <rect width="32" height="32" rx="9" fill="currentColor" />
    <path d="M9 11.5h14M16 11.5V23" stroke="var(--brand-contrast)" strokeWidth="2.6" strokeLinecap="round" fill="none" />
    <circle cx="23" cy="11.5" r="2.1" fill="var(--brand-contrast)" />
  </svg>;
}

export function Avatar({ name, size = 28 }: { name: string; size?: number }) {
  const initials = name.split(/[\s@._-]+/).filter(Boolean).slice(0, 2).map(part => part[0]).join("").toLocaleUpperCase("pt-BR") || "?";
  let hash = 0;
  for (const char of name) hash = (hash * 31 + char.charCodeAt(0)) % 360;
  return <span className="avatar" style={{ width: size, height: size, fontSize: Math.round(size * .4), ["--hue" as string]: hash }} title={name} aria-hidden="true">{initials}</span>;
}

export function EmptyState({ icon, title, children, action }: { icon: IconName; title: string; children?: ReactNode; action?: ReactNode }) {
  return <div className="empty"><span className="empty-icon"><Icon name={icon} size="lg" /></span><h3>{title}</h3>{children && <p>{children}</p>}{action}</div>;
}

export function PageHeader({ title, description, actions, eyebrow }: { title: string; description?: ReactNode; actions?: ReactNode; eyebrow?: ReactNode }) {
  return <header className="page-header"><div>{eyebrow && <p className="eyebrow">{eyebrow}</p>}<h1>{title}</h1>{description && <p className="page-description">{description}</p>}</div>{actions && <div className="page-actions">{actions}</div>}</header>;
}

export function Alert({ tone = "danger", children, role }: { tone?: "danger" | "success" | "info" | "warning"; children: ReactNode; role?: "alert" | "status" }) {
  return <div className={`alert ${tone}`} role={role ?? (tone === "danger" ? "alert" : "status")}><Icon name={tone === "success" ? "check" : tone === "info" ? "info" : "alert"} size="sm" /><div>{children}</div></div>;
}
