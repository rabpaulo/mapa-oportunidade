"""Cria uma base pública minimizada; nunca modifica o cadastro local original."""
import argparse
import csv
import gzip
import hashlib
import json
import re
import shutil
import sqlite3
import sys
import tempfile
import uuid
from collections import Counter
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src'))
sys.path.insert(0, str(ROOT))
import banco
import cnae
from gerar_leads import _limpar_nome, BONUS_PORTE
from pipeline.database import connection, validate_database
from pipeline.locking import acquire_collection_lock
from pipeline.publication import SCHEMA, FIELDS, POLICY, CORPORATE_NATURES, validate_public
from pipeline.downloads import prepare_downloads

PRIVATE_FIELDS = ('nome', 'empresa', 'email', 'telefone', 'whatsapp', 'endereco', 'bairro', 'dominio_proprio', 'tem_celular')
IDENTIFIER = re.compile(r'(?<!\d)(?:\d{3}[.\s-]?\d{3}[.\s-]?\d{3}[.\s-]?\d{2}|\d{14})(?!\d)|[^\s@]+@[^\s@]+|https?://', re.I)


def suppression_hash(cnpj):
    if not re.fullmatch(r'[A-Z0-9]{12}\d{2}', cnpj):
        raise ValueError('Informe um CNPJ com 14 caracteres, sem pontuação.')
    return hashlib.sha256(cnpj.encode('ascii')).hexdigest()


def load_suppressions(path):
    if not path.exists():
        return set()
    data = json.loads(path.read_text())
    if not isinstance(data, list) or any(not isinstance(x, str) or not re.fullmatch(r'[0-9a-f]{64}', x) for x in data):
        raise ValueError('Lista de supressões inválida.')
    return set(data)


def suppress(source, cnpj):
    with acquire_collection_lock(source):
        path = source / 'privacidade' / 'supressoes.json'
        values = load_suppressions(path)
        values.add(suppression_hash(cnpj))
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix('.tmp')
        temporary.write_text(json.dumps(sorted(values), indent=2) + '\n')
        temporary.replace(path)


