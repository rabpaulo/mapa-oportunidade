"""Importa uma base CE compatível sem modificar o banco de origem."""
import argparse
import json
import shutil
import sqlite3
from pathlib import Path


def import_base(source: Path, target: Path):
    original = source / 'data' / 'uf' / 'CE' / 'contatos.db'
    destination = target / 'uf' / 'CE' / 'contatos.db'
    if destination.exists():
        print('A base do Ceará já existe; nenhum dado foi sobrescrito.')
        return
    if not original.is_file():
        raise SystemExit(f'Base CE não encontrada em {original}. Execute scripts/coletar.py para baixar o cadastro.')
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix('.importando')
    try:
        with sqlite3.connect(original.as_uri() + '?mode=ro', uri=True) as src:
            if src.execute("SELECT valor FROM meta WHERE chave='uf'").fetchone() != ('CE',):
                raise ValueError('O banco de origem não é do Ceará.')
            with sqlite3.connect(temporary) as dst:
                src.backup(dst)
                dst.execute('PRAGMA journal_mode=DELETE')
        for folder in ['receita', 'ibge']:
            (target / folder).mkdir(parents=True, exist_ok=True)
        for filename in ['estabelecimentos_ce.csv.gz', 'empresas_ce.csv.gz', '_versao_ce.json', 'municipios.csv.gz', 'cnaes.csv.gz']:
            origin = source / 'data' / 'receita' / filename
            if origin.is_file():
                shutil.copy2(origin, target / 'receita' / filename)
        for filename in ['estados.json', 'municipios_CE.json', 'malha_23.geojson', 'malha_br.geojson']:
            origin = source / 'data' / 'ibge' / filename
            if origin.is_file():
                shutil.copy2(origin, target / 'ibge' / filename)
        report = original.parent / '_INDICE.md'
        if report.is_file():
            shutil.copy2(report, destination.parent / report.name)
        temporary.replace(destination)
        with sqlite3.connect(destination.as_uri() + '?mode=ro', uri=True) as conn:
            print(json.dumps(dict(conn.execute('SELECT chave, valor FROM meta')), ensure_ascii=False, indent=2))
    finally:
        temporary.unlink(missing_ok=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--origem', type=Path, required=True, help='Diretório contendo data/uf/CE/contatos.db')
    parser.add_argument('--destino', type=Path, default=Path(__file__).resolve().parents[1] / 'data')
    args = parser.parse_args()
    import_base(args.origem.expanduser().resolve(), args.destino.expanduser().resolve())
