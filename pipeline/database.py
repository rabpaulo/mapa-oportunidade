import sqlite3
import unicodedata
from contextlib import contextmanager
from pathlib import Path
from src.geografia import UFS


@contextmanager
def connection(path: Path, expected_uf='CE'):
    if not path.is_file():
        raise ValueError(f'A base de {expected_uf} ainda não foi preparada.')
    conn = sqlite3.connect(path.resolve().as_uri() + '?mode=ro', uri=True, timeout=10)
    try:
        conn.execute('PRAGMA query_only = ON')
        uf = conn.execute("SELECT valor FROM meta WHERE chave='uf'").fetchone()
        if expected_uf not in UFS or not uf or uf[0] != expected_uf:
            raise ValueError(f'O banco configurado não é uma base de {expected_uf}.')
        yield conn
    finally:
        conn.close()


def validate_database(path: Path, expected_uf='CE'):
    with connection(path, expected_uf) as conn:
        if conn.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
            raise ValueError('A nova base falhou na verificação de integridade.')
        if conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0] == 0:
            raise ValueError('A nova base não contém contatos.')
        if conn.execute("SELECT COUNT(*) FROM municipios WHERE uf IS NULL OR uf <> ? OR (codigo_ibge<>'' AND codigo_ibge NOT LIKE ?)", (expected_uf, UFS[expected_uf] + '%')).fetchone()[0]:
            raise ValueError(f'A nova base possui municípios sem correspondência com {expected_uf}.')
        missing = conn.execute("SELECT cod_municipio,nome,contatos FROM municipios WHERE codigo_ibge IS NULL OR codigo_ibge=''").fetchall()
        if missing:
            meta = dict(conn.execute('SELECT chave,valor FROM meta'))
            # A reviewed source inconsistency remains downloadable, never assigned
            # a fabricated coordinate. All other unmapped municipalities fail.
            permitted = expected_uf == 'SC' and meta.get('publicacao_restrita') != '1' and all(
                code == '0403' and ''.join(c for c in unicodedata.normalize('NFKD', name.casefold()) if not unicodedata.combining(c)) == 'acara'
                for code, name, _ in missing)
            if not permitted or meta.get('municipios_sem_malha') != str(len(missing)) or meta.get('contatos_sem_malha') != str(sum(row[2] for row in missing)):
                raise ValueError(f'A nova base possui municípios sem correspondência com {expected_uf}.')
        conn.execute("SELECT rowid FROM contatos_fts WHERE contatos_fts MATCH 'fortaleza*' LIMIT 1").fetchall()
