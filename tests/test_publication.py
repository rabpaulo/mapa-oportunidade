import csv
import gzip
import json
import sqlite3

import pytest

from gerar_leads import _limpar_nome
from scripts.preparar_base_publica import prepare_public, suppress, suppression_hash, IDENTIFIER, PRIVATE_FIELDS
from scripts.preparar_release import FILES


def public_source(source):
    (source / 'receita').mkdir()
    with sqlite3.connect(source / FILES[0]) as conn:
        version = dict(conn.execute('SELECT chave,valor FROM meta'))['versao_receita']
    (source / 'receita/_versao_ce.json').write_text(json.dumps({'uf': 'CE', 'versao_receita': version}))
    with gzip.open(source / 'receita/empresas_ce.csv.gz', 'wt', encoding='utf-8', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=['cnpj_basico', 'natureza'])
        writer.writeheader()
        writer.writerows([{'cnpj_basico': str(i) * 8, 'natureza': '2135' if i == 1 else '2062'} for i in range(1, 5)])
    (source / 'ibge').mkdir()
    for name in FILES[1:]:
        (source / name).write_text(json.dumps({'type': 'FeatureCollection', 'features': []}))
    return source


@pytest.mark.parametrize('name,expected', [
    ('123.456.789-09 Empresa Ltda', 'Empresa Ltda'),
    ('Empresa 12345678909 Comercial', 'Empresa Comercial'),
    ('Empresa 123.456.789-09', 'Empresa'),
    ('12345678909', ''),
    ('Empresa 123\u200b45678909 Ltda', 'Empresa Ltda'),
    ('Loja 24 Horas', 'Loja 24 Horas'),
])
def test_cpf_is_removed_anywhere_without_restoring_original(name, expected):
    assert _limpar_nome(name) == expected


def test_publication_excludes_ei_unknown_and_suppressions_and_preserves_source(sample_data, tmp_path):
    source = public_source(sample_data)
    db = source / FILES[0]
    with sqlite3.connect(db) as conn:
        conn.execute("UPDATE contatos SET nome='Empresa 123.456.789-09 Revisada', empresa='Empresa Revisada Ltda' WHERE id=2")
    original = db.read_bytes()
    suppress(source, '33333333000100')
    output = tmp_path / 'public-one'
    stats = prepare_public(source, output)
    assert stats['publicados'] == 2
    assert stats['suprimidos'] == 1
    assert stats['natureza_excluida'] == 2  # EI and unclassified source.
    assert db.read_bytes() == original
    with sqlite3.connect(output / FILES[0]) as conn:
        conn.row_factory = sqlite3.Row
        rows = conn.execute('SELECT * FROM contatos').fetchall()
        assert {r['cnpj'] for r in rows} == {'22222222000100', '44444444000100'}
        for row in rows:
            assert all(field not in row.keys() for field in PRIVATE_FIELDS)
            assert len(row.keys()) == 9
        assert conn.execute("SELECT valor FROM meta WHERE chave='publicacao_restrita'").fetchone()[0] == '1'
        assert conn.execute("SELECT COUNT(*) FROM contatos_fts WHERE contatos_fts MATCH '22222222*'").fetchone()[0] == 1
    second = tmp_path / 'public-two'
    assert prepare_public(source, second)['suprimidos'] == 1


def test_missing_classification_or_invalid_suppression_fails_closed(sample_data, tmp_path):
    public_source(sample_data)
    (sample_data / 'receita/empresas_ce.csv.gz').unlink()
    with pytest.raises(ValueError, match='natureza jurídica'):
        prepare_public(sample_data, tmp_path / 'missing')
    assert not (tmp_path / 'missing').exists()
    with pytest.raises(ValueError):
        suppression_hash('invalid')


def test_invalid_suppression_file_does_not_publish(sample_data, tmp_path):
    public_source(sample_data)
    (sample_data / 'privacidade').mkdir()
    (sample_data / 'privacidade/supressoes.json').write_text('["invalid"]')
    with pytest.raises(ValueError, match='supressões'):
        prepare_public(sample_data, tmp_path / 'invalid')
    assert not (tmp_path / 'invalid').exists()


def test_classification_from_a_different_version_is_rejected(sample_data, tmp_path):
    public_source(sample_data)
    (sample_data / 'receita/_versao_ce.json').write_text(json.dumps({'uf': 'CE', 'versao_receita': 'old'}))
    with pytest.raises(ValueError, match='mesma versão'):
        prepare_public(sample_data, tmp_path / 'stale')
    assert not (tmp_path / 'stale').exists()
