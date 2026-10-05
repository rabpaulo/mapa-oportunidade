import fcntl
from pathlib import Path


def acquire_collection_lock(target: Path):
    """Coleta, publicação e release compartilham uma trava entre processos."""
    target.mkdir(parents=True, exist_ok=True)
    guard = (target / '.coleta.lock').open('a')
    try:
        fcntl.flock(guard, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        guard.close()
        raise ValueError('Já existe uma coleta ou atualização em andamento neste diretório de dados.')
    return guard
