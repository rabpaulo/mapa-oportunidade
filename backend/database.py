import re
import sqlite3
import time
import unicodedata
from functools import lru_cache
from contextlib import contextmanager
from pathlib import Path

from backend.config import database_path
from backend.models import AnalysisRequest, Filters, SearchRequest


class DatabaseUnavailable(Exception):
    pass


def normalized(text: str) -> str:
    return ''.join(c for c in unicodedata.normalize('NFKD', text) if not unicodedata.combining(c)).lower().strip()


def fts_expression(text: str) -> str:
    return ' '.join('"' + token + '"*' for token in re.findall(r'[a-z0-9]+', normalized(text)))


@lru_cache(maxsize=32)
def category_values(path: str, modified: int, column: str):
    with connection(Path(path)) as conn:
        if column in ('cidade', 'segmento'):
            table = 'municipios' if column == 'cidade' else 'segmentos'
            values = conn.execute(f'SELECT nome FROM {table}')
        else:
            values = conn.execute('SELECT DISTINCT porte FROM contatos')
        return {normalized(r[0]): r[0] for r in values}


def canonical(column, value):
    path = database_path()
    if not path.is_file():
        return value
    return category_values(str(path), path.stat().st_mtime_ns, column).get(normalized(value), value.strip())


@contextmanager
def connection(path: Path | None = None, time_budget: float | None = 15):
    path = path or database_path()
    if not path.is_file():
        raise DatabaseUnavailable('A base do Ceará ainda não foi preparada. Consulte a aba Base.')
    conn = sqlite3.connect(path.as_uri() + '?mode=ro', uri=True, timeout=10)
    conn.row_factory = sqlite3.Row
    conn.create_function('normalizar', 1, lambda v: normalized(v or ''), deterministic=True)
    if time_budget is not None:
        deadline = time.monotonic() + time_budget
        conn.set_progress_handler(lambda: int(time.monotonic() > deadline), 10_000)
    try:
        conn.execute('PRAGMA query_only = ON')
        uf = conn.execute("SELECT valor FROM meta WHERE chave='uf'").fetchone()
        if not uf or uf[0] != 'CE':
            raise DatabaseUnavailable('O banco configurado não é uma base do Ceará.')
        yield conn
    finally:
        conn.close()


def where(filters: Filters, ids: list[int] | None = None):
    """Único compilador de filtros para telas, exportação e ferramentas da IA."""
    clauses, params = [], []
    if ids:
        # JSON evita o limite de parâmetros do SQLite para seleções grandes.
        import json
        return 'contatos c', 'c.id IN (SELECT value FROM json_each(?))', [json.dumps(sorted(set(ids)))]
    source = 'contatos c'
    if filters.termo.strip():
        expression = fts_expression(filters.termo)
        if expression:
            source = 'contatos_fts JOIN contatos c ON c.id=contatos_fts.rowid'
            clauses.append('contatos_fts MATCH ?')
            params.append(expression)
        else:
            clauses.append('0=1')
    for column, value in [('cidade', filters.cidade), ('segmento', filters.segmento), ('porte', filters.porte), ('bairro', filters.bairro)]:
        if value.strip():
            if column == 'bairro':
                clauses.append('normalizar(c.bairro) = ?')
                params.append(normalized(value))
            else:
                clauses.append(f'c.{column} = ?')
                params.append(canonical(column, value))
    for column, values in [('cidade', filters.cidades), ('segmento', filters.segmentos)]:
        if values:
            clauses.append(f"c.{column} IN ({','.join('?' for _ in values)})")
            params.extend(canonical(column, v) for v in values)
    if filters.score_minimo:
        clauses.append('c.score >= ?')
        params.append(filters.score_minimo)
    for year, operator in [(filters.ano_minimo, '>='), (filters.ano_maximo, '<=')]:
        if year is not None:
            clauses.append(f'c.abertura {operator} ?')
            params.append(str(year))
    if filters.somente_celular:
        clauses.append('c.tem_celular = 1')
    if filters.somente_email:
        clauses.append("c.email <> ''")
    if filters.somente_sem_dominio:
        clauses.append("c.email <> '' AND c.dominio_proprio = 0")
    return source, ' AND '.join(clauses) or '1=1', params


