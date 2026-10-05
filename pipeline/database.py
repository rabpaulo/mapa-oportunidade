import sqlite3
from contextlib import contextmanager
from pathlib import Path


@contextmanager
def connection(path: Path):
    if not path.is_file():
        raise ValueError('A base do Ceará ainda não foi preparada.')
    conn = sqlite3.connect(path.as_uri() + '?mode=ro', uri=True, timeout=10)
    try:
        conn.execute('PRAGMA query_only = ON')
        uf = conn.execute("SELECT valor FROM meta WHERE chave='uf'").fetchone()
        if not uf or uf[0] != 'CE':
            raise ValueError('O banco configurado não é uma base do Ceará.')
        yield conn
    finally:
        conn.close()


def validate_database(path: Path):
    with connection(path) as conn:
        if conn.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
            raise ValueError('A nova base falhou na verificação de integridade.')
        if conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0] == 0:
            raise ValueError('A nova base não contém contatos.')
        if conn.execute("SELECT COUNT(*) FROM municipios WHERE uf <> 'CE' OR codigo_ibge IS NULL OR codigo_ibge='' OR codigo_ibge NOT LIKE '23%'").fetchone()[0]:
            raise ValueError('A nova base possui municípios sem correspondência com o Ceará.')
        conn.execute("SELECT rowid FROM contatos_fts WHERE contatos_fts MATCH 'fortaleza*' LIMIT 1").fetchall()
