import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[1]
load_dotenv(ROOT / '.env')


def data_dir() -> Path:
    return Path(os.getenv('CEARA_DATA_DIR', str(ROOT / 'data'))).expanduser().resolve()


def database_path() -> Path:
    return data_dir() / 'uf' / 'CE' / 'contatos.db'
