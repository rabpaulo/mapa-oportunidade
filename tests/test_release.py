import json
import io
import sqlite3
import tarfile
import brotli

import pytest

from scripts.preparar_release import FILES, prepare, sha256
from pipeline.locking import acquire_collection_lock


@pytest.fixture
def release_source(sample_data):
    (sample_data / 'ibge').mkdir()
    for name in FILES[1:]:
        (sample_data / name).write_text(json.dumps({'type': 'FeatureCollection', 'features': []}))
    return sample_data


def test_backup_is_valid_reproducible_and_preserves_source(release_source, tmp_path):
    original = (release_source / FILES[0]).read_bytes()
    first, second = tmp_path / 'release-one', tmp_path / 'release-two'
    manifest = prepare(release_source, first)
    assert manifest == prepare(release_source, second)
    assert manifest['archive_sha256'] == sha256(first / 'snapshot.tar.br')
    assert (release_source / FILES[0]).read_bytes() == original
    with tarfile.open(fileobj=io.BytesIO(brotli.decompress((first / 'snapshot.tar.br').read_bytes()))) as tar:
        assert tar.getnames() == FILES
        for entry in tar:
            path = tmp_path / 'restored' / entry.name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(tar.extractfile(entry).read())
            assert sha256(path) == manifest['files'][entry.name]
    with sqlite3.connect(tmp_path / 'restored' / FILES[0]) as conn:
        assert conn.execute('PRAGMA quick_check').fetchone()[0] == 'ok'
        assert conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0] == 5
    assert not list(tmp_path.glob('.release-*'))


@pytest.mark.parametrize('mode', ['missing-map', 'invalid-map', 'foreign-uf', 'bad-fts', 'existing-output'])
def test_invalid_release_does_not_publish(release_source, tmp_path, mode):
    output = tmp_path / 'release'
    if mode == 'missing-map':
        (release_source / FILES[1]).unlink()
    elif mode == 'invalid-map':
        (release_source / FILES[1]).write_text('{"type":"Polygon"}')
    elif mode in ('foreign-uf', 'bad-fts'):
        with sqlite3.connect(release_source / FILES[0]) as conn:
            conn.execute("UPDATE meta SET valor='SC' WHERE chave='uf'" if mode == 'foreign-uf' else 'DROP TABLE contatos_fts')
    else:
        output.mkdir()
        (output / 'preserved').write_text('previous')
    with pytest.raises((ValueError, sqlite3.Error)):
        prepare(release_source, output)
    if mode == 'existing-output':
        assert (output / 'preserved').read_text() == 'previous'
    else:
        assert not output.exists()
    assert not list(tmp_path.glob('.release-*'))


def test_release_waits_for_collection_publication(release_source, tmp_path):
    with acquire_collection_lock(release_source):
        with pytest.raises(ValueError, match='coleta ou atualização'):
            prepare(release_source, tmp_path / 'blocked-release')
    assert not (tmp_path / 'blocked-release').exists()
