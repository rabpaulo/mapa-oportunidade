"""Inicia os dois servidores e encerra seus processos filhos juntos."""
import argparse
import os
import signal
import socket
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--producao', action='store_true', help='Compila e inicia a versão de produção')
    args = parser.parse_args()
    for port in (3000, 8000):
        with socket.socket() as sock:
            # Conexões recém-encerradas em TIME_WAIT não ocupam o servidor.
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            try:
                sock.bind(('127.0.0.1', port))
            except OSError:
                print(f'A porta {port} já está em uso. Encerre o outro servidor antes de iniciar.', file=sys.stderr)
                return 1
    if args.producao:
        subprocess.run(['npm', 'run', 'build'], cwd=ROOT / 'app', check=True)
    children = []
    def stop(signum=None, frame=None):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, stop)
    try:
        children.append(subprocess.Popen([sys.executable, '-m', 'uvicorn', 'backend.main:app', '--host', '127.0.0.1', '--port', '8000'], cwd=ROOT, start_new_session=True))
        children.append(subprocess.Popen(['npm', 'run', 'start' if args.producao else 'dev'], cwd=ROOT / 'app', start_new_session=True))
        print('\nMapa de Oportunidades Ceará: http://localhost:3000\nCtrl+C encerra os dois serviços.\n', flush=True)
        while all(child.poll() is None for child in children):
            time.sleep(.4)
        return next((child.returncode or 0 for child in children if child.poll() is not None), 0)
    except KeyboardInterrupt:
        return 0
    finally:
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        for child in children:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGTERM)
        for child in children:
            try:
                child.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGKILL)
                child.wait()


if __name__ == '__main__':
    sys.exit(main())
