import json

import httpx
import pytest

from backend import ai


def test_missing_key_does_not_disable_regular_queries(client, monkeypatch):
    monkeypatch.delenv('GEMINI_API_KEY', raising=False)
    assert client.post('/api/chat', json={'pergunta': 'Olá'}).status_code == 503
    assert client.post('/api/contatos/buscar', json={}).json()['total'] == 5


def test_general_question_and_conversation_history(client, monkeypatch):
    monkeypatch.setenv('GEMINI_API_KEY', 'test-secret')
    async def fake(payload, key, model):
        assert key == 'test-secret'
        assert payload['contents'][0]['parts'][0]['text'] == 'Explique pesquisa de mercado.'
        assert payload['contents'][-1]['parts'][0]['text'] == 'Dê um exemplo.'
        assert 'generalista' in payload['systemInstruction']['parts'][0]['text']
        return {'role': 'model', 'parts': [{'text': 'Uma entrevista com clientes é um exemplo.'}]}
    monkeypatch.setattr(ai, 'generate', fake)
    result = client.post('/api/chat', json={'pergunta': 'Dê um exemplo.', 'historico': [{'role': 'user', 'text': 'Explique pesquisa de mercado.'}, {'role': 'model', 'text': 'É um estudo do público.'}]}).json()
    assert result['texto'].startswith('Uma entrevista')
    assert result['fontes'] == []


def test_database_tools_and_no_identifiable_rows_sent_to_gemini(client, monkeypatch):
    monkeypatch.setenv('GEMINI_API_KEY', 'test-secret')
    calls = []
    async def fake(payload, key, model):
        calls.append(payload)
        if len(calls) == 1:
            return {'role': 'model', 'parts': [{'functionCall': {'name': 'buscar_empresas', 'args': {'filtros': {'cidade': 'Fortaleza'}, 'limite': 2}}, 'thoughtSignature': 'preserve-me'}, {'functionCall': {'name': 'analisar_base', 'args': {'filtros': {'cidades': ['Fortaleza', 'Sobral']}, 'agrupar_por': ['cidade']}}}]}
        assert payload['contents'][-2]['parts'][0]['thoughtSignature'] == 'preserve-me'
        serialized = json.dumps(payload, ensure_ascii=False)
        for sensitive in ['Padaria São José', '11111111000100', 'padaria@gmail.com', 'Rua Um 12', '99999-0000']:
            assert sensitive not in serialized
        return {'role': 'model', 'parts': [{'text': 'Fortaleza tem 3 empresas e Sobral tem 1 neste recorte. Veja a tabela local.'}]}
    monkeypatch.setattr(ai, 'generate', fake)
    response = client.post('/api/chat', json={'pergunta': 'Compare Fortaleza e Sobral e mostre empresas.'})
    assert response.status_code == 200
    result = response.json()
    assert result['resultados'][0]['total'] == 3
    assert result['resultados'][0]['itens'][0]['nome'] == 'Padaria São José'
    assert result['fontes'][0]['versao'] == '2026-09-14'


def test_invalid_tool_and_missing_information_are_returned_to_model(client, monkeypatch):
    monkeypatch.setenv('GEMINI_API_KEY', 'test-secret')
    rounds = 0
    async def fake(payload, key, model):
        nonlocal rounds
        rounds += 1
        if rounds == 1:
            return {'role': 'model', 'parts': [{'functionCall': {'name': 'analisar_base', 'args': {'agrupar_por': ['faturamento']}}}, {'functionCall': {'name': 'executar_sql', 'args': {'sql': 'DELETE FROM contatos'}}}]}
        assert all('erro' in part['functionResponse']['response'] for part in payload['contents'][-1]['parts'])
        return {'role': 'model', 'parts': [{'text': 'A base não contém faturamento.'}]}
    monkeypatch.setattr(ai, 'generate', fake)
    result = client.post('/api/chat', json={'pergunta': 'Qual o faturamento?'}).json()
    assert result['texto'] == 'A base não contém faturamento.'
    assert result['resultados'] == []
    assert client.post('/api/contatos/buscar', json={}).json()['total'] == 5


@pytest.mark.parametrize('status,expected', [(403, 401), (429, 429), (500, 503), (404, 503)])
def test_provider_errors_are_clear_and_do_not_leak_credentials(client, monkeypatch, status, expected):
    monkeypatch.setenv('GEMINI_API_KEY', 'never-display-this-key')
    class FakeClient:
        def __init__(self, **kwargs): pass
        async def __aenter__(self): return self
        async def __aexit__(self, *args): pass
        async def post(self, url, **kwargs): return httpx.Response(status, json={'error': {'message': 'private details'}})
    monkeypatch.setattr(ai.httpx, 'AsyncClient', FakeClient)
    response = client.post('/api/chat', json={'pergunta': 'Olá'})
    assert response.status_code == expected
    assert 'never-display-this-key' not in response.text
    assert 'private details' not in response.text


def test_timeout(client, monkeypatch):
    monkeypatch.setenv('GEMINI_API_KEY', 'test-key')
    class FakeClient:
        def __init__(self, **kwargs): pass
        async def __aenter__(self): return self
        async def __aexit__(self, *args): pass
        async def post(self, *args, **kwargs): raise httpx.ReadTimeout('timeout')
    monkeypatch.setattr(ai.httpx, 'AsyncClient', FakeClient)
    assert client.post('/api/chat', json={'pergunta': 'Olá'}).status_code == 504
