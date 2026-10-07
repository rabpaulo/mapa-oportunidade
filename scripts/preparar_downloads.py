"""Prepara downloads imutáveis dos bancos existentes, sem reprocessar a Receita."""
import argparse
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from pipeline.config import data_dir
from pipeline.downloads import prepare_downloads
from pipeline.locking import acquire_collection_lock
from src.geografia import UFS

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dados', type=Path, default=data_dir())
    parser.add_argument('--uf', nargs='+', type=str.upper, choices=sorted(UFS), default=['CE'])
    args = parser.parse_args()
    with acquire_collection_lock(args.dados):
        for uf in dict.fromkeys(args.uf):
            print(prepare_downloads(args.dados.resolve(), uf))
