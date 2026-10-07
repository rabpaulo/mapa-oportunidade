"""Coleta os Dados Abertos CNPJ e publica bases locais por UF."""
import argparse
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from pipeline.jobs import JobManager
from src.geografia import UFS


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dados', type=Path, help='Diretório de dados; padrão: CEARA_DATA_DIR ou data/ do projeto')
    parser.add_argument('--reaproveitar', action='store_true', help='Regenera a base usando os recortes locais, sem baixar o cadastro nacional')
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--uf', nargs='+', type=str.upper, choices=sorted(UFS), default=['CE'])
    group.add_argument('--todos', action='store_true', help='Coleta as 27 UFs numa única passagem nacional')
    args = parser.parse_args(argv)
    if args.dados:
        os.environ['CEARA_DATA_DIR'] = str(args.dados.expanduser().resolve())
    collector = JobManager(log_output=lambda line: print(line, flush=True))
    try:
        collector.run(download=not args.reaproveitar, ufs=sorted(UFS) if args.todos else args.uf)
    except KeyboardInterrupt:
        print('\nColeta interrompida. A base anterior foi preservada.', flush=True)
        return 130
    return 0 if collector.snapshot()['status'] == 'concluido' else 1


if __name__ == '__main__':
    sys.exit(main())
