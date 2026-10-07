import csv
import gzip
import io
import json
import subprocess
import sqlite3
import sys
import zipfile
from pathlib import Path

import pytest
import requests

import gerar_leads
import ingestar_receita
import receita
from pipeline.database import connection
from pipeline.jobs import JobManager
from scripts.coletar import main as collect


def csv_bytes(rows, trailing_newline=True):
    stream = io.StringIO(newline='')
    writer = csv.writer(stream, delimiter=';', quoting=csv.QUOTE_ALL, lineterminator='\n')
    writer.writerows(rows)
    text = stream.getvalue()
    return (text if trailing_newline else text.rstrip('\n')).encode('latin-1')


def archive(rows, trailing_newline=True):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as zipped:
        zipped.writestr('cadastro.csv', csv_bytes(rows, trailing_newline))
    return stream.getvalue()


def establishment(basic, uf='CE', active=True, city='1389', name='Padaria São José', email='PADARIA@GMAIL.COM', mobile=True):
    row = [''] * 30
    fields = {
        receita.CNPJ_BASICO: basic, receita.CNPJ_ORDEM: '0001', receita.CNPJ_DV: '00',
        receita.MATRIZ_FILIAL: '1', receita.NOME_FANTASIA: name,
        receita.SITUACAO: '02' if active else '08', receita.DATA_INICIO: '20190101',
        receita.CNAE_PRINCIPAL: '4721102', receita.TIPO_LOGRADOURO: 'RUA',
        receita.LOGRADOURO: 'SÃO JOÃO', receita.NUMERO: '10', receita.BAIRRO: 'CENTRO',
        receita.UF: uf, receita.MUNICIPIO: city, receita.DDD_1: '85',
        receita.TELEFONE_1: '999990000' if mobile else '33330000', receita.EMAIL: email,
    }
    for column, value in fields.items():
        row[column] = value
    return row


class Response:
    def __init__(self, content, status=200):
        self.content = content
        self.text = content.decode('latin-1')
        self.headers = {'Content-Length': str(len(content))}
        self.status = status

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def raise_for_status(self):
        if self.status >= 400:
            raise requests.HTTPError(f'HTTP {self.status}')

    def iter_content(self, chunk_size):
        # Chunks pequenos exercitam a escrita progressiva do downloader real.
        for offset in range(0, len(self.content), 31):
            yield self.content[offset:offset + 31]


@pytest.fixture
def source(monkeypatch):
    establishment_rows = [
        establishment('11111111'),
        establishment('22222222', city='1559', name='', email='contato@sertao.com.br', mobile=False),
        establishment('33333333', active=False),
        establishment('44444444', uf='SP'),
        establishment('55555555', email='', mobile=False),
        ['linha truncada', 'CE'],
    ]
    # O último registro não tem newline: a razão social deve ser preservada.
    company_rows = [
        ['44444444', 'EMPRESA DE OUTRO ESTADO', '2062', '49', '1000', '01', ''],
        ['11111111', 'PADARIA SÃO JOSÉ LTDA', '2062', '49', '1000', '01', ''],
        ['55555555', 'LOJA SEM CANAL', '2062', '49', '1000', '01', ''],
        ['22222222', 'SERTÃO ALIMENTOS LTDA', '2062', '49', '1000', '03', ''],
    ]
    index = b'<a href="2026-08-09/">old</a><a href="2026-09-14/">new</a>'
    responses = {
        receita.ESPELHO: index,
        f'{receita.ESPELHO}/2026-09-14/Municipios.zip': archive([['1389', 'FORTALEZA'], ['1559', 'SOBRAL']]),
        f'{receita.ESPELHO}/2026-09-14/Cnaes.zip': archive([['4721102', 'PADARIA E CONFEITARIA']]),
    }
    for block in range(10):
        responses[f'{receita.ESPELHO}/2026-09-14/Estabelecimentos{block}.zip'] = archive(establishment_rows if block == 0 else [], False)
        responses[f'{receita.ESPELHO}/2026-09-14/Empresas{block}.zip'] = archive(company_rows if block == 0 else [], False)
    requested = []

    def get(url, **kwargs):
        requested.append(url)
        assert kwargs['headers']['User-Agent'] == receita.UA
        return Response(responses[url])

    monkeypatch.setattr(receita.requests, 'get', get)
    return responses, requested


