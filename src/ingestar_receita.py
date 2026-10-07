# -*- coding: utf-8 -*-
"""Coleta estabelecimentos ativos em uma ou mais UFs, em uma passagem nacional.

Uso:
    python src/ingestar_receita.py --uf CE --dados /pasta/temporaria

Escreve receita/estabelecimentos_ce.csv.gz e empresas_ce.csv.gz.

Os arquivos da Receita sao nacionais, nao ha download por estado. Cada ZIP
e baixado, lido em blocos e apagado; somente estabelecimentos ativos das UFs selecionadas e
as empresas correspondentes sao gravados. Ceara e o padrao.

As linhas sao gravadas conforme saem do parser: guardar os milhoes de
estabelecimentos em memoria nao caberia na RAM.

Para coletar e publicar o SQLite com seguranca, use scripts/coletar.py. Este modulo de ingestao escreve diretamente no destino informado.
"""

import argparse
import csv
import gzip
import json
import os
import sys
import time
import zipfile
import sqlite3
import tempfile
from geografia import UFS

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import receita

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Onde os dados ficam. O padrao e a pasta do repositorio, mas o app instalado
# passa --dados apontando para fora da instalacao: assim uma atualizacao troca
# o programa sem encostar no que foi baixado.
DADOS = os.path.join(RAIZ, "data")
DESTINO = os.path.join(DADOS, "receita")


def usar_pasta_dados(caminho):
    global DADOS, DESTINO
    DADOS = os.path.abspath(caminho)
    DESTINO = os.path.join(DADOS, "receita")
    os.makedirs(DESTINO, exist_ok=True)

COLUNAS = [
    "cnpj", "matriz_filial", "nome_fantasia", "data_inicio", "cnae",
    "cnae_secundaria", "logradouro", "numero", "complemento", "bairro", "cep",
    "municipio", "ddd1", "telefone1", "ddd2", "telefone2", "email",
]


def _tmpdir():
    base = os.environ.get("TEMP") or os.environ.get("TMP") or "."
    caminho = os.path.join(base, "mapa-oportunidades-ceara-cnpj")
    os.makedirs(caminho, exist_ok=True)
    return caminho


def _linha_saida(c):
    cnpj = c[receita.CNPJ_BASICO] + c[receita.CNPJ_ORDEM] + c[receita.CNPJ_DV]
    logr = " ".join(x for x in (c[receita.TIPO_LOGRADOURO], c[receita.LOGRADOURO]) if x)
    return [
        cnpj, c[receita.MATRIZ_FILIAL], c[receita.NOME_FANTASIA],
        c[receita.DATA_INICIO], c[receita.CNAE_PRINCIPAL], c[receita.CNAE_SECUNDARIA],
        logr, c[receita.NUMERO], c[receita.COMPLEMENTO], c[receita.BAIRRO],
        c[receita.CEP], c[receita.MUNICIPIO],
        c[receita.DDD_1], c[receita.TELEFONE_1],
        c[receita.DDD_2], c[receita.TELEFONE_2],
        c[receita.EMAIL].strip().lower(),
    ]


def caminho_estabelecimentos(uf):
    return os.path.join(DESTINO, f"estabelecimentos_{uf.lower()}.csv.gz")


def caminho_empresas(uf):
    return os.path.join(DESTINO, f"empresas_{uf.lower()}.csv.gz")


class BasicIndex:
    """Índice em disco; CNPJ permanece texto inclusive no formato alfanumérico."""
    def __init__(self):
        self.folder = tempfile.TemporaryDirectory(prefix='.cnpj-index-', dir=_tmpdir())
        self.conn = sqlite3.connect(os.path.join(self.folder.name, 'alvos.db'))
        self.conn.executescript("PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-32768; CREATE TABLE alvos (basico TEXT, uf TEXT, PRIMARY KEY(basico,uf)) WITHOUT ROWID; CREATE TEMP TABLE candidatos (basico TEXT PRIMARY KEY) WITHOUT ROWID;")
        self.batch = []

    def add(self, basic, uf):
        self.batch.append((basic, uf))
        if len(self.batch) >= 20000:
            self.flush()

    def flush(self):
        self.conn.executemany('INSERT OR IGNORE INTO alvos VALUES (?,?)', self.batch)
        self.conn.commit()
        self.batch.clear()

    def targets(self, rows):
        self.conn.execute('DELETE FROM candidatos')
        self.conn.executemany('INSERT OR IGNORE INTO candidatos VALUES (?)', ((basic,) for basic in rows))
        return self.conn.execute('SELECT a.basico,a.uf FROM candidatos c CROSS JOIN alvos a ON a.basico=c.basico')

    def close(self):
        self.conn.close()
        self.folder.cleanup()


