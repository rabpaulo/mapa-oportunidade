import shutil
import sqlite3

import pytest

from pipeline.database import validate_database
from pipeline.jobs import JobManager


def test_failed_generation_preserves_previous_database(sample_data, monkeypatch):
    path = sample_data / 'uf' / 'CE' / 'contatos.db'
    before = path.read_bytes()
    manager = JobManager()
    (sample_data / 'receita').mkdir()
    (sample_data / 'receita' / 'estabelecimentos_ce.csv.gz').touch()
    def fail(*args): raise RuntimeError('Falha simulada de processamento')
    monkeypatch.setattr(manager, 'command', fail)
    manager.run(False)
    assert manager.snapshot()['status'] == 'erro'
    assert path.read_bytes() == before
    assert not list(sample_data.glob('.atualizacao-*'))


def test_invalid_new_database_is_not_published(sample_data, tmp_path):
    path = sample_data / 'uf' / 'CE' / 'contatos.db'
    before = path.read_bytes()
    staging = tmp_path / 'staging'
    (staging / 'uf' / 'CE').mkdir(parents=True)
    new = staging / 'uf' / 'CE' / 'contatos.db'
    shutil.copy2(path, new)
    with sqlite3.connect(new) as conn:
        conn.execute("UPDATE municipios SET codigo_ibge='' WHERE nome='Fortaleza'")
    with pytest.raises(ValueError):
        JobManager().publish(staging, sample_data)
    assert path.read_bytes() == before


def test_atomic_publication_and_readers_keep_old_snapshot(sample_data, tmp_path):
    path = sample_data / 'uf' / 'CE' / 'contatos.db'
    staging = tmp_path / 'new-generation'
    (staging / 'uf' / 'CE').mkdir(parents=True)
    for folder in ['receita', 'ibge']: (staging / folder).mkdir()
    new = staging / 'uf' / 'CE' / 'contatos.db'
    shutil.copy2(path, new)
    with sqlite3.connect(new) as conn:
        conn.execute("UPDATE meta SET valor='2026-10-01' WHERE chave='versao_receita'")
    with sqlite3.connect(path.as_uri() + '?mode=ro', uri=True) as reader:
        reader.execute('BEGIN')
        assert reader.execute("SELECT valor FROM meta WHERE chave='versao_receita'").fetchone()[0] == '2026-09-14'
        JobManager().publish(staging, sample_data)
        assert reader.execute("SELECT valor FROM meta WHERE chave='versao_receita'").fetchone()[0] == '2026-09-14'
    with sqlite3.connect(path.as_uri() + '?mode=ro', uri=True) as reader:
        assert reader.execute("SELECT valor FROM meta WHERE chave='versao_receita'").fetchone()[0] == '2026-10-01'
    validate_database(path)


def test_duplicate_job_is_rejected(sample_data):
    manager = JobManager()
    manager.state['status'] = 'rodando'
    with pytest.raises(ValueError): manager.start(False)