def configure_pipeline(monkeypatch):
    # Restaurar os caminhos globais depois de cada teste mantém o banco real intacto.
    for module, keys in [(ingestar_receita, ['DADOS', 'DESTINO']), (gerar_leads, ['DADOS', 'RECEITA'])]:
        for key in keys:
            monkeypatch.setattr(module, key, getattr(module, key))

    def command(self, script, staging, ufs=('CE',)):
        module = {'ingestar_receita.py': ingestar_receita, 'gerar_leads.py': gerar_leads}[script]
        with monkeypatch.context() as context:
            context.setenv('TMP', str(staging))
            context.setenv('TEMP', str(staging))
            context.setattr(sys, 'argv', [script, '--uf', *ufs, '--dados', str(staging)])
            assert module.main() == 0

    monkeypatch.setattr(JobManager, 'command', command)


def cache_ibge(target):
    folder = target / 'ibge'
    folder.mkdir()
    cached = {
        'estados.json': [{'id': 23, 'sigla': 'CE', 'nome': 'Ceará'}],
        'municipios_CE.json': [{'id': '2304400', 'nome': 'Fortaleza'}, {'id': '2312908', 'nome': 'Sobral'}],
        'malha_br.geojson': {'type': 'FeatureCollection', 'features': []},
        'malha_23.geojson': {'type': 'FeatureCollection', 'features': []},
    }
    for name, data in cached.items():
        (folder / name).write_text(json.dumps(data))


def test_full_collection_from_archives_to_published_sqlite(sample_data, source, monkeypatch):
    _, requested = source
    cache_ibge(sample_data)
    configure_pipeline(monkeypatch)
    assert collect([]) == 0
    with connection(sample_data / 'uf' / 'CE' / 'contatos.db') as conn:
        info = dict(conn.execute('SELECT chave, valor FROM meta'))
        info['contatos'] = conn.execute('SELECT COUNT(*) FROM contatos').fetchone()[0]
        info['municipios'] = conn.execute('SELECT COUNT(*) FROM municipios').fetchone()[0]
    assert info['uf'] == 'CE'
    assert info['versao_receita'] == '2026-09-14'
    assert info['contatos'] == 2
    assert info['municipios'] == 2
    with connection(sample_data / 'uf' / 'CE' / 'contatos.db') as conn:
        conn.row_factory = sqlite3.Row
        result = conn.execute("SELECT c.* FROM contatos_fts JOIN contatos c ON c.id=contatos_fts.rowid WHERE contatos_fts MATCH 'sao*'").fetchall()
        assert len(result) == 1
        assert result[0]['email'] == 'padaria@gmail.com'
        other = conn.execute("SELECT * FROM contatos WHERE cidade='Sobral'").fetchone()
    assert other['empresa'] == 'Sertão Alimentos Ltda'
    assert other['nome'] == other['empresa']
    assert other['porte'] == 'Pequeno porte'
    assert other['abertura'] == '2019'
    assert len([url for url in requested if '/Estabelecimentos' in url]) == 10
    assert len([url for url in requested if '/Empresas' in url]) == 10
    with gzip.open(sample_data / 'receita' / 'estabelecimentos_ce.csv.gz', 'rt') as f:
        assert len(list(csv.DictReader(f))) == 3  # Inclui ativo sem canal, descartado ao gerar leads.
    assert not (sample_data / 'receita' / 'estabelecimentos_sp.csv.gz').exists()
    assert not list(sample_data.glob('.atualizacao-*'))
    assert not list(sample_data.rglob('*.zip'))


