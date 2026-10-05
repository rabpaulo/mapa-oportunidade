"""Coleta os Dados Abertos CNPJ pelo mesmo pipeline do Garimpo, somente CE."""
import argparse
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from backend.jobs import JobManager


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dados', type=Path, help='Diretório de dados; padrão: CEARA_DATA_DIR ou data/ do projeto')
    parser.add_argument('--reaproveitar', action='store_true', help='Regenera a base usando os recortes locais, sem baixar o cadastro nacional')
    args = parser.parse_args(argv)
    if args.dados:
        os.environ['CEARA_DATA_DIR'] = str(args.dados.expanduser().resolve())
    collector = JobManager(log_output=lambda line: print(line, flush=True))
    try:
        collector.run(download=not args.reaproveitar)
    except KeyboardInterrupt:
        print('\nColeta interrompida. A base anterior foi preservada.', flush=True)
        return 130
    return 0 if collector.snapshot()['status'] == 'concluido' else 1


if __name__ == '__main__':
    sys.exit(main())
