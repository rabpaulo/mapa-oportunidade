'use client';
import { useCallback, useEffect, useRef, useState } from 'react';
import dynamic from 'next/dynamic';
import { ArrowUpRight, Database, Layers, Map, MessageSquare, Moon, Sun, Users } from 'lucide-react';
import Empresas from '@/componentes/Empresas';
import Ramos from '@/componentes/Ramos';
import Assistente, { type ChatTurn } from '@/componentes/Assistente';
import Base from '@/componentes/Base';
import { api, date, emptyFilters, number, percent, type Area, type BaseInfo, type Filters, type Job } from '@/lib/api';

const MapView = dynamic(() => import('@/componentes/MapView'), { ssr: false, loading: () => <div className="map-skeleton skeleton" role="status" aria-label="Preparando o mapa" /> });
type Screen = 'mapa' | 'empresas' | 'ramos' | 'assistente' | 'base';
const nav = [{ id: 'mapa', title: 'Mapa', icon: Map }, { id: 'empresas', title: 'Empresas', icon: Users }, { id: 'ramos', title: 'Ramos', icon: Layers }, { id: 'assistente', title: 'Assistente', icon: MessageSquare }, { id: 'base', title: 'Base', icon: Database }] as const;
const titles = { mapa: ['O Ceará, em perspectiva.', 'Cada município conta uma parte da história. Explore onde estão as empresas e encontre seu próximo recorte.'], empresas: ['Empresas para conhecer.', 'Transforme a base em um recorte útil. Pesquise, compare e leve os contatos que interessam.'], ramos: ['Atividades que movem o estado.', 'Compare a presença de cada ramo e descubra novas possibilidades no território.'], assistente: ['Uma pergunta abre caminhos.', 'Converse livremente. Quando a resposta estiver na base, o assistente consulta os dados do Ceará.'], base: ['Conheça a origem dos dados.', 'Um acervo local, com fontes claras e atualização sob seu controle.'] };
export default function Home() {
  const [screen, setScreen] = useState<Screen>('mapa');
  const [theme, setTheme] = useState<'claro' | 'escuro'>('claro');
  const [base, setBase] = useState<BaseInfo | null>(null);
  const [areas, setAreas] = useState<Area[]>([]);
  const [segments, setSegments] = useState<Area[]>([]);
  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [job, setJob] = useState<Job>({ status: 'ocioso', linhas: [], iniciado_em: null, baixar_cadastro: false });
  const [error, setError] = useState('');
  const priorJob = useRef('ocioso');
  const reload = useCallback(async () => {
    try { const info = await api<BaseInfo>('base'); setBase(info); setError(''); if (info.disponivel) { const [cities, branches] = await Promise.all([api<Area[]>('municipios'), api<Area[]>('ramos')]); setAreas(cities); setSegments(branches); } } catch (e) { setError((e as Error).message); }
  }, []);
  useEffect(() => { void reload(); const saved = localStorage.getItem('ceara-tema'); const value = saved === 'escuro' ? 'escuro' : 'claro'; setTheme(value); document.documentElement.dataset.tema = value; }, [reload]);
  useEffect(() => {
    let disposed = false;
    const check = async () => { try { const value = await api<Job>('tarefas'); if (disposed) return; setJob(value); if (priorJob.current === 'rodando' && value.status === 'concluido') void reload(); priorJob.current = value.status; } catch { /* Initial base errors are presented through the main UI. */ } };
    void check(); const timer = setInterval(check, 2500); return () => { disposed = true; clearInterval(timer); };
  }, [reload]);
  function toggleTheme() { const next = theme === 'claro' ? 'escuro' : 'claro'; setTheme(next); document.documentElement.dataset.tema = next; localStorage.setItem('ceara-tema', next); }
  function explore(value: Filters) { setFilters(value); setScreen('empresas'); }
  const [title, description] = titles[screen];
  return <div className="app-shell"><a className="skip-link" href="#main">Ir para o conteúdo</a>
    <header className="site-header"><button className="brand" onClick={() => setScreen('mapa')} aria-label="Mapa de Oportunidades Ceará, início"><span className="brand-symbol"><Map size={24} strokeWidth={1.5} /></span><span>Mapa de Oportunidades<strong>Ceará<span className="brand-period">.</span></strong></span></button>
      <nav aria-label="Navegação principal">{nav.map(item => <button key={item.id} className={screen === item.id ? 'active' : ''} aria-current={screen === item.id ? 'page' : undefined} onClick={() => setScreen(item.id)}><item.icon size={16} /><span>{item.title}</span></button>)}</nav>
      <div className="header-end"><span className="local-status"><span className="status-dot" data-ready={!!base?.disponivel} /> Base local</span><button className="icon-button" onClick={toggleTheme} aria-label={theme === 'claro' ? 'Ativar tema escuro' : 'Ativar tema claro'}>{theme === 'claro' ? <Moon size={18} /> : <Sun size={18} />}</button></div>
    </header>
    <main id="main"><div className="page-heading"><div><span className="eyebrow">CE / Brasil <span className="eyebrow-divider">—</span> Inteligência territorial</span><h1>{title}</h1><p>{description}</p></div><div className="edition"><span>CADASTRO RECEITA FEDERAL</span><strong>{date(base?.versao_receita)}</strong><span>{base?.disponivel ? '184 municípios · um território' : 'Acervo em preparação'}</span></div></div>
      {job.status === 'rodando' && <button className="job-banner" onClick={() => setScreen('base')}><span className="status-dot pulsing" /> Atualizando a base do Ceará <span>{job.linhas[job.linhas.length - 1]}</span><ArrowUpRight size={16} /></button>}
      {error ? <div className="empty error" role="alert"><h2>Não foi possível abrir a aplicação</h2><p>{error}</p><button className="button" onClick={() => void reload()}>Tentar novamente</button></div> : !base ? <div className="loading-page"><div className="skeleton" /><div className="skeleton" /><span role="status">Abrindo a base do Ceará…</span></div> : <>
        {screen === 'assistente' ? <Assistente base={base} turns={turns} setTurns={setTurns} explore={explore} /> : screen === 'base' ? <Base base={base} job={job} setJob={value => { priorJob.current = value.status; setJob(value); }} /> : !base.disponivel ? <div className="empty"><Database size={32} /><h2>O Ceará começa pela base.</h2><p>{base.erro}</p><button className="button primary" onClick={() => setScreen('base')}>Preparar base <ArrowUpRight size={16} /></button></div> : screen === 'mapa' ? <>
          <div className="stats-strip"><div><span>Empresas com contato</span><strong>{number(base.contatos)}</strong><small>Ativas no cadastro</small></div><div><span>Municípios mapeados</span><strong>{number(base.municipios)}<small>/ 184</small></strong><small>Do litoral ao sertão</small></div><div><span>Com celular</span><strong>{percent(base.com_celular ?? 0, base.contatos ?? 0)}</strong><small>{number(base.com_celular)} empresas</small></div><div><span>Ramos de atividade</span><strong>{number(base.ramos)}</strong><small>Possibilidades para explorar</small></div></div>
          <MapView base={base} areas={areas} theme={theme} openCity={city => explore({ ...emptyFilters(), cidade: city })} />
          <div className="map-bottom-note"><p>Uma fotografia do cadastro, com empresas ativas e contato aproveitável.</p><button className="text-button" onClick={() => setScreen('assistente')}>Faça uma pergunta sobre os dados <ArrowUpRight size={15} /></button></div>
        </> : screen === 'empresas' ? <Empresas filters={filters} setFilters={setFilters} areas={areas} segments={segments} refresh={base.gerado_em} /> : <Ramos segments={segments} openSegment={segmento => explore({ ...emptyFilters(), segmento })} />}
      </>}
    </main>
    <footer className="site-footer"><span>Ceará, município por município.</span><span>Dados Receita Federal e IBGE <span className="footer-separator">/</span> Derivado do <a href="https://github.com/ivobraatz/garimpo" target="_blank" rel="noopener noreferrer">Garimpo</a> · AGPL-3.0</span></footer>
  </div>;
}