def prepare_public(source: Path, output: Path):
    source, output = source.resolve(), output.resolve()
    if output.exists():
        raise ValueError('Escolha um destino novo para a base pública.')
    with acquire_collection_lock(source):
        validate_database(source / 'uf/CE/contatos.db')
        origin = source / 'receita/empresas_ce.csv.gz'
        if not origin.is_file():
            raise ValueError('A origem Empresas é necessária para verificar a natureza jurídica; a publicação foi bloqueada.')
        version_path = source / 'receita/_versao_ce.json'
        version = json.loads(version_path.read_text()) if version_path.exists() else {}
        with connection(source / 'uf/CE/contatos.db') as check:
            database_version = dict(check.execute('SELECT chave, valor FROM meta')).get('versao_receita')
        if not database_version or version.get('versao_receita') != database_version or version.get('uf') != 'CE':
            raise ValueError('A classificação de natureza jurídica deve pertencer à mesma versão CE do banco.')
        types = {}
        with gzip.open(origin, 'rt', encoding='utf-8') as f:
            for row in csv.DictReader(f):
                types[row['cnpj_basico']] = row['natureza']
        suppressed = load_suppressions(source / 'privacidade/supressoes.json')
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='.publica-', dir=output.parent) as temp:
            staging = Path(temp) / 'base'
            db = staging / 'uf/CE/contatos.db'
            conn = banco.abrir(str(db), criar=True)
            counts = Counter()
            with connection(source / 'uf/CE/contatos.db') as original:
                original.row_factory = sqlite3.Row
                metadata = {key: value for key, value in original.execute('SELECT chave, valor FROM meta')
                            if key in ('uf', 'versao_receita', 'baixado_em')}
                cities = {r['cod_municipio']: r['codigo_ibge'] for r in original.execute('SELECT * FROM municipios')}
                conn.executescript(SCHEMA)
                batch = []
                for row in original.execute('SELECT * FROM contatos'):
                    counts['originais'] += 1
                    nature = types.get(row['cnpj'][:8], '')
                    # Publish only recognized corporate entities; EI/MEI and
                    # unknown/public-body/individual classifications stay local.
                    if nature not in CORPORATE_NATURES:
                        counts['natureza_excluida'] += 1
                        continue
                    if suppression_hash(row['cnpj']) in suppressed:
                        counts['suprimidos'] += 1
                        continue
                    year = row['abertura'] or ''
                    age = datetime.now(ZoneInfo('America/Sao_Paulo')).year - int(year) if year.isdigit() else 0
                    # Public score uses business categories, not personal channels.
                    porte_code = {'Microempresa': '01', 'Pequeno porte': '03', 'Medio/grande': '05'}.get(row['porte'], '00')
                    score = min(round(cnae.peso(row['segmento']) * 2.5 + BONUS_PORTE.get(porte_code, 0) + (5 if age >= 3 else 0)), 100)
                    # Every copied string is either a CNPJ or a controlled category.
                    values = {key: row[key] for key in FIELDS}
                    values['score'] = score
                    batch.append(tuple(values[k] for k in FIELDS))
                    counts['publicados'] += 1
                    if len(batch) >= 5000:
                        conn.executemany('INSERT INTO contatos (' + ','.join(FIELDS) + ') VALUES (' + ','.join('?' for _ in FIELDS) + ')', batch)
                        batch.clear()
                if batch:
                    conn.executemany('INSERT INTO contatos (' + ','.join(FIELDS) + ') VALUES (' + ','.join('?' for _ in FIELDS) + ')', batch)
            if not counts['publicados']:
                conn.close()
                raise ValueError('A base pública ficaria vazia.')
            metadata.update(publicacao_restrita='1', politica_publicacao=POLICY, geracao=uuid.uuid4().hex,
                            gerado_em=datetime.now(ZoneInfo('America/Sao_Paulo')).strftime('%Y-%m-%d %H:%M'),
                            recorte='Entidades empresariais, sem empresário individual, nomes, contatos ou endereço; não representa todas as empresas do Ceará.')
            conn.execute("INSERT INTO municipios SELECT cod_municipio,'',cidade,'CE',COUNT(*),AVG(score) FROM contatos GROUP BY cod_municipio,cidade")
            conn.execute('INSERT INTO segmentos SELECT segmento,COUNT(*),AVG(score) FROM contatos GROUP BY segmento')
            conn.execute("INSERT INTO contatos_fts(contatos_fts) VALUES('rebuild')")
            conn.executemany('INSERT INTO meta VALUES (?,?)', [(k, str(v)) for k,v in metadata.items()])
            conn.executemany('UPDATE municipios SET codigo_ibge=? WHERE cod_municipio=?', [(code, city) for city, code in cities.items()])
            conn.commit()
            conn.execute('PRAGMA journal_mode=DELETE')
            validate_public(conn)
            conn.close()
            validate_database(db)
            (staging / 'ibge').mkdir()
            for name in ('malha_23.geojson', 'malha_br.geojson'):
                shutil.copyfile(source / 'ibge' / name, staging / 'ibge' / name)
            prepare_downloads(staging)
            (staging / 'auditoria-publicacao.json').write_text(json.dumps(dict(counts), ensure_ascii=False, indent=2) + '\n')
            shutil.move(staging, output)
    return dict(counts)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dados', type=Path, default=ROOT / 'data')
    parser.add_argument('--saida', type=Path)
    parser.add_argument('--suprimir-cnpj', help='Registra uma supressão persistente para futuras publicações.')
    args = parser.parse_args()
    try:
        if args.suprimir_cnpj:
            suppress(args.dados.resolve(), args.suprimir_cnpj)
            print('Supressão registrada. Gere e publique um novo release para aplicá-la ao site.')
        elif args.saida:
            print(json.dumps(prepare_public(args.dados, args.saida), ensure_ascii=False))
        else:
            parser.error('Informe --saida ou --suprimir-cnpj.')
    except (OSError, ValueError, sqlite3.Error, KeyError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
