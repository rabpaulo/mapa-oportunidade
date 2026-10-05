from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator


class StrictModel(BaseModel):
    model_config = ConfigDict(extra='forbid')


class Filters(StrictModel):
    termo: str = Field(default='', max_length=200)
    cidade: str = Field(default='', max_length=120)
    cidades: list[str] = Field(default_factory=list, max_length=20)
    segmento: str = Field(default='', max_length=120)
    segmentos: list[str] = Field(default_factory=list, max_length=20)
    porte: str = Field(default='', max_length=80)
    bairro: str = Field(default='', max_length=120)
    ano_minimo: int | None = Field(default=None, ge=1800, le=2200)
    ano_maximo: int | None = Field(default=None, ge=1800, le=2200)
    score_minimo: int = Field(default=0, ge=0, le=100)
    somente_celular: bool = False
    somente_email: bool = False
    somente_sem_dominio: bool = False
    ordem: Literal['score', 'nome', 'cidade', 'recente', 'antiga'] = 'score'

    @model_validator(mode='after')
    def dates(self):
        if self.ano_minimo and self.ano_maximo and self.ano_minimo > self.ano_maximo:
            raise ValueError('O ano inicial deve ser menor ou igual ao final.')
        if any(not isinstance(v, str) or len(v) > 120 for v in self.cidades + self.segmentos):
            raise ValueError('Municípios e ramos devem ter até 120 caracteres.')
        return self


class SearchRequest(StrictModel):
    filtros: Filters = Field(default_factory=Filters)
    pagina: int = Field(default=1, ge=1, le=1_000_000)
    por_pagina: int = Field(default=50, ge=1, le=200)


class ExportRequest(StrictModel):
    filtros: Filters = Field(default_factory=Filters)
    ids: list[int] = Field(default_factory=list, max_length=20_000)
    base_gerada_em: str | None = Field(default=None, max_length=80)

    @model_validator(mode='after')
    def positive_ids(self):
        if any(i <= 0 for i in self.ids):
            raise ValueError('IDs devem ser positivos.')
        return self


Group = Literal['cidade', 'bairro', 'segmento', 'porte', 'abertura']
Metric = Literal['contatos', 'com_email', 'com_celular', 'sem_dominio', 'score_medio']


class AnalysisRequest(StrictModel):
    filtros: Filters = Field(default_factory=Filters)
    agrupar_por: list[Group] = Field(default_factory=list, max_length=2)
    ordenar_por: Metric = 'contatos'
    limite: int = Field(default=20, ge=1, le=200)


class ToolSearch(StrictModel):
    filtros: Filters = Field(default_factory=Filters)
    limite: int = Field(default=20, ge=1, le=100)


class UpdateRequest(StrictModel):
    baixar_cadastro: bool = False


class ChatMessage(StrictModel):
    role: Literal['user', 'model']
    text: str = Field(min_length=1, max_length=6000)


class ChatRequest(StrictModel):
    pergunta: str = Field(min_length=1, max_length=4000)
    historico: list[ChatMessage] = Field(default_factory=list, max_length=20)
