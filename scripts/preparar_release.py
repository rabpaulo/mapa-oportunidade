"""Prepara um snapshot consistente da base CE, sem alterar os dados originais."""
import argparse
import brotli
import hashlib
import json
import shutil
import sqlite3
import sys
import tarfile
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from pipeline.config import data_dir
from pipeline.database import validate_database
from pipeline.locking import acquire_collection_lock
from pipeline.downloads import prepare_downloads
from pipeline.publication import validate_public

FILES = ['uf/CE/contatos.db', 'ibge/malha_23.geojson', 'ibge/malha_br.geojson']


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def prepare(source: Path, output: Path, public_only=False):
    if not source.is_dir():
        raise ValueError('Diretório de dados ausente.')
    with acquire_collection_lock(source):
        return _prepare(source, output, public_only)


def _prepare(source: Path, output: Path, public_only=False):
    validate_database(source / FILES[0])
    if output.exists():
        raise ValueError('O destino do release já existe. Escolha um novo diretório.')
    for name in FILES:
        if not (source / name).is_file():
            raise ValueError(f'Arquivo da base ausente: {name}')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.release-', dir=output.parent) as temporary:
        staging = Path(temporary)
        database = staging / 'data' / FILES[0]
        database.parent.mkdir(parents=True)
        with sqlite3.connect((source / FILES[0]).as_uri() + '?mode=ro', uri=True) as original:
            with sqlite3.connect(database) as backup:
                original.backup(backup)
                backup.execute('PRAGMA journal_mode=DELETE')
        validate_database(database)
        with sqlite3.connect(database.as_uri() + '?mode=ro', uri=True) as conn:
            metadata = dict(conn.execute('SELECT chave, valor FROM meta'))
            if public_only or metadata.get('publicacao_restrita') == '1':
                validate_public(conn)
        for name in FILES[1:]:
            destination = staging / 'data' / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            content = json.loads((source / name).read_text())
            if not isinstance(content, dict) or content.get('type') != 'FeatureCollection' or not isinstance(content.get('features'), list):
                raise ValueError(f'Malha GeoJSON inválida: {name}')
            shutil.copyfile(source / name, destination)
        bundle = prepare_downloads(staging / 'data')
        files = FILES + [str(path.relative_to(staging / 'data')) for path in sorted(bundle.iterdir())]
        release = staging / 'release'
        release.mkdir()
        archive = release / 'snapshot.tar.br'
        # Stable archive metadata makes repeating the same release reproducible.
        tar_path = staging / 'snapshot.tar'
        with tar_path.open('wb') as raw:
            with tarfile.open(fileobj=raw, mode='w', format=tarfile.USTAR_FORMAT) as tar:
                for name in files:
                    path = staging / 'data' / name
                    entry = tarfile.TarInfo(name)
                    entry.size = path.stat().st_size
                    entry.mode = 0o440
                    with path.open('rb') as content:
                        tar.addfile(entry, content)
        compressor = brotli.Compressor(quality=9)
        with tar_path.open('rb') as raw, archive.open('wb') as compressed:
            while chunk := raw.read(1024 * 1024):
                compressed.write(compressor.process(chunk))
            compressed.write(compressor.finish())
        if not metadata.get('versao_receita') or not metadata.get('gerado_em'):
            raise ValueError('A base precisa informar versão e data de geração.')
        manifest = {
            'url': None,
            'version': metadata['versao_receita'] + '/' + metadata['gerado_em'],
            'archive_sha256': sha256(archive),
            'files': {name: sha256(staging / 'data' / name) for name in files},
        }
        (release / 'snapshot.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
        shutil.move(str(release), output)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dados', type=Path, default=data_dir())
    parser.add_argument('--saida', type=Path, required=True)
    parser.add_argument('--fixar', action='store_true', help='Fixa manifesto e arquivo local para o próximo build do container')
    parser.add_argument('--publico', action='store_true', help='Exige esquema público minimizado v2')
    args = parser.parse_args()
    try:
        manifest = prepare(args.dados.expanduser().resolve(), args.saida.expanduser().resolve(), public_only=args.publico or args.fixar)
        if args.fixar:
            pin(args.saida.expanduser().resolve())
    except (ValueError, sqlite3.Error, OSError) as error:
        print(str(error), file=sys.stderr)
        return 1
    print(f"Release preparado: {args.saida} — {manifest['version']}")
    return 0


def pin(release: Path):
    manifest = json.loads((release / 'snapshot.json').read_text())
    if sha256(release / 'snapshot.tar.br') != manifest['archive_sha256']:
        raise ValueError('Checksum do snapshot inválido.')
    deployment = ROOT / 'deployment'
    (deployment / 'snapshot-input').mkdir(parents=True, exist_ok=True)
    # Keep the compressed artifact out of Git; only its version and hashes are tracked.
    shutil.copyfile(release / 'snapshot.tar.br', deployment / 'snapshot-input' / 'snapshot.tar.br')
    (deployment / 'snapshot-input' / 'snapshot.tar.gz').unlink(missing_ok=True)
    (deployment / 'snapshot.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')


if __name__ == '__main__':
    sys.exit(main())
