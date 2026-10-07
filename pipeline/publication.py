"""Esquema público independente: nenhum campo nominal é copiado."""
POLICY = 'empresas-sem-dados-pessoais-v2'
FIELDS = ('id', 'cnpj', 'cidade', 'cod_municipio', 'segmento', 'oportunidade', 'porte', 'abertura', 'score')
CORPORATE_NATURES = {'2038', '2046', '2054', '2062', '2070', '2089', '2097', '2143', '2151', '2160', '2178', '2194', '2216', '2224', '2232', '2240', '2259', '2267', '2291', '2305', '2313', '2321', '2330', '2348', '2356'}
SCHEMA = '''
CREATE TABLE contatos (id INTEGER PRIMARY KEY, cnpj TEXT NOT NULL, cidade TEXT NOT NULL, cod_municipio TEXT NOT NULL,
 segmento TEXT NOT NULL, oportunidade TEXT NOT NULL, porte TEXT NOT NULL, abertura TEXT NOT NULL, score INTEGER NOT NULL);
CREATE TABLE meta (chave TEXT PRIMARY KEY, valor TEXT);
CREATE TABLE municipios (cod_municipio TEXT PRIMARY KEY, codigo_ibge TEXT, nome TEXT, uf TEXT, contatos INTEGER, score_medio REAL);
CREATE TABLE segmentos (nome TEXT PRIMARY KEY, contatos INTEGER, score_medio REAL);
CREATE INDEX idx_cnpj ON contatos(cnpj);
CREATE INDEX idx_cidade ON contatos(cidade);
CREATE INDEX idx_segmento ON contatos(segmento);
CREATE INDEX idx_score ON contatos(score DESC);
CREATE INDEX idx_porte ON contatos(porte);
CREATE VIRTUAL TABLE contatos_fts USING fts5(cnpj, cidade, segmento, porte, content='contatos', content_rowid='id', tokenize='unicode61');
'''
TABLE_COLUMNS = {
    'contatos': set(FIELDS), 'meta': {'chave', 'valor'},
    'municipios': {'cod_municipio', 'codigo_ibge', 'nome', 'uf', 'contatos', 'score_medio'},
    'segmentos': {'nome', 'contatos', 'score_medio'},
    'contatos_fts': {'cnpj', 'cidade', 'segmento', 'porte'},
}


def validate_public(conn):
    meta = dict(conn.execute('SELECT chave, valor FROM meta'))
    if meta.get('uf') != 'CE' or meta.get('publicacao_restrita') != '1' or meta.get('politica_publicacao') != POLICY or not meta.get('versao_receita'):
        raise ValueError('O release público exige a política minimizada v2 do Ceará.')
    if conn.execute("SELECT COUNT(*) FROM municipios WHERE uf IS NULL OR uf<>'CE' OR codigo_ibge IS NULL OR codigo_ibge NOT LIKE '23%'").fetchone()[0]:
        raise ValueError('O banco público contém municípios de outra UF ou não verificados.')
    if conn.execute("SELECT COUNT(*) FROM contatos WHERE typeof(cnpj)<>'text' OR length(cnpj)<>14 OR cnpj GLOB '*[^A-Z0-9]*' OR substr(cnpj,13) GLOB '*[^0-9]*'").fetchone()[0]:
        raise ValueError('A publicação exige CNPJs textuais válidos no formato numérico ou alfanumérico.')
    permitted_tables = set(TABLE_COLUMNS) | {'contatos_fts', 'contatos_fts_data', 'contatos_fts_idx', 'contatos_fts_docsize', 'contatos_fts_config'}
    tables = {row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    if tables != permitted_tables:
        raise ValueError('O banco público contém tabelas não autorizadas.')
    for table, columns in TABLE_COLUMNS.items():
        if {r[1] for r in conn.execute(f'PRAGMA table_info({table})')} != columns:
            raise ValueError('O banco público contém campos privados ou esquema antigo.')
