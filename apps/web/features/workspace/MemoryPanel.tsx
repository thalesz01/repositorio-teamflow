"use client";

import { useEffect, useState, type FormEvent } from "react";
import { MemoryIndexPanel } from "./MemoryIndexPanel";
import { api, errorMessage, type Schema } from "./api";

const statusLabels: Record<Schema["MemoryStatus"], string> = { candidate: "Candidata", validated: "Validada", obsolete: "Obsoleta" };
export function MemoryPanel({ path }: { path: string }) {
  const [items, setItems] = useState<Schema["MemoryDto"][]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [archived, setArchived] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [selected, setSelected] = useState<Schema["MemoryDetail"] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    api<Schema["PageResultOfMemoryDto"]>(`${path}/memory/?page=${page}&pageSize=20&archived=${archived}`, { signal: controller.signal })
      .then(result => { setItems(result.items); setTotal(Number(result.total)); })
      .catch(failure => { if (!controller.signal.aborted) setError(errorMessage(failure)); });
    return () => controller.abort();
  }, [path, page, archived, refresh]);
  async function perform(action: () => Promise<void>) {
    setBusy(true); setError(null);
    try { await action(); setRefresh(value => value + 1); }
    catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    await perform(async () => {
      const title = String(data.get("title"));
      const content = String(data.get("content"));
      const detail = selected
        ? await api<Schema["MemoryDetail"]>(`${path}/memory/${selected.item.id}`, { method: "PUT", body: JSON.stringify({
          version: selected.item.version, title, content, status: String(data.get("status")) as Schema["MemoryStatus"],
        } satisfies Schema["MemoryUpdate"]) })
        : await api<Schema["MemoryDetail"]>(`${path}/memory/`, { method: "POST", body: JSON.stringify({
          key: String(data.get("key")), title, content, kind: String(data.get("kind")),
        }) });
      setSelected(detail);
    });
  }
  return <details className="detail-panel" open><summary>Memória compartilhada</summary>
    <p className="hint">Resoluções de merges entram como candidatas. A equipe valida o conteúdo antes de reutilizá-lo como orientação.</p>
    {error && <p role="alert" className="error">{error}</p>}
    <label><input type="checkbox" checked={archived} onChange={event => { setArchived(event.target.checked); setPage(1); setSelected(null); }} /> Mostrar arquivadas</label>
    {!items.length && <p>Nenhuma fonte nesta página.</p>}
    <ul>{items.map(item => <li key={item.id}>
      {!archived ? <button disabled={busy} onClick={() => void perform(async () => {
        setSelected(await api<Schema["MemoryDetail"]>(`${path}/memory/${item.id}`));
      })}>{item.title}</button> : <span>{item.title}</span>}
      {` · ${statusLabels[item.status]}`}
      {item.pullRequestUrl && <a href={item.pullRequestUrl} target="_blank" rel="noreferrer"> · Fonte: PR</a>}
      <button disabled={busy} onClick={() => void perform(async () => {
        if (archived) await api<Schema["MemoryDetail"]>(`${path}/memory/${item.id}/restore`, { method: "POST", body: JSON.stringify({ version: item.version }) });
        else await api<Schema["MemoryDetail"]>(`${path}/memory/${item.id}?version=${item.version}`, { method: "DELETE" });
        setSelected(null);
      })}>{archived ? "Restaurar como candidata" : "Arquivar"}</button>
    </li>)}</ul>
    <div className="toolbar"><button disabled={page === 1 || busy} onClick={() => setPage(value => value - 1)}>Fontes anteriores</button>
      <span>Página {page} · {total} fonte(s)</span><button disabled={page * 20 >= total || busy} onClick={() => setPage(value => value + 1)}>Próximas fontes</button>
      <button disabled={busy} onClick={() => { setSelected(null); setArchived(false); }}>Nova fonte</button></div>
    {!archived && <form key={selected?.item.version ?? "new"} onSubmit={save} aria-label="Editar memória">
      <fieldset disabled={busy}>
        <label>Chave da fonte<input name="key" required maxLength={240} defaultValue={selected?.item.key ?? ""} disabled={selected !== null} /></label>
        {!selected && <label>Tipo<select name="kind"><option value="document">Documento</option><option value="code">Código permitido</option><option value="decision">Decisão</option></select></label>}
        <label>Título da memória<input name="title" required maxLength={160} defaultValue={selected?.item.title ?? ""} /></label>
        <label>Conteúdo da memória<textarea name="content" required maxLength={32768} rows={5} defaultValue={selected?.content ?? ""} /></label>
        {selected && <label>Status da memória<select name="status" defaultValue={selected.item.status}>
          <option value="candidate">Candidata</option><option value="validated">Validada pela equipe</option><option value="obsolete">Obsoleta</option>
        </select></label>}
        <button className="primary">{selected ? "Salvar versão da memória" : "Criar fonte candidata"}</button>
      </fieldset>
      <p className="hint">Conteúdo limitado a 32 KiB. Arquivar é reversível e suspende o uso da fonte; nenhuma exclusão física é realizada.</p>
    </form>}
    {selected && !archived && <MemoryIndexPanel path={path} source={selected.item} />}
  </details>;
}
