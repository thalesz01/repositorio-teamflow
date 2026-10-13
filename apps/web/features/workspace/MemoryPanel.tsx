"use client";

import { useEffect, useState, type FormEvent } from "react";
import { MemoryIndexPanel } from "./MemoryIndexPanel";
import { Alert, EmptyState, Icon } from "./ui";
import { api, errorMessage, type Schema } from "./api";

const statusLabels: Record<Schema["MemoryStatus"], string> = { candidate: "Candidata", validated: "Validada", obsolete: "Obsoleta" };
const statusTones: Record<Schema["MemoryStatus"], string> = { candidate: "warning", validated: "success", obsolete: "" };

export function MemoryPanel({ path }: { path: string }) {
  const [items, setItems] = useState<Schema["MemoryDto"][]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [archived, setArchived] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [selected, setSelected] = useState<Schema["MemoryDetail"] | null>(null);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    api<Schema["PageResultOfMemoryDto"]>(`${path}/memory/?page=${page}&pageSize=20&archived=${archived}`, { signal: controller.signal })
      .then(result => { setItems(result.items); setTotal(Number(result.total)); setLoaded(true); })
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
  return <>
    <p className="hint" style={{ marginTop: -8 }}>Resoluções de merges entram como candidatas. A equipe valida o conteúdo antes de reutilizá-lo como orientação.</p>
    {error && <Alert>{error}</Alert>}
    <div className="split">
      <section className="card" aria-label="Fontes da memória">
        <div className="card-header"><div><h2>Fontes</h2><p>{total} fonte(s) {archived ? "arquivadas" : "ativas"}</p></div>
          <button className="sm" style={{ marginLeft: "auto" }} disabled={busy} onClick={() => { setSelected(null); setArchived(false); }}><Icon name="plus" size="sm" />Nova fonte</button></div>
        <div style={{ padding: "10px 16px", borderBottom: "1px solid var(--border)" }}><label><input type="checkbox" checked={archived} onChange={event => { setArchived(event.target.checked); setPage(1); setSelected(null); }} />Mostrar arquivadas</label></div>
        {loaded && !items.length && <EmptyState icon="memory" title="Nenhuma fonte nesta página">Crie uma fonte ao lado ou conclua uma tarefa para gerar uma resolução candidata.</EmptyState>}
        <ul className="source-list">{items.map(item => <li key={item.id} aria-current={selected?.item.id === item.id}>
          {!archived ? <button className="source-open" disabled={busy} onClick={() => void perform(async () => {
            setSelected(await api<Schema["MemoryDetail"]>(`${path}/memory/${item.id}`));
          })}>{item.title}<span className="source-key">{item.key}</span></button> : <span className="source-open"><strong>{item.title}</strong><span className="source-key">{item.key}</span></span>}
          <span className={`badge ${statusTones[item.status]}`}>{statusLabels[item.status]}</span>
          {item.pullRequestUrl && <a href={item.pullRequestUrl} target="_blank" rel="noreferrer" title="Abrir a revisão de origem"><Icon name="link" size="sm" label="Fonte: revisão" /></a>}
          <button className="ghost sm" disabled={busy} onClick={() => void perform(async () => {
            if (archived) await api<Schema["MemoryDetail"]>(`${path}/memory/${item.id}/restore`, { method: "POST", body: JSON.stringify({ version: item.version }) });
            else await api<Schema["MemoryDetail"]>(`${path}/memory/${item.id}?version=${item.version}`, { method: "DELETE" });
            setSelected(null);
          })}>{archived ? "Restaurar como candidata" : "Arquivar"}</button>
        </li>)}</ul>
        <div className="pager"><button className="sm" disabled={page === 1 || busy} onClick={() => setPage(value => value - 1)}>Fontes anteriores</button>
          <span>Página {page}</span><button className="sm" disabled={page * 20 >= total || busy} onClick={() => setPage(value => value + 1)}>Próximas fontes</button></div>
      </section>
      <div className="stack">
        {!archived && <section className="card">
          <div className="card-header"><div><h2>{selected ? "Editar fonte" : "Nova fonte"}</h2><p>{selected ? "Ao validar, a fonte pode ser indexada e citada em análises." : "Registre uma decisão, documento ou código permitido."}</p></div>
            {selected && <span className={`badge ${statusTones[selected.item.status]}`}>{statusLabels[selected.item.status]}</span>}</div>
          <div className="card-body"><form key={selected?.item.version ?? "new"} onSubmit={save} aria-label="Editar memória">
            <fieldset disabled={busy}>
              <label>Chave da fonte<input name="key" required maxLength={240} defaultValue={selected?.item.key ?? ""} disabled={selected !== null} placeholder="docs/regra-de-arredondamento.md" /></label>
              {!selected && <label>Tipo<select name="kind"><option value="document">Documento</option><option value="code">Código permitido</option><option value="decision">Decisão</option></select></label>}
              <label>Título da memória<input name="title" required maxLength={160} defaultValue={selected?.item.title ?? ""} /></label>
              <label>Conteúdo da memória<textarea name="content" required maxLength={32768} rows={7} defaultValue={selected?.content ?? ""} /></label>
              {selected && <label>Status da memória<select name="status" defaultValue={selected.item.status}>
                <option value="candidate">Candidata</option><option value="validated">Validada pela equipe</option><option value="obsolete">Obsoleta</option>
              </select></label>}
              <div className="actions"><button className="primary">{selected ? "Salvar versão da memória" : "Criar fonte candidata"}</button></div>
            </fieldset>
            <p className="hint">Conteúdo limitado a 32 KiB. Arquivar é reversível e suspende o uso da fonte; nenhuma exclusão física é realizada.</p>
          </form></div>
        </section>}
        {selected && !archived && <MemoryIndexPanel path={path} source={selected.item} />}
      </div>
    </div>
  </>;
}