def test_failed_archive_download_keeps_previous_database(sample_data, source, monkeypatch):
    _, requested = source
    original = receita.requests.get
    before = (sample_data / 'uf' / 'CE' / 'contatos.db').read_bytes()

    def fail(url, **kwargs):
        if url.endswith('Estabelecimentos1.zip'):
            return Response(b'indisponivel', 503)
        return original(url, **kwargs)

    monkeypatch.setattr(receita.requests, 'get', fail)
    configure_pipeline(monkeypatch)
    assert collect([]) == 1
    assert (sample_data / 'uf' / 'CE' / 'contatos.db').read_bytes() == before
    assert not list(sample_data.glob('.atualizacao-*'))
    assert not list(sample_data.rglob('*.zip'))
    assert not any('/Empresas' in url for url in requested)


def test_collectors_share_collection_lock(sample_data):
    guard = JobManager().acquire_collection_lock()
    other = JobManager()
    try:
        with pytest.raises(ValueError, match='coleta ou atualização'):
            other.start(True)
        assert other.snapshot()['status'] == 'ocioso'
        assert collect([]) == 1
    finally:
        guard.close()
    # O arquivo permanece, mas sua trava é liberada ao terminar o processo.
    JobManager().acquire_collection_lock().close()


def test_collection_cli_help_and_other_ufs_are_rejected():
    root = Path(__file__).resolve().parents[1]
    help_result = subprocess.run([sys.executable, 'scripts/coletar.py', '--help'], cwd=root, capture_output=True, text=True)
    assert help_result.returncode == 0
    assert '--reaproveitar' in help_result.stdout
    for args in [['--uf', 'XX'], ['--blocos', '0'], ['--blocos', '11']]:
        result = subprocess.run([sys.executable, 'src/ingestar_receita.py', *args], cwd=root, capture_output=True, text=True)
        assert result.returncode == 2
        assert 'invalid choice' in result.stderr


def test_interrupted_collection_cleans_staging_and_preserves_data(sample_data, monkeypatch):
    before = (sample_data / 'uf' / 'CE' / 'contatos.db').read_bytes()

    def interrupt(*args):
        raise KeyboardInterrupt

    monkeypatch.setattr(JobManager, 'command', interrupt)
    assert collect([]) == 130
    assert (sample_data / 'uf' / 'CE' / 'contatos.db').read_bytes() == before
    assert not list(sample_data.glob('.atualizacao-*'))
    JobManager().acquire_collection_lock().close()


def test_interrupt_stops_the_real_pipeline_child(tmp_path, monkeypatch):
    from pipeline import jobs as module
    source_folder = tmp_path / 'src'
    source_folder.mkdir()
    (source_folder / 'waiting.py').write_text('import time\nprint("coletando", flush=True)\nwhile True: time.sleep(.1)\n')
    monkeypatch.setattr(module, 'ROOT', tmp_path)
    children = []
    popen = subprocess.Popen

    def capture(*args, **kwargs):
        child = popen(*args, **kwargs)
        children.append(child)
        return child

    def interrupt(line):
        assert line == 'coletando'
        raise KeyboardInterrupt

    monkeypatch.setattr(subprocess, 'Popen', capture)
    with pytest.raises(KeyboardInterrupt):
        JobManager(log_output=interrupt).command('waiting.py', tmp_path)
    assert len(children) == 1
    assert children[0].poll() is not None


