'use client';
import { useEffect, useRef, useState } from 'react';
import Markdown from 'react-markdown';
import { ArrowUp, ArrowUpRight, MessageSquare, RotateCcw } from 'lucide-react';
import { api, filterLabel, number, type BaseInfo, type ChatResponse, type Contact, type Filters } from '@/lib/api';
import { ContactDetails } from './Empresas';

export type ChatTurn = { role: 'user' | 'model'; text: string; response?: ChatResponse };
const suggestions = ['Quais municípios têm mais empresas na base?', 'Compare Fortaleza e Juazeiro do Norte.', 'Encontre padarias em Sobral com celular.', 'Me ajude a planejar uma pesquisa de mercado.'];
const labels: Record<string, string> = { cidade: 'Município', bairro: 'Bairro', segmento: 'Ramo', porte: 'Porte', abertura: 'Ano', contatos: 'Empresas', com_email: 'Com e-mail', com_celular: 'Com celular', sem_dominio: 'E-mail sem domínio', score_medio: 'Score médio' };

export default function Assistente({ base, turns, setTurns, explore }: { base: BaseInfo; turns: ChatTurn[]; setTurns: (value: ChatTurn[]) => void; explore: (filters: Filters) => void }) {
  const [question, setQuestion] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [detail, setDetail] = useState<Contact | null>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const abort = useRef<AbortController | null>(null);
  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => { bottom.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); }, [turns, loading]);
  async function submit(value = question) {
    const text = value.trim(); if (!text || loading) return;
    const history = turns.slice(-12).map(t => ({ role: t.role, text: t.text.slice(0, 6000) }));
    const updated: ChatTurn[] = [...turns, { role: 'user', text }];
    setTurns(updated); setQuestion(''); setLoading(true); setError('');
    const controller = new AbortController(); abort.current = controller;
    try {
      const response = await api<ChatResponse>('chat', { pergunta: text, historico: history }, controller.signal);
      setTurns([...updated, { role: 'model', text: response.texto, response }]);
    } catch (e) { if (!controller.signal.aborted) { setError((e as Error).message); setQuestion(text); } }
    finally { if (!controller.signal.aborted) { setLoading(false); input.current?.focus(); } }
  }
  return <section className="assistant-layout">
    <aside className="assistant-about"><span className="eyebrow">Uma conversa aberta</span><h2>Dados locais.<br />Ideias sem fronteiras.</h2><p>Pergunte sobre o Ceará, compare empresas ou converse sobre qualquer assunto.</p><div className="assistant-capability"><span className="status-dot" data-ready={base.ia_configurada} /><span>{base.ia_configurada ? 'Gemini configurado' : 'Chave do Gemini necessária'}</span></div><p className="help-text">As consultas usam a base local. Perguntas gerais usam o conhecimento do modelo, sem pesquisa na internet.</p><div className="data-note"><strong>Seus contatos ficam aqui</strong><p>O Gemini recebe perguntas, estatísticas e categorias. A identificação das empresas aparece diretamente nesta aplicação.</p></div><a className="text-button external-link" href="https://aistudio.google.com/apikey" target="_blank" rel="noopener noreferrer">Criar chave no Google AI Studio <ArrowUpRight size={14} /></a></aside>
    <div className="conversation-panel"><div className="conversation-header"><span><MessageSquare size={17} /> Assistente</span><button className="text-button" disabled={loading || !turns.length} onClick={() => { setTurns([]); setError(''); }}><RotateCcw size={14} /> Nova conversa</button></div>
      <div className="conversation-body" aria-live="polite">
        {!turns.length && <div className="conversation-welcome"><span className="assistant-mark"><MessageSquare size={24} /></span><h3>O que você quer descobrir?</h3><p>Comece por uma pergunta ou siga uma destas ideias.</p><div className="suggestions">{suggestions.map(text => <button key={text} disabled={loading} onClick={() => void submit(text)}>{text}<ArrowUpRight size={16} /></button>)}</div></div>}
        {turns.map((turn, i) => <article key={i} className={`chat-message ${turn.role}`}><span className="message-author">{turn.role === 'user' ? 'Você' : 'Assistente'}</span><div className="markdown"><Markdown>{turn.text}</Markdown></div>
          {turn.response?.resultados.map((result, j) => <div className="chat-result" key={j}><div className="result-heading"><strong>{result.titulo}</strong><button className="text-button" onClick={() => explore(result.filtros)}>Explorar recorte <ArrowUpRight size={14} /></button></div><p className="help-text">{filterLabel(result.filtros)}</p>
            {result.tipo === 'empresas' ? <><p className="muted">{number(result.total)} encontradas · {result.itens.length} exibidas</p><div className="table-scroll"><table className="data-table"><thead><tr><th>Empresa</th><th>Município</th><th>Ramo</th><th className="numeric">Score</th></tr></thead><tbody>{result.itens.map(c => <tr key={c.id}><td><button className="company-name" onClick={() => setDetail(c)}>{c.nome}</button></td><td>{c.cidade}</td><td>{c.segmento}</td><td className="numeric">{c.score}</td></tr>)}</tbody></table></div></> : <div className="table-scroll"><table className="data-table"><thead><tr>{[...result.agrupar_por, 'contatos', 'com_celular', 'com_email', 'score_medio'].map(k => <th key={k}>{labels[k] || k}</th>)}</tr></thead><tbody>{result.itens.map((row, k) => <tr key={k}>{[...result.agrupar_por, 'contatos', 'com_celular', 'com_email', 'score_medio'].map(col => <td key={col}>{typeof row[col] === 'number' ? number(row[col] as number) : row[col] || 'Não informado'}</td>)}</tr>)}</tbody></table></div>}
          </div>)}
          {!!turn.response?.fontes.length && <div className="chat-sources">{turn.response.fontes.map((source, j) => <p key={j}><strong>{source.fonte}</strong> · Cadastro de {source.versao}<br />{filterLabel(source.filtros)}</p>)}</div>}
        </article>)}
        {loading && <div className="assistant-thinking" role="status"><span className="skeleton" />Consultando e preparando a resposta…</div>}
        {error && <div className="inline-error" role="alert">{error}</div>}<div ref={bottom} />
      </div>
      <form className="composer" onSubmit={e => { e.preventDefault(); void submit(); }}><label className="sr-only" htmlFor="question">Sua pergunta</label><textarea ref={input} id="question" maxLength={4000} rows={2} placeholder="Pergunte sobre a base ou qualquer outro assunto…" value={question} onChange={e => setQuestion(e.target.value)} onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); void submit(); } }} /><button className="send-button" aria-label="Enviar pergunta" disabled={loading || !question.trim()}><ArrowUp size={20} /></button></form>
      <p className="composer-note">Enter para enviar · Shift + Enter para nova linha · Conversa guardada apenas durante a sessão</p>
    </div>
    {detail && <ContactDetails contact={detail} close={() => setDetail(null)} />}
  </section>;
}
