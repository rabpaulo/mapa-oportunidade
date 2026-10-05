'use client';

import { useEffect, useRef, useState } from 'react';
import { ArrowDownToLine, ArrowUpRight, ChevronLeft, ChevronRight, Mail, Phone, Search, SlidersHorizontal, X } from 'lucide-react';
import { api, emptyFilters, exportContacts, filterLabel, number, type Area, type Contact, type Filters, type PageContacts } from '@/lib/api';

export function ContactDetails({ contact: c, close }: { contact: Contact; close: () => void }) {
  const panel = useRef<HTMLElement>(null);
  useEffect(() => {
    const before = document.activeElement as HTMLElement | null;
    panel.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close();
      if (event.key === 'Tab' && panel.current) {
        const nodes = Array.from(panel.current.querySelectorAll<HTMLElement>('button, a[href]'));
        const first = nodes[0], last = nodes[nodes.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
      }
    };
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('keydown', onKey); before?.focus(); };
  }, [close]);
  return <div className="detail-backdrop" onClick={close}><aside ref={panel} tabIndex={-1} className="contact-detail" role="dialog" aria-modal="true" aria-label={`Detalhes de ${c.nome}`} onClick={e => e.stopPropagation()}>
    <header><span className="eyebrow">Perfil da empresa</span><button className="icon-button" aria-label="Fechar detalhes" onClick={close}><X size={19} /></button></header>
    <span className="score large">{c.score}<small>/100</small></span><h2>{c.nome}</h2><p className="muted">{c.empresa}</p><span className="tag">{c.segmento}</span>
    <dl className="detail-list">{[['CNPJ', c.cnpj], ['Município', c.cidade], ['Bairro', c.bairro], ['Endereço', c.endereco], ['Porte', c.porte], ['Ano de abertura', c.abertura], ['Telefone', c.telefone], ['E-mail', c.email]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value || 'Não informado'}</dd></div>)}</dl>
    <div className="detail-actions">{c.whatsapp && <a className="button primary" href={c.whatsapp} target="_blank" rel="noopener noreferrer">Abrir WhatsApp <ArrowUpRight size={16} /></a>}{c.email && <a className="button" href={`mailto:${c.email}`}><Mail size={16} /> Enviar e-mail</a>}</div>
    <div className="data-note"><strong>Critérios de priorização</strong><p>{c.oportunidade}</p><p>O score prioriza serviços digitais. Domínio próprio é inferido pelo e-mail e não comprova presença ou ausência de site.</p></div>
  </aside></div>;
}