ORDERS = {
    'score': 'c.score DESC, c.cidade, c.nome, c.id',
    'nome': 'c.nome, c.id', 'cidade': 'c.cidade, c.score DESC, c.id',
    'recente': 'c.abertura DESC, c.score DESC, c.id',
    'antiga': 'c.abertura, c.score DESC, c.id',
}
AGGREGATES = """COUNT(*) AS contatos,
 SUM(CASE WHEN c.email <> '' THEN 1 ELSE 0 END) AS com_email,
 SUM(c.tem_celular) AS com_celular,
 SUM(CASE WHEN c.email <> '' AND c.dominio_proprio=0 THEN 1 ELSE 0 END) AS sem_dominio,
 COALESCE(ROUND(AVG(c.score), 2), 0) AS score_medio"""


def contact(row):
    result = dict(row)
    result.pop('busca', None)
    result['dominio_proprio'] = bool(result['dominio_proprio'])
    result['tem_celular'] = bool(result['tem_celular'])
    return result


def search(request: SearchRequest):
    source, condition, params = where(request.filtros)
    with connection() as conn:
        total = conn.execute(f'SELECT COUNT(*) FROM {source} WHERE {condition}', params).fetchone()[0]
        rows = conn.execute(f'SELECT c.* FROM {source} WHERE {condition} ORDER BY {ORDERS[request.filtros.ordem]} LIMIT ? OFFSET ?',
                            [*params, request.por_pagina, (request.pagina - 1) * request.por_pagina])
        return {'total': total, 'itens': [contact(r) for r in rows]}


def detail(id: int):
    with connection() as conn:
        row = conn.execute('SELECT * FROM contatos WHERE id=?', [id]).fetchone()
        return contact(row) if row else None


def metadata():
    with connection() as conn:
        meta = dict(conn.execute('SELECT chave, valor FROM meta'))
        counts = dict(conn.execute(f'SELECT {AGGREGATES} FROM contatos c').fetchone())
        meta.update(counts)
        meta['municipios'] = conn.execute('SELECT COUNT(*) FROM municipios').fetchone()[0]
        meta['ramos'] = conn.execute('SELECT COUNT(*) FROM segmentos').fetchone()[0]
        meta['tamanho_mb'] = round(database_path().stat().st_size / 1e6, 1)
        return meta


def areas(kind: str):
    if kind not in ('municipios', 'segmentos'):
        raise ValueError('Agrupamento inválido.')
    with connection() as conn:
        code = 'codigo_ibge' if kind == 'municipios' else 'nome'
        return [dict(r) for r in conn.execute(f'SELECT {code} AS codigo, nome, contatos, com_email, com_celular, sem_dominio, score_medio FROM {kind} ORDER BY contatos DESC, nome')]


def analyze(request: AnalysisRequest):
    source, condition, params = where(request.filtros)
    groups = list(dict.fromkeys(request.agrupar_por))
    columns = ', '.join('c.' + col for col in groups)
    prefix = columns + ', ' if columns else ''
    group = ' GROUP BY ' + columns if columns else ''
    with connection() as conn:
        rows = [dict(r) for r in conn.execute(f'SELECT {prefix}{AGGREGATES} FROM {source} WHERE {condition}{group} ORDER BY {request.ordenar_por} DESC LIMIT ?', [*params, request.limite])]
    for row in rows:
        for metric in ['com_email', 'com_celular', 'sem_dominio']:
            row[metric] = row[metric] or 0
            row['percentual_' + metric] = round(row[metric] * 100 / row['contatos'], 2) if row['contatos'] else 0
    return rows


def facets(filters: Filters):
    result = {}
    for key, group, removed in [('municipios', 'cidade', {'cidade': '', 'cidades': []}), ('segmentos', 'segmento', {'segmento': '', 'segmentos': []})]:
        rows = analyze(AnalysisRequest(filtros=filters.model_copy(update=removed), agrupar_por=[group], limite=200))
        result[key] = [{'codigo': r[group], 'nome': r[group], **{k: v for k, v in r.items() if k != group}} for r in rows]
    return result


def validate_database(path: Path):
    with connection(path) as conn:
        if conn.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
            raise ValueError('A nova base falhou na verificação de integridade.')
        if conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0] == 0:
            raise ValueError('A nova base não contém contatos.')
        if conn.execute("SELECT COUNT(*) FROM municipios WHERE uf <> 'CE' OR codigo_ibge IS NULL OR codigo_ibge='' OR codigo_ibge NOT LIKE '23%'").fetchone()[0]:
            raise ValueError('A nova base possui municípios sem correspondência com o Ceará.')
        conn.execute("SELECT rowid FROM contatos_fts WHERE contatos_fts MATCH 'fortaleza*' LIMIT 1").fetchall()
