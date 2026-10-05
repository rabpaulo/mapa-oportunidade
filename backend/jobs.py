import copy
import fcntl
import os
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import threading
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

from backend.config import ROOT, data_dir
from backend.database import validate_database


class JobManager:
    def __init__(self, log_output=None):
        self.lock = threading.Lock()
        self.log_output = log_output
        self.state = {'status': 'ocioso', 'linhas': [], 'iniciado_em': None, 'baixar_cadastro': False}

    def snapshot(self):
        with self.lock:
            return copy.deepcopy(self.state)

    def log(self, line):
        with self.lock:
            self.state['linhas'] = (self.state['linhas'] + [line])[-500:]
        if self.log_output:
            self.log_output(line)

    def acquire_collection_lock(self):
        """A coleta pelo terminal e pelo navegador compartilha a mesma trava."""
        target = data_dir()
        target.mkdir(parents=True, exist_ok=True)
        guard = (target / '.coleta.lock').open('a')
        try:
            fcntl.flock(guard, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            guard.close()
            raise ValueError('Já existe uma coleta ou atualização em andamento neste diretório de dados.')
        return guard

    @staticmethod
    def initial_state(download):
        return {'status': 'rodando', 'linhas': [], 'iniciado_em': datetime.now(ZoneInfo('America/Sao_Paulo')).isoformat(), 'baixar_cadastro': download}

    def start(self, download: bool):
        with self.lock:
            if self.state['status'] == 'rodando':
                raise ValueError('Já existe uma atualização em andamento.')
            guard = self.acquire_collection_lock()
            self.state = self.initial_state(download)
        try:
            threading.Thread(target=self.run, args=(download, guard), daemon=True).start()
        except Exception:
            guard.close()
            with self.lock:
                self.state['status'] = 'erro'
            raise
        return self.snapshot()

    def command(self, script, staging: Path):
        env = os.environ.copy()
        env['TMP'] = str(staging)
        env['TEMP'] = str(staging)
        env['TZ'] = 'America/Sao_Paulo'
        env['PYTHONIOENCODING'] = 'utf-8'
        with subprocess.Popen([sys.executable, '-u', str(ROOT / 'src' / script), '--uf', 'CE', '--dados', str(staging)],
                              cwd=staging, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace') as process:
            try:
                for line in process.stdout:
                    self.log(line.rstrip())
                if process.wait() != 0:
                    raise RuntimeError('O processamento falhou. Veja o log; a base anterior foi preservada.')
            except BaseException:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
                raise

    def run(self, download: bool, guard=None):
        target = data_dir()
        try:
            if guard is None:
                guard = self.acquire_collection_lock()
                with self.lock:
                    self.state = self.initial_state(download)
            with tempfile.TemporaryDirectory(prefix='.atualizacao-', dir=target) as name:
                staging = Path(name)
                # Sem baixar, os recortes locais são suficientes para regenerar.
                for folder in ['receita', 'ibge']:
                    if (target / folder).exists():
                        shutil.copytree(target / folder, staging / folder)
                if download:
                    self.log('Baixando o cadastro nacional e preservando somente o Ceará…')
                    self.command('ingestar_receita.py', staging)
                elif not (staging / 'receita' / 'estabelecimentos_ce.csv.gz').exists():
                    raise ValueError('Não há cadastro local. Marque “Baixar novo cadastro”.')
                self.log('Gerando a base em uma pasta temporária…')
                self.command('gerar_leads.py', staging)
                self.publish(staging, target)
            self.log('Atualização concluída. A nova base do Ceará está disponível.')
            with self.lock:
                self.state['status'] = 'concluido'
        except Exception as error:
            self.log(str(error))
            with self.lock:
                self.state['status'] = 'erro'
        finally:
            if guard is not None:
                guard.close()

    def publish(self, staging: Path, target: Path):
        new_db = staging / 'uf' / 'CE' / 'contatos.db'
        # DELETE permite trocar um único arquivo atomicamente, sem WAL/SHM
        # compartilhados com conexões que ainda leem a geração anterior.
        with sqlite3.connect(new_db) as conn:
            conn.execute('PRAGMA wal_checkpoint(TRUNCATE)')
            conn.execute('PRAGMA journal_mode=DELETE')
        validate_database(new_db)
        self.log('Integridade e municípios verificados. Publicando a nova geração…')
        destination = target / 'uf' / 'CE' / 'contatos.db'
        destination.parent.mkdir(parents=True, exist_ok=True)
        for folder in ['receita', 'ibge']:
            (target / folder).mkdir(parents=True, exist_ok=True)
            for file in (staging / folder).glob('*'):
                if file.is_file():
                    os.replace(file, target / folder / file.name)
        report = new_db.parent / '_INDICE.md'
        if report.exists():
            os.replace(report, destination.parent / report.name)
        os.replace(new_db, destination)


jobs = JobManager()