def test_multi_state_collection_preserves_text_cnpj_and_downloads_once(sample_data, source, monkeypatch):
    responses, requested = source
    version = f'{receita.ESPELHO}/2026-09-14/'
    ce = establishment('AB12CD34')
    sp = establishment('AB12CD34', uf='SP', city='7000')
    sp[receita.CNPJ_ORDEM] = 'E08G'
    responses[version + 'Estabelecimentos0.zip'] = archive([ce, sp, establishment('00000001')], False)
    responses[version + 'Empresas0.zip'] = archive([
        ['AB12CD34', 'EMPRESA ALFA LTDA', '2062', '49', '1000', '01', ''],
        ['00000001', 'EMPRESA NUMERICA LTDA', '2062', '49', '1000', '01', ''],
    ], False)
    responses[version + 'Municipios.zip'] = archive([['1389', 'FORTALEZA'], ['7000', 'SAO PAULO']])
    cache_ibge(sample_data)
    (sample_data / 'ibge/municipios_SP.json').write_text(json.dumps([{'id': '3550308', 'nome': 'São Paulo'}]))
    (sample_data / 'ibge/malha_35.geojson').write_text(json.dumps({'type': 'FeatureCollection', 'features': []}))
    (sample_data / 'ibge/estados.json').write_text(json.dumps([{'id': 23, 'sigla': 'CE'}, {'id': 35, 'sigla': 'SP'}]))
    untouched = sample_data / 'uf/MG/contatos.db'
    untouched.parent.mkdir()
    untouched.write_bytes((sample_data / 'uf/CE/contatos.db').read_bytes())
    before = untouched.read_bytes()
    configure_pipeline(monkeypatch)
    assert collect(['--uf', 'CE', 'SP', 'CE']) == 0
    for uf, expected in [('CE', 'AB12CD34000100'), ('SP', 'AB12CD34E08G00')]:
        with connection(sample_data / 'uf' / uf / 'contatos.db', uf) as conn:
            assert conn.execute('SELECT cnpj,empresa FROM contatos WHERE cnpj=?', (expected,)).fetchone() == (expected, 'Empresa Alfa Ltda')
            if uf == 'CE':
                assert conn.execute("SELECT cnpj FROM contatos WHERE empresa='Empresa Numerica Ltda'").fetchone()[0] == '00000001000100'
        bundles = list((sample_data / 'uf' / uf / 'downloads').iterdir())
        assert len(bundles) == 1
        with gzip.open(bundles[0] / 'contatos.csv.gz', 'rt') as f:
            assert list(csv.DictReader(f))[0]['cnpj'] == expected
    assert len([url for url in requested if '/Estabelecimentos' in url]) == 10
    assert len([url for url in requested if '/Empresas' in url]) == 10
    assert not list(sample_data.glob('.atualizacao-*'))
    assert untouched.read_bytes() == before


@pytest.mark.parametrize('uf,source_name,official_name,code', [
    ('BA', 'Santa Teresinha', 'santa terezinha', '2928505'),
    ('MG', 'Brasopolis', 'brazopolis', '3108909'),
    ('PA', 'Santa Isabel do para', 'santa izabel do para', '1506500'),
    ('RJ', 'Parati', 'paraty', '3303807'),
    ('RN', 'Ares', 'arez', '2401206'),
    ('RN', 'Boa Saude', 'januario cicco', '2405306'),
    ('RR', 'Sao Luiz', 'sao luiz do anaua', '1400605'),
    ('RS', 'Santana do Livramento', 'sant ana do livramento', '4317103'),
    ('TO', 'Fortaleza do Tabocao', 'tabocao', '1708254'),
    ('TO', 'Sao Valerio da Natividade', 'sao valerio', '1720499'),
])
def test_official_municipal_alias_is_scoped_to_its_state(uf, source_name, official_name, code):
    import banco
    with sqlite3.connect(':memory:') as conn:
        conn.row_factory = sqlite3.Row
        conn.execute('CREATE TABLE municipios(cod_municipio TEXT,nome TEXT,codigo_ibge TEXT,contatos INTEGER)')
        conn.execute('CREATE TABLE meta(chave TEXT PRIMARY KEY,valor TEXT)')
        conn.execute('INSERT INTO municipios VALUES(?,?,NULL,1)', ('3929', source_name))
        official = {official_name: code}
        assert banco.aplicar_codigos_ibge(conn, official, uf='CE') == (0, 1)
        assert banco.aplicar_codigos_ibge(conn, official, uf=uf) == (1, 0)
        assert conn.execute('SELECT codigo_ibge FROM municipios').fetchone()[0] == code