def baixar_estabelecimentos(versao, ufs, blocos=10, log=print):
    os.makedirs(DESTINO, exist_ok=True)
    arquivos, escritores = {}, {}
    totais = {uf: 0 for uf in ufs}
    basicos = BasicIndex()
    try:
        for uf in ufs:
            arquivos[uf] = gzip.open(caminho_estabelecimentos(uf), 'wt', encoding='utf-8', newline='')
            escritores[uf] = csv.writer(arquivos[uf])
            escritores[uf].writerow(COLUNAS)
        for i in range(blocos):
            nome = f'Estabelecimentos{i}.zip'
            caminho = os.path.join(_tmpdir(), nome)
            antes = dict(totais)
            log(f'  [{i+1}/{blocos}] {nome}', flush=True)
            try:
                receita._baixar(f'{receita.ESPELHO}/{versao}/{nome}', caminho, log=log)
                for linha in receita._linhas_do_zip(caminho, ufs):
                    c = receita._converter(linha)
                    if not c or c[receita.UF] not in escritores or c[receita.SITUACAO] != receita.ATIVA:
                        continue
                    uf = c[receita.UF]
                    escritores[uf].writerow(_linha_saida(c))
                    basicos.add(c[receita.CNPJ_BASICO], uf)
                    totais[uf] += 1
                basicos.flush()
                log('      ' + ', '.join(f'{uf} +{totais[uf]-antes[uf]}' for uf in ufs), flush=True)
            finally:
                if os.path.exists(caminho):
                    os.remove(caminho)
    except BaseException:
        basicos.close()
        raise
    finally:
        for fh in arquivos.values():
            fh.close()
    return basicos, totais


def baixar_empresas(versao, basicos, ufs, blocos=10, log=print):
    arquivos, escritores = {}, {}
    achados = {uf: 0 for uf in ufs}
    try:
        basicos.flush()
        for uf in ufs:
            arquivos[uf] = gzip.open(caminho_empresas(uf), 'wt', encoding='utf-8', newline='')
            escritores[uf] = csv.writer(arquivos[uf])
            escritores[uf].writerow(['cnpj_basico', 'razao_social', 'natureza', 'qualificacao', 'capital_social', 'porte'])
        for i in range(blocos):
            nome = f'Empresas{i}.zip'
            caminho = os.path.join(_tmpdir(), nome)
            log(f'  [{i+1}/{blocos}] {nome}', flush=True)
            try:
                receita._baixar(f'{receita.ESPELHO}/{versao}/{nome}', caminho, log=log)
                with zipfile.ZipFile(caminho) as zf, zf.open(zf.namelist()[0]) as bruto:
                    rows = {}
                    def write_batch():
                        for basic, uf in basicos.targets(rows):
                            c = receita._converter(rows[basic], minimo=6)
                            if c:
                                escritores[uf].writerow(c[:6])
                                achados[uf] += 1
                        rows.clear()
                    for linha in bruto:
                        if len(linha) >= 12:
                            basic = linha[1:9].decode('latin-1')
                            rows[basic] = linha.rstrip(b'\r\n')
                        if len(rows) >= 20000:
                            write_batch()
                    if rows:
                        write_batch()
            finally:
                if os.path.exists(caminho):
                    os.remove(caminho)
            log('      ' + ', '.join(f'{uf}: {achados[uf]} empresas' for uf in ufs), flush=True)
    finally:
        for fh in arquivos.values():
            fh.close()
        basicos.close()
    return achados