export default function Empresas({ filters, setFilters, areas, segments, refresh }: { filters: Filters; setFilters: (value: Filters) => void; areas: Area[]; segments: Area[]; refresh: string | undefined }) {
  const [data, setData] = useState<PageContacts>({ total: 0, itens: [] });
  const [facets, setFacets] = useState<{ municipios: Area[]; segmentos: Area[] } | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [exporting, setExporting] = useState(false);
  const [exportMessage, setExportMessage] = useState('');
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [detail, setDetail] = useState<Contact | null>(null);
  const [showFilters, setShowFilters] = useState(true);
  const [term, setTerm] = useState(filters.termo);
  const size = 50;
  useEffect(() => { setSelected(new Set()); setDetail(null); setPage(1); }, [refresh]);
  const change = <K extends keyof Filters>(key: K, value: Filters[K]) => { setPage(1); setSelected(new Set()); setFilters({ ...filters, [key]: value }); };
  useEffect(() => { setShowFilters(window.innerWidth > 760); }, []);
  useEffect(() => { if (term === filters.termo) return; const t = setTimeout(() => { setPage(1); setSelected(new Set()); setFilters({ ...filters, termo: term }); }, 300); return () => clearTimeout(t); }, [term, filters, setFilters]);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError('');
    api<PageContacts>('contatos/buscar', { filtros: filters, pagina: page, por_pagina: size }, controller.signal)
      .then(setData).catch(e => { if (!controller.signal.aborted) setError(e.message); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [filters, page, refresh]);
  useEffect(() => {
    const controller = new AbortController();
    api<{ municipios: Area[]; segmentos: Area[] }>('facetas', filters, controller.signal).then(setFacets).catch(() => { if (!controller.signal.aborted) setFacets(null); });
    return () => controller.abort();
  }, [filters, refresh]);
  const toggle = (id: number) => setSelected(values => { const next = new Set(values); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const allSelected = data.itens.length > 0 && data.itens.every(c => selected.has(c.id));
  const exportNow = async () => { setExporting(true); setExportMessage(''); try { await exportContacts(filters, [...selected], refresh); setExportMessage('Planilha pronta. Confira os downloads do navegador.'); } catch (e) { setExportMessage((e as Error).message); } finally { setExporting(false); } };
  const totalPages = Math.max(1, Math.ceil(data.total / size));
  const cities = facets?.municipios ?? areas;
  const branches = facets?.segmentos ?? segments;
  const addMissing = (list: Area[], value: string) => value && !list.some(a => a.nome === value) ? [{ codigo: value, nome: value, contatos: 0 } as Area, ...list] : list;
  return <section className="companies-layout" data-filters={showFilters}>
    <aside className="filters-panel">
      <div className="filters-heading"><h2><SlidersHorizontal size={16} /> Seu recorte</h2><button className="text-button" onClick={() => { setPage(1); setTerm(''); setSelected(new Set()); setFilters(emptyFilters()); }}>Limpar</button></div>
      <label className="field"><span>Município</span><select value={filters.cidade} onChange={e => change('cidade', e.target.value)}><option value="">Todo o Ceará</option>{addMissing(cities, filters.cidade).map(a => <option key={a.nome} value={a.nome}>{a.nome} · {number(a.contatos)}</option>)}</select></label>
      <label className="field"><span>Ramo de atividade</span><select value={filters.segmento} onChange={e => change('segmento', e.target.value)}><option value="">Todos os ramos</option>{addMissing(branches, filters.segmento).map(a => <option key={a.nome} value={a.nome}>{a.nome} · {number(a.contatos)}</option>)}</select></label>
      <label className="field"><span>Porte</span><select value={filters.porte} onChange={e => change('porte', e.target.value)}><option value="">Todos os portes</option>{['Microempresa', 'Pequeno porte', 'Medio/grande', 'Nao informado'].map(p => <option key={p}>{p}</option>)}</select></label>
      <label className="field"><span>Bairro</span><input value={filters.bairro} onChange={e => change('bairro', e.target.value)} placeholder="Nome completo do bairro" /></label>
      <label className="field"><span>Score mínimo <strong>{filters.score_minimo}</strong></span><input type="range" min="0" max="100" step="5" value={filters.score_minimo} onChange={e => change('score_minimo', Number(e.target.value))} /></label>
      <div className="year-fields"><label className="field"><span>Abertura desde</span><input type="number" min="1800" max="2200" placeholder="Ano" value={filters.ano_minimo ?? ''} onChange={e => change('ano_minimo', e.target.value ? Number(e.target.value) : null)} /></label><label className="field"><span>Até</span><input type="number" min="1800" max="2200" placeholder="Ano" value={filters.ano_maximo ?? ''} onChange={e => change('ano_maximo', e.target.value ? Number(e.target.value) : null)} /></label></div>
      <div className="filter-checks">{([['somente_celular', 'Com celular'], ['somente_email', 'Com e-mail'], ['somente_sem_dominio', 'E-mail sem domínio próprio']] as const).map(([key, label]) => <label key={key}><input type="checkbox" checked={filters[key]} onChange={e => change(key, e.target.checked)} />{label}</label>)}</div>
      <p className="help-text">A base reúne empresas ativas com e-mail ou celular. O score prioriza oportunidades em serviços digitais.</p>
    </aside>
    <div className="companies-main">
      <div className="table-toolbar"><div className="search-input"><Search size={18} /><input aria-label="Buscar empresas" placeholder="Buscar nome, empresa, cidade ou ramo…" value={term} onChange={e => setTerm(e.target.value)} /></div><button className="button filter-toggle" onClick={() => setShowFilters(!showFilters)} aria-expanded={showFilters}><SlidersHorizontal size={16} /> Filtros</button><button className="button" onClick={exportNow} disabled={exporting || !data.total || loading}><ArrowDownToLine size={16} />{exporting ? 'Gerando Excel…' : selected.size ? `Exportar ${number(selected.size)}` : 'Exportar recorte'}</button></div>
      <div className="table-context"><span><strong>{number(data.total)}</strong> empresas {selected.size > 0 && <span className="tag">{number(selected.size)} selecionadas <button aria-label="Limpar seleção" onClick={() => setSelected(new Set())}><X size={12} /></button></span>}</span><label>Ordenar <select aria-label="Ordenar empresas" value={filters.ordem} onChange={e => change('ordem', e.target.value as Filters['ordem'])}><option value="score">Maior score</option><option value="nome">Nome</option><option value="cidade">Município</option><option value="recente">Mais recentes</option><option value="antiga">Mais antigas</option></select></label></div>
      {filterLabel(filters) !== 'Todo o Ceará' && <p className="active-filters">{filterLabel(filters)}</p>}
      {exportMessage && <p className="inline-message" role="status">{exportMessage}</p>}
      {error ? <div className="empty error" role="alert"><h2>Não foi possível buscar as empresas</h2><p>{error}</p><button className="button" onClick={() => setFilters({ ...filters })}>Tentar novamente</button></div> : loading ? <div className="table-skeleton" aria-label="Buscando empresas" role="status">{Array.from({ length: 8 }, (_, i) => <div key={i} className="skeleton" />)}</div> : <>
        <div className="table-scroll"><table className="data-table contacts-table"><thead><tr><th><input type="checkbox" aria-label="Selecionar empresas da página" checked={allSelected} onChange={() => setSelected(values => { const next = new Set(values); data.itens.forEach(c => allSelected ? next.delete(c.id) : next.add(c.id)); return next; })} /></th><th>Empresa</th><th>Município</th><th>Ramo</th><th>Contato</th><th className="numeric">Score</th><th><span className="sr-only">Detalhes</span></th></tr></thead><tbody>{data.itens.map(c => <tr key={c.id} data-selected={selected.has(c.id)}><td><input type="checkbox" aria-label={`Selecionar ${c.nome}`} checked={selected.has(c.id)} onChange={() => toggle(c.id)} /></td><td><button className="company-name" onClick={() => setDetail(c)}>{c.nome}</button><span className="table-secondary">{c.porte} · {c.abertura || 'Ano não informado'}</span></td><td>{c.cidade}<span className="table-secondary">{c.bairro}</span></td><td><span className="segment-cell">{c.segmento}</span></td><td><div className="contact-icons">{c.whatsapp && <a href={c.whatsapp} title="Abrir WhatsApp" aria-label={`WhatsApp de ${c.nome}`} target="_blank" rel="noopener noreferrer"><Phone size={16} /></a>}{c.email && <a href={`mailto:${c.email}`} title={c.email} aria-label={`E-mail de ${c.nome}`}><Mail size={16} /></a>}</div></td><td className="numeric"><span className="score" data-level={c.score >= 75 ? 'alto' : 'normal'}>{c.score}</span></td><td><button className="icon-button" aria-label={`Ver detalhes de ${c.nome}`} onClick={() => setDetail(c)}><ArrowUpRight size={16} /></button></td></tr>)}</tbody></table></div>
        {!data.itens.length && <div className="empty"><Search size={30} /><h2>Nenhuma empresa neste recorte</h2><p>Experimente outro município, ramo ou um score menor.</p></div>}
        <div className="pagination"><span>{data.total ? `${number((page - 1) * size + 1)}–${number(Math.min(page * size, data.total))} de ${number(data.total)}` : '0 resultados'}</span><div><button className="icon-button" disabled={page === 1} aria-label="Página anterior" onClick={() => setPage(page - 1)}><ChevronLeft size={18} /></button><span>Página {page} de {number(totalPages)}</span><button className="icon-button" disabled={page >= totalPages} aria-label="Próxima página" onClick={() => setPage(page + 1)}><ChevronRight size={18} /></button></div></div>
      </>}
    </div>
    {detail && <ContactDetails contact={detail} close={() => setDetail(null)} />}
  </section>;
}
