'use client';
import { useState } from 'react';
import { ArrowUpRight, Layers, Search } from 'lucide-react';
import { type Area, number, percent } from '@/lib/api';

export default function Ramos({ segments, openSegment }: { segments: Area[]; openSegment: (value: string) => void }) {
  const [search, setSearch] = useState('');
  const [order, setOrder] = useState<'contatos' | 'score_medio' | 'com_celular' | 'com_email'>('contatos');
  const visible = segments.filter(s => s.nome.toLocaleLowerCase('pt-BR').includes(search.toLocaleLowerCase('pt-BR'))).sort((a, b) => b[order] - a[order]);
  const max = Math.max(...segments.map(s => s.contatos), 1);
  return <section className="segments-panel"><div className="table-toolbar"><div className="search-input"><Search size={18} /><input aria-label="Buscar ramo" placeholder="Encontre um ramo de atividade…" value={search} onChange={e => setSearch(e.target.value)} /></div><label className="sort-field">Comparar por <select aria-label="Ordenar ramos" value={order} onChange={e => setOrder(e.target.value as typeof order)}><option value="contatos">Empresas</option><option value="score_medio">Score médio</option><option value="com_celular">Com celular</option><option value="com_email">Com e-mail</option></select></label></div>
    <div className="table-scroll"><table className="data-table segments-table"><thead><tr><th>Ramo de atividade</th><th className="numeric">Empresas</th><th>Distribuição</th><th className="numeric">Celular</th><th className="numeric">E-mail</th><th className="numeric">Sem domínio*</th><th className="numeric">Score médio</th><th><span className="sr-only">Explorar</span></th></tr></thead><tbody>{visible.map((s, i) => <tr key={s.nome}><td><button className="company-name" onClick={() => openSegment(s.nome)}><span className="rank-index">{String(i + 1).padStart(2, '0')}</span>{s.nome}</button></td><td className="numeric strong">{number(s.contatos)}</td><td><div className="distribution-bar"><i style={{ width: `${Math.max(2, s.contatos / max * 100)}%` }} /></div></td><td className="numeric">{percent(s.com_celular, s.contatos)}</td><td className="numeric">{percent(s.com_email, s.contatos)}</td><td className="numeric">{percent(s.sem_dominio, s.contatos)}</td><td className="numeric">{s.score_medio.toFixed(1)}</td><td><button className="icon-button" aria-label={`Explorar ${s.nome}`} onClick={() => openSegment(s.nome)}><ArrowUpRight size={17} /></button></td></tr>)}</tbody></table></div>
    {!visible.length && <div className="empty"><Layers size={28} /><h2>Nenhum ramo encontrado</h2><p>Tente buscar por uma atividade mais ampla.</p></div>}
    <p className="table-footnote">* E-mail em provedor gratuito. Esse indicador não comprova ausência de site. Os ramos são agrupamentos comerciais derivados do CNAE.</p>
  </section>;
}
