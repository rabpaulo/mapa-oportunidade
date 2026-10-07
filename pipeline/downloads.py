"""Arquivos imutáveis por geração; catálogo sem caminhos fornecidos pelo cliente."""
import csv
import gzip
import hashlib
import json
import io
import shutil
import sqlite3
import tempfile
from pathlib import Path
from pipeline.database import connection
from pipeline.publication import validate_public
from src.geografia import UFS


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def generation(meta):
    return hashlib.sha256((meta['versao_receita'] + '/' + meta['gerado_em'] + '/' + meta.get('geracao', '')).encode()).hexdigest()[:24]


def prepare_downloads(source: Path, uf='CE'):
    db = source / 'uf' / uf / 'contatos.db'
    with connection(db, uf) as conn:
        meta = dict(conn.execute('SELECT chave, valor FROM meta'))
    key = generation(meta)
    parent = db.parent / 'downloads'
    output = parent / key
    if output.exists():
        return output
    parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.downloads-', dir=parent) as name:
        staging = Path(name)
        with sqlite3.connect(db.resolve().as_uri() + '?mode=ro', uri=True) as original:
            with sqlite3.connect(staging / 'contatos.db') as backup:
                original.backup(backup)
                backup.execute('PRAGMA journal_mode=DELETE')
        descriptors = []
        with connection(staging / 'contatos.db', uf) as conn:
            if meta.get('publicacao_restrita') == '1':
                validate_public(conn)
            for table in ('contatos', 'municipios', 'segmentos'):
                cursor = conn.execute(f'SELECT * FROM {table} ORDER BY ' + ('id' if table == 'contatos' else 'nome'))
                fields = [d[0] for d in cursor.description if d[0] != 'busca' or meta.get('publicacao_restrita') != '1']
                with (staging / f'{table}.csv.gz').open('wb') as raw, gzip.GzipFile(fileobj=raw, mode='wb', filename='', mtime=0, compresslevel=6) as compressed, io.TextIOWrapper(compressed, encoding='utf-8', newline='') as file:
                    writer = csv.writer(file)
                    writer.writerow(fields)
                    count = 0
                    all_fields = [d[0] for d in cursor.description]
                    positions = [all_fields.index(f) for f in fields]
                    for row in cursor:
                        # CSV opened by spreadsheet software must not execute cells.
                        writer.writerow([("'" + str(row[p])) if isinstance(row[p], str) and row[p].lstrip().startswith(('=', '+', '-', '@')) else row[p] for p in positions])
                        count += 1
                descriptors.append((table, f'{table}.csv.gz', 'csv.gz', count))
            total = conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0]
        descriptors.append(('banco', 'contatos.db', 'sqlite', total))
        (staging / 'metadados.json').write_text(json.dumps(meta, ensure_ascii=False, indent=2) + '\n')
        descriptors.append(('metadados', 'metadados.json', 'json', 1))
        for id_, filename in [('malha', f'malha_{UFS[uf]}.geojson'), ('brasil', 'malha_br.geojson')]:
            origin = source / 'ibge' / filename
            if origin.exists():
                shutil.copyfile(origin, staging / filename)
                geo = json.loads(origin.read_text())
                descriptors.append((id_, filename, 'geojson', len(geo.get('features', []))))
        catalog = [{'id': id_, 'arquivo': filename, 'uf': uf, 'versao': meta['versao_receita'] + '/' + meta['gerado_em'],
                    'geracao': key, 'formato': fmt, 'registros': count, 'tamanho_bytes': (staging / filename).stat().st_size,
                    'sha256': sha256(staging / filename)} for id_, filename, fmt, count in descriptors]
        (staging / 'catalogo.json').write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + '\n')
        staging.rename(output)
    return output
