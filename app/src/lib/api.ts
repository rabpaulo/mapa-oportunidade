export type Filters = {
  termo: string; cidade: string; cidades: string[]; segmento: string; segmentos: string[];
  porte: string; bairro: string; ano_minimo: number | null; ano_maximo: number | null;
  score_minimo: number; somente_celular: boolean; somente_email: boolean;
  somente_sem_dominio: boolean; ordem: 'score' | 'nome' | 'cidade' | 'recente' | 'antiga';
};
export const emptyFilters = (): Filters => ({ termo: '', cidade: '', cidades: [], segmento: '', segmentos: [], porte: '', bairro: '', ano_minimo: null, ano_maximo: null, score_minimo: 0, somente_celular: false, somente_email: false, somente_sem_dominio: false, ordem: 'score' });
export type Contact = {
  id: number; cnpj: string; nome: string; empresa: string; email: string; telefone: string;
  whatsapp: string; cidade: string; bairro: string; endereco: string; segmento: string;
  oportunidade: string; porte: string; abertura: string; dominio_proprio: boolean;
  tem_celular: boolean; score: number;
};
export type PageContacts = { total: number; itens: Contact[] };
export type Area = { codigo: string; nome: string; contatos: number; com_email: number; com_celular: number; sem_dominio: number; score_medio: number };
export type BaseInfo = {
  disponivel: boolean; erro?: string; uf?: string; contatos?: number; municipios?: number;
  ramos?: number; versao_receita?: string; gerado_em?: string; recorte?: string;
  com_email?: number; com_celular?: number; sem_dominio?: number; score_medio?: number;
  tamanho_mb?: number; ia_configurada: boolean; modelo_ia: string;
};
export type AnalysisRow = Record<string, string | number>;
export type ChatResult = { tipo: 'empresas'; titulo: string; filtros: Filters; total: number; itens: Contact[] } | { tipo: 'analise'; titulo: string; filtros: Filters; agrupar_por: string[]; itens: AnalysisRow[] };
export type ChatResponse = { texto: string; resultados: ChatResult[]; fontes: { fonte: string; versao: string; filtros: Filters }[]; modelo: string };

export const number = (value: number | undefined) => new Intl.NumberFormat('pt-BR').format(value ?? 0);
export const percent = (part: number, total: number) => (total ? part * 100 / total : 0).toLocaleString('pt-BR', { maximumFractionDigits: 1 }) + '%';
export const date = (value: string | undefined) => value ? value.split(' ')[0].split('-').reverse().join('/') : '—';
export function filterLabel(f: Filters): string {
  const labels = [f.termo && `Busca: ${f.termo}`, f.cidade, ...f.cidades, f.segmento, ...f.segmentos, f.porte, f.bairro,
    f.score_minimo > 0 && `Score ≥ ${f.score_minimo}`, f.somente_celular && 'Com celular', f.somente_email && 'Com e-mail', f.somente_sem_dominio && 'E-mail sem domínio próprio',
    f.ano_minimo && `Desde ${f.ano_minimo}`, f.ano_maximo && `Até ${f.ano_maximo}`].filter(Boolean);
  return labels.length ? labels.join(' · ') : 'Todo o Ceará';
}
