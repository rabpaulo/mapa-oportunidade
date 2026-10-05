import shutil
import sqlite3
import subprocess
import sys
from pathlib import Path

from scripts.importar_base import import_base


def test_import_cli_requires_explicit_source():
    root = Path(__file__).resolve().parents[1]
    result = subprocess.run([sys.executable, 'scripts/importar_base.py'], cwd=root, capture_output=True, text=True)
    assert result.returncode == 2
    assert '--origem' in result.stderr


def test_explicit_import_is_independent_and_never_overwrites(sample_data, tmp_path):
    source = tmp_path / 'source'
    original = source / 'data' / 'uf' / 'CE' / 'contatos.db'
    original.parent.mkdir(parents=True)
    shutil.copy2(sample_data / 'uf' / 'CE' / 'contatos.db', original)
    before = original.read_bytes()
    target = tmp_path / 'imported'
    import_base(source, target)
    destination = target / 'uf' / 'CE' / 'contatos.db'
    assert original.stat().st_ino != destination.stat().st_ino
    with sqlite3.connect(destination) as conn:
        assert conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0] == 5
        assert conn.execute("SELECT valor FROM meta WHERE chave='uf'").fetchone()[0] == 'CE'
        conn.execute('DELETE FROM contatos WHERE id=1')
    import_base(source, target)
    with sqlite3.connect(destination) as conn:
        assert conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0] == 4
    assert original.read_bytes() == before
