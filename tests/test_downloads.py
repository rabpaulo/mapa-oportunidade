import csv
import gzip
import json
import sqlite3
import shutil
import pytest
from pipeline.downloads import prepare_downloads, sha256
from pipeline.publication import FIELDS, validate_public
from pipeline.database import validate_database
from scripts.preparar_base_publica import prepare_public
from test_publication import public_source


def test_download_catalog_hashes_rows_and_immutable_generation(sample_data):
    output = prepare_downloads(sample_data)
    catalog = json.loads((output / 'catalogo.json').read_text())
    assert {x['id'] for x in catalog} == {'banco', 'contatos', 'municipios', 'segmentos', 'metadados'}
    for item in catalog:
        path = output / item['arquivo']
        assert item['sha256'] == sha256(path)
        assert item['tamanho_bytes'] == path.stat().st_size
    with gzip.open(output / 'contatos.csv.gz', 'rt') as f:
        rows = list(csv.DictReader(f))
    assert len(rows) == 5
    assert rows[0]['cnpj'] == '11111111000100'
    assert rows[0]['email'] == 'padaria@gmail.com'
    assert prepare_downloads(sample_data) == output
    with sqlite3.connect(sample_data / 'uf/CE/contatos.db') as conn:
        conn.execute("INSERT OR REPLACE INTO meta VALUES('geracao','new-generation')")
    assert prepare_downloads(sample_data) != output
    assert (output / 'contatos.db').exists()


def test_public_downloads_omit_private_columns_and_personal_search_text(sample_data, tmp_path):
    public_source(sample_data)
    output = tmp_path / 'public'
    prepare_public(sample_data, output)
    bundle = prepare_downloads(output)
    with sqlite3.connect(bundle / 'contatos.db') as conn:
        validate_public(conn)
        assert {r[1] for r in conn.execute('PRAGMA table_info(contatos)')} == set(FIELDS)
        assert conn.execute("SELECT COUNT(*) FROM contatos_fts WHERE contatos_fts MATCH 'sertao*'").fetchone()[0] == 0
    with gzip.open(bundle / 'contatos.csv.gz', 'rt') as f:
        reader = csv.DictReader(f)
        assert reader.fieldnames == list(FIELDS)
        assert len(list(reader)) == 3
    with sqlite3.connect(output / 'uf/CE/contatos.db') as conn:
        conn.execute('CREATE TABLE socios(nome TEXT)')
        with pytest.raises(ValueError):
            validate_public(conn)


def test_reviewed_geographic_issue_is_preserved_and_requires_explicit_metadata(sample_data):
    path = sample_data / 'uf/SC/contatos.db'
    path.parent.mkdir()
    shutil.copyfile(sample_data / 'uf/CE/contatos.db', path)
    with sqlite3.connect(path) as conn:
        conn.execute("UPDATE meta SET valor='SC' WHERE chave='uf'")
        conn.execute("UPDATE municipios SET uf='SC',codigo_ibge='4205407'")
        conn.execute("UPDATE municipios SET cod_municipio='0403',nome='Acara',codigo_ibge='' WHERE nome='Fortaleza'")
        conn.execute("UPDATE contatos SET cidade='Acara',cod_municipio='0403' WHERE cidade='Fortaleza'")
    with pytest.raises(ValueError, match='sem correspondência'):
        validate_database(path, 'SC')
    with sqlite3.connect(path) as conn:
        conn.executemany('INSERT OR REPLACE INTO meta VALUES(?,?)', [('municipios_sem_malha','1'),('contatos_sem_malha','3')])
    validate_database(path, 'SC')
    bundle = prepare_downloads(sample_data, 'SC')
    with gzip.open(bundle / 'contatos.csv.gz', 'rt') as file:
        rows = list(csv.DictReader(file))
        assert len(rows) == 5
        assert sum(row['cidade'] == 'Acara' for row in rows) == 3
    with sqlite3.connect(path) as conn:
        assert conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0] == 5
        conn.execute("UPDATE municipios SET nome='Outro município' WHERE cod_municipio='0403'")
    with pytest.raises(ValueError, match='sem correspondência'):
        validate_database(path, 'SC')


def test_public_snapshot_rejects_personal_identifier_in_the_cnpj_field(sample_data, tmp_path):
    public_source(sample_data)
    output = tmp_path / 'public'
    prepare_public(sample_data, output)
    with sqlite3.connect(output / 'uf/CE/contatos.db') as conn:
        conn.execute("UPDATE contatos SET cnpj='12345678901'")
        with pytest.raises(ValueError, match='CNPJs textuais'):
            validate_public(conn)


@pytest.mark.parametrize('sql', [
    "UPDATE meta SET valor='SP' WHERE chave='uf'",
    "UPDATE meta SET valor='politica-antiga' WHERE chave='politica_publicacao'",
    "UPDATE municipios SET uf='SP',codigo_ibge='3550308'",
])
def test_public_database_rejects_other_states_and_old_policies(sample_data, tmp_path, sql):
    public_source(sample_data)
    output = tmp_path / 'public'
    prepare_public(sample_data, output)
    with sqlite3.connect(output / 'uf/CE/contatos.db') as conn:
        conn.execute(sql)
        with pytest.raises(ValueError):
            validate_public(conn)


def test_public_search_index_cannot_contain_name_columns(sample_data, tmp_path):
    public_source(sample_data)
    output = tmp_path / 'public'
    prepare_public(sample_data, output)
    with sqlite3.connect(output / 'uf/CE/contatos.db') as conn:
        conn.execute('DROP TABLE contatos_fts')
        conn.execute("CREATE VIRTUAL TABLE contatos_fts USING fts5(nome,content='contatos',content_rowid='id')")
        with pytest.raises(ValueError, match='campos privados'):
            validate_public(conn)
