import copy
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

from pipeline.config import ROOT, data_dir
from pipeline.database import validate_database
from pipeline.locking import acquire_collection_lock
from pipeline.downloads import prepare_downloads
from src.geografia import normalize_ufs


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
        """Todas as coletas locais compartilham a mesma trava de publicação."""
        return acquire_collection_lock(data_dir())

    @staticmethod
    def initial_state(download):
        return {'status': 'rodando', 'linhas': [], 'iniciado_em': datetime.now(ZoneInfo('America/Sao_Paulo')).isoformat(), 'baixar_cadastro': download}

    def start(self, download: bool, ufs=('CE',)):
        ufs = normalize_ufs(ufs)
        with self.lock:
            if self.state['status'] == 'rodando':
                raise ValueError('Já existe uma atualização em andamento.')
            guard = self.acquire_collection_lock()
            self.state = self.initial_state(download)
        try:
            threading.Thread(target=self.run, args=(download, guard, ufs), daemon=True).start()
        except Exception:
            guard.close()
            with self.lock:
                self.state['status'] = 'erro'
            raise
        return self.snapshot()

    def command(self, script, staging: Path, ufs=('CE',)):
        env = os.environ.copy()
        env['TMP'] = str(staging)
        env['TEMP'] = str(staging)
        env['TZ'] = 'America/Sao_Paulo'
        env['PYTHONIOENCODING'] = 'utf-8'
        with subprocess.Popen([sys.executable, '-u', str(ROOT / 'src' / script), '--uf', *ufs, '--dados', str(staging)],
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

    def run(self, download: bool, guard=None, ufs=('CE',)):
        ufs = normalize_ufs(ufs)
        target = data_dir()
        try:
            if guard is None:
                guard = self.acquire_collection_lock()
                with self.lock:
                    self.state = self.initial_state(download)
            with tempfile.TemporaryDirectory(prefix='.atualizacao-', dir=target) as name:
                staging = Path(name)
                if (target / 'ibge').exists():
                    shutil.copytree(target / 'ibge', staging / 'ibge')
                if download:
                    self.log('Baixando uma vez o cadastro nacional para: ' + ', '.join(ufs))
                    self.command('ingestar_receita.py', staging, ufs)
                else:
                    (staging / 'receita').mkdir()
                    names = ['municipios.csv.gz', 'cnaes.csv.gz'] + [f'{prefix}_{uf.lower()}.{suffix}' for uf in ufs for prefix, suffix in [('estabelecimentos', 'csv.gz'), ('empresas', 'csv.gz'), ('_versao', 'json')]]
                    for filename in names:
                        source = target / 'receita' / filename
                        if not source.is_file():
                            raise ValueError(f'Cadastro local ausente: {filename}. Execute uma coleta nova.')
                        shutil.copyfile(source, staging / 'receita' / filename)
                for uf in ufs:
                    self.log(f'Gerando {uf} em uma pasta temporária…')
                    self.command('gerar_leads.py', staging, [uf])
                self.publish(staging, target, ufs)
            self.log('Atualização concluída. UFs disponíveis: ' + ', '.join(ufs))
            with self.lock:
                self.state['status'] = 'concluido'
        except Exception as error:
            self.log(str(error))
            with self.lock:
                self.state['status'] = 'erro'
        finally:
            if guard is not None:
                guard.close()

    def publish(self, staging: Path, target: Path, ufs=('CE',)):
        ufs = normalize_ufs(ufs)
        # Validate every requested state before replacing any live database.
        for uf in ufs:
            new_db = staging / 'uf' / uf / 'contatos.db'
            with sqlite3.connect(new_db) as conn:
                conn.execute('PRAGMA wal_checkpoint(TRUNCATE)')
                conn.execute('PRAGMA journal_mode=DELETE')
            validate_database(new_db, uf)
            prepare_downloads(staging, uf)
        self.log('Integridade, municípios e downloads verificados. Publicando…')
        for folder in ['receita', 'ibge']:
            (target / folder).mkdir(parents=True, exist_ok=True)
            for file in (staging / folder).glob('*'):
                if file.is_file():
                    os.replace(file, target / folder / file.name)
        for uf in ufs:
            origin = staging / 'uf' / uf
            destination = target / 'uf' / uf
            destination.mkdir(parents=True, exist_ok=True)
            (destination / 'downloads').mkdir(exist_ok=True)
            for bundle in (origin / 'downloads').iterdir():
                os.replace(bundle, destination / 'downloads' / bundle.name)
            report = origin / '_INDICE.md'
            if report.exists():
                os.replace(report, destination / report.name)
            # The database selects an already complete immutable download generation.
            os.replace(origin / 'contatos.db', destination / 'contatos.db')


jobs = JobManager()
