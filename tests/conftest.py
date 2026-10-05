import sqlite3
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
import banco


@pytest.fixture
def sample_data(tmp_path, monkeypatch):
    monkeypatch.setenv('CEARA_DATA_DIR', str(tmp_path))
    path = tmp_path / 'uf' / 'CE' / 'contatos.db'
    path.parent.mkdir(parents=True)
    conn = sqlite3.connect(path)
    banco.preparar(conn)
    rows = [
        ('11111111000100', 'Padaria São José', 'São José Alimentos', 'padaria@gmail.com', '(85) 99999-0000', 'https://wa.me/5585999990000', 'Fortaleza', '1389', 'Aldeota', 'Rua Um 12', 'Padaria e confeitaria', 'Pedido online', 'Microempresa', '2019', 0, 1, 90),
        ('22222222000100', 'Metalúrgica Sertão', 'Sertão Ltda', 'contato@sertao.com.br', '', '', 'Fortaleza', '1389', 'Centro', 'Rua Dois 30', 'Metalurgia', 'ERP', 'Pequeno porte', '2024', 1, 0, 70),
        ('33333333000100', 'Padaria Cariri', 'Cariri Ltda', '', '(88) 99999-0011', 'https://wa.me/5588999990011', 'Juazeiro Do Norte', '1447', 'Centro', 'Rua Três 14', 'Padaria e confeitaria', 'Pedido online', 'Microempresa', '2020', 0, 1, 80),
        ('44444444000100', 'Oficina Sobral', 'Sobral Serviços', 'oficina@yahoo.com', '', '', 'Sobral', '1559', 'Centro', 'Rua Quatro 4', 'Oficina mecanica', 'Agenda', 'Medio/grande', '2022', 0, 0, 60),
        ('55555555000100', 'Padaria Nova', 'Nova Alimentos', 'nova@gmail.com', '(85) 99999-0022', 'https://wa.me/5585999990022', 'Fortaleza', '1389', 'Aldeota', 'Rua Cinco 5', 'Padaria e confeitaria', 'Pedido online', 'Microempresa', '2025', 0, 1, 50),
    ]
    banco.inserir(conn, [(*r, banco.chave_busca(r[1], r[2], r[6], r[10])) for r in rows])
    banco.finalizar(conn, 'CE', {'uf': 'CE', 'versao_receita': '2026-09-14', 'gerado_em': '2026-10-04 17:12'})
    conn.executemany('UPDATE municipios SET codigo_ibge=? WHERE nome=?', [('2304400', 'Fortaleza'), ('2307304', 'Juazeiro Do Norte'), ('2312908', 'Sobral')])
    conn.commit()
    conn.execute('PRAGMA journal_mode=DELETE')
    conn.close()
    return tmp_path
