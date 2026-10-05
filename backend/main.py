import os
import sqlite3
import sys
import tempfile
from pathlib import Path

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, JSONResponse
from starlette.background import BackgroundTask

from backend import ai, database as db
from backend.config import ROOT, data_dir
from backend.jobs import jobs
from backend.models import AnalysisRequest, ChatRequest, ExportRequest, Filters, SearchRequest, UpdateRequest

sys.path.insert(0, str(ROOT / 'src'))
from exportar import escrever_consulta

app = FastAPI(title='Mapa de Oportunidades Ceará', docs_url='/api/docs', openapi_url='/api/openapi.json')


@app.middleware('http')
async def local_only(request: Request, call_next):
    # Bind a localhost e exigir mesma origem evita que sites externos usem a
    # chave ou acionem tarefas pela sessão local do usuário.
    host = request.headers.get('host', '').split(':')[0]
    forwarded = request.headers.get('x-forwarded-host', host).split(':')[0]
    if host not in ('127.0.0.1', 'localhost', 'testserver') or forwarded not in ('127.0.0.1', 'localhost', 'testserver'):
        return JSONResponse({'detail': 'Esta aplicação aceita somente acesso local.'}, status_code=403)
    if request.method not in ('GET', 'HEAD', 'OPTIONS'):
        origin = request.headers.get('origin')
        if origin and origin not in ('http://localhost:3000', 'http://127.0.0.1:3000', 'http://localhost:8000', 'http://127.0.0.1:8000'):
            return JSONResponse({'detail': 'Origem não permitida.'}, status_code=403)
        if request.headers.get('content-type', '').split(';')[0] != 'application/json':
            return JSONResponse({'detail': 'Envie application/json.'}, status_code=415)
    return await call_next(request)


@app.exception_handler(db.DatabaseUnavailable)
async def unavailable(request, error):
    return JSONResponse({'detail': str(error)}, status_code=503)


@app.exception_handler(sqlite3.Error)
async def database_error(request, error):
    return JSONResponse({'detail': 'Não foi possível concluir a consulta à base. Tente um recorte menor ou verifique os dados.'}, status_code=503)


@app.exception_handler(ai.AIError)
async def ai_error(request, error):
    return JSONResponse({'detail': error.message}, status_code=error.status)


@app.get('/api/saude')
def health():
    return {'status': 'ok', 'uf': 'CE'}


@app.get('/api/base')
def base():
    configured = bool(os.getenv('GEMINI_API_KEY', '').strip())
    try:
        return {'disponivel': True, **db.metadata(), 'ia_configurada': configured, 'modelo_ia': os.getenv('GEMINI_MODEL', 'gemini-3.5-flash-lite')}
    except db.DatabaseUnavailable as error:
        return {'disponivel': False, 'erro': str(error), 'ia_configurada': configured, 'modelo_ia': os.getenv('GEMINI_MODEL', 'gemini-3.5-flash-lite')}


@app.post('/api/contatos/buscar')
def search(request: SearchRequest):
    return db.search(request)


@app.get('/api/contatos/{id}')
def detail(id: int):
    found = db.detail(id)
    if not found:
        raise HTTPException(404, 'Empresa não encontrada.')
    return found


@app.post('/api/facetas')
def facets(filters: Filters):
    return db.facets(filters)


@app.get('/api/municipios')
def municipalities():
    return db.areas('municipios')


@app.get('/api/ramos')
def segments():
    return db.areas('segmentos')


@app.post('/api/analises')
def analysis(request: AnalysisRequest):
    return db.analyze(request)


@app.get('/api/malhas/{nome}')
def geometry(nome: str):
    if nome not in ('malha_23.geojson', 'malha_br.geojson'):
        raise HTTPException(404, 'Malha não disponível. Apenas o Ceará pode ser consultado.')
    path = data_dir() / 'ibge' / nome
    if not path.is_file():
        raise HTTPException(404, 'Malha ausente. Atualize a base para baixar a malha do IBGE.')
    return FileResponse(path, media_type='application/geo+json')


@app.post('/api/exportar')
def export(request: ExportRequest):
    source, condition, params = db.where(request.filtros, request.ids)
    fd, name = tempfile.mkstemp(suffix='.xlsx', prefix='ceara-')
    os.close(fd)
    path = Path(name)
    try:
        # A escrita de planilhas grandes pode levar minutos; o orçamento
        # curto das consultas interativas não deve interromper a exportação.
        with db.connection(time_budget=None) as conn:
            generation = conn.execute("SELECT valor FROM meta WHERE chave='gerado_em'").fetchone()
            if request.base_gerada_em and (not generation or request.base_gerada_em != generation[0]):
                raise HTTPException(409, 'A base foi atualizada. Recarregue a lista e refaça a seleção antes de exportar.')
            escrever_consulta(conn, f'SELECT c.* FROM {source} WHERE {condition} ORDER BY {db.ORDERS[request.filtros.ordem]}', params, str(path), 'Ceará — seleção de empresas' if request.ids else 'Ceará — recorte filtrado da base de contatos')
    except Exception:
        path.unlink(missing_ok=True)
        raise
    return FileResponse(path, filename='oportunidades-ceara.xlsx', media_type='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                        background=BackgroundTask(path.unlink, missing_ok=True))


@app.get('/api/tarefas')
def task_status():
    return jobs.snapshot()


@app.post('/api/tarefas')
def start_task(request: UpdateRequest):
    try:
        return jobs.start(request.baixar_cadastro)
    except ValueError as error:
        raise HTTPException(409, str(error))


@app.post('/api/chat')
async def chat(request: ChatRequest):
    return await ai.chat(request)