def basicos_do_arquivo(uf, index):
    with gzip.open(caminho_estabelecimentos(uf), 'rt', encoding='utf-8') as fh:
        for row in csv.DictReader(fh):
            index.add(row['cnpj'][:8], uf)
    index.flush()


def registrar_versao(uf, versao, estabelecimentos, log=print):
    """Guarda de qual publicacao da Receita os dados vieram.

    O relatorio precisa dizer a data do cadastro: um lead de tres meses atras
    tem valor diferente de um de ontem, e sem isso nao ha como saber.
    """
    caminho = os.path.join(DESTINO, f"_versao_{uf.lower()}.json")
    with open(caminho, "w", encoding="utf-8") as fh:
        json.dump({
            "uf": uf,
            "versao_receita": versao,
            "baixado_em": time.strftime("%Y-%m-%d %H:%M"),
            "estabelecimentos_ativos": estabelecimentos,
        }, fh, ensure_ascii=False, indent=2)
    log(f"  versao registrada para {uf}")


def main():
    ap = argparse.ArgumentParser(description="Ingestao dos Dados Abertos de CNPJ")
    ap.add_argument("--uf", nargs="+", default=["CE"], type=str.upper, choices=sorted(UFS),
                    help="uma ou mais UFs; um único download nacional")
    ap.add_argument("--pular-empresas", action="store_true")
    ap.add_argument("--somente-empresas", action="store_true",
                    help="reaproveita o arquivo de estabelecimentos ja baixado")
    ap.add_argument("--blocos", type=int, default=10, choices=range(1, 11),
                    help="quantos dos 10 blocos ler; menos serve para conferir")
    ap.add_argument("--dados", help="pasta de dados (padrao: data/ do projeto)")
    args = ap.parse_args()

    ufs = []
    for uf in args.uf:
        sigla = uf.strip().upper()
        if len(sigla) == 2 and sigla.isalpha() and sigla not in ufs:
            ufs.append(sigla)
    if not ufs:
        print("Informe ao menos uma sigla de estado, com duas letras.")
        return 1

    if args.dados:
        usar_pasta_dados(args.dados)

    inicio = time.time()
    if args.somente_empresas:
        versions = [json.load(open(os.path.join(DESTINO, f'_versao_{uf.lower()}.json')))['versao_receita'] for uf in ufs]
        if len(set(versions)) != 1:
            raise ValueError('As UFs precisam pertencer à mesma versão da Receita.')
        versao = versions[0]
    else:
        versao = receita.ultima_versao()
    print(f"  estados: {', '.join(ufs)}")
    print(f"  dados em: {DADOS}")

    if args.somente_empresas:
        print("\nEmpresas (CNPJs lidos dos arquivos existentes):")
        basicos = BasicIndex()
        for uf in ufs:
            basicos_do_arquivo(uf, basicos)
        baixar_empresas(versao, basicos, ufs)
        print(f"\nConcluido em {(time.time()-inicio)/60:.1f} min")
        return 0

    os.makedirs(DESTINO, exist_ok=True)
    for nome in ("Municipios", "Cnaes"):
        tabela = receita.tabela_auxiliar(versao, nome)
        with gzip.open(os.path.join(DESTINO, f"{nome.lower()}.csv.gz"), "wt",
                       encoding="utf-8", newline="") as fh:
            esc = csv.writer(fh)
            esc.writerow(["codigo", "descricao"])
            for k, v in tabela.items():
                esc.writerow([k, v])

    print("\nEstabelecimentos:")
    basicos, totais = baixar_estabelecimentos(versao, ufs, args.blocos)
    for uf in ufs:
        registrar_versao(uf, versao, totais[uf])

    if not args.pular_empresas:
        print("\nEmpresas:")
        baixar_empresas(versao, basicos, ufs)
    else:
        basicos.close()

    print(f"\nConcluido em {(time.time()-inicio)/60:.1f} min")
    print(f"Agora rode: " + "; ".join(
        f"python src/gerar_leads.py --uf {uf}" for uf in ufs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
