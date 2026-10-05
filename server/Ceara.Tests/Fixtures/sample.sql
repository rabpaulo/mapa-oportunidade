
PRAGMA journal_mode = DELETE;

CREATE TABLE IF NOT EXISTS contatos (
    id              INTEGER PRIMARY KEY,
    cnpj            TEXT,
    nome            TEXT NOT NULL,
    empresa         TEXT,
    email           TEXT,
    telefone        TEXT,
    whatsapp        TEXT,
    cidade          TEXT,
    cod_municipio   TEXT,
    bairro          TEXT,
    endereco        TEXT,
    segmento        TEXT,
    oportunidade    TEXT,
    porte           TEXT,
    abertura        TEXT,
    dominio_proprio INTEGER,
    tem_celular     INTEGER,
    score           INTEGER,
    busca           TEXT
);

CREATE TABLE IF NOT EXISTS meta (
    chave TEXT PRIMARY KEY,
    valor TEXT
);

-- Agregados por municipio e por segmento: o mapa e os filtros da tela pedem
-- contagens a cada interacao, e varrer 1,4 milhao de linhas para isso seria
-- lento a toa.
-- A chave e o codigo da Receita, que e o que vem nos contatos. O codigo do
-- IBGE entra depois, casado pelo nome, e so serve para desenhar o mapa.
CREATE TABLE IF NOT EXISTS municipios (
    cod_municipio TEXT PRIMARY KEY,
    codigo_ibge   TEXT,
    nome          TEXT,
    uf            TEXT,
    contatos      INTEGER,
    com_email     INTEGER,
    com_celular   INTEGER,
    sem_dominio   INTEGER,
    score_medio   REAL
);

CREATE TABLE IF NOT EXISTS segmentos (
    nome        TEXT PRIMARY KEY,
    contatos    INTEGER,
    com_email   INTEGER,
    com_celular INTEGER,
    sem_dominio INTEGER,
    score_medio REAL
);

CREATE INDEX IF NOT EXISTS idx_cidade    ON contatos(cidade);
CREATE INDEX IF NOT EXISTS idx_municipio ON contatos(cod_municipio);
CREATE INDEX IF NOT EXISTS idx_segmento  ON contatos(segmento);
CREATE INDEX IF NOT EXISTS idx_score     ON contatos(score DESC);
CREATE INDEX IF NOT EXISTS idx_porte     ON contatos(porte);
CREATE INDEX IF NOT EXISTS idx_mun_ibge  ON municipios(codigo_ibge);
INSERT INTO "contatos" VALUES(1,'11111111000100','Padaria São José','São José Alimentos','padaria@gmail.com','(85) 99999-0000','https://wa.me/5585999990000','Fortaleza','1389','Aldeota','Rua Um 12','Padaria e confeitaria','Pedido online','Microempresa','2019',0,1,90,'padaria sao jose sao jose alimentos fortaleza padaria e confeitaria');
INSERT INTO "contatos" VALUES(2,'22222222000100','Metalúrgica Sertão','Sertão Ltda','contato@sertao.com.br','','','Fortaleza','1389','Centro','Rua Dois 30','Metalurgia','ERP','Pequeno porte','2024',1,0,70,'metalurgica sertao sertao ltda fortaleza metalurgia');
INSERT INTO "contatos" VALUES(3,'33333333000100','Padaria Cariri','Cariri Ltda','','(88) 99999-0011','https://wa.me/5588999990011','Juazeiro Do Norte','1447','Centro','Rua Três 14','Padaria e confeitaria','Pedido online','Microempresa','2020',0,1,80,'padaria cariri cariri ltda juazeiro do norte padaria e confeitaria');
INSERT INTO "contatos" VALUES(4,'44444444000100','Oficina Sobral','Sobral Serviços','oficina@yahoo.com','','','Sobral','1559','Centro','Rua Quatro 4','Oficina mecanica','Agenda','Medio/grande','2022',0,0,60,'oficina sobral sobral servicos sobral oficina mecanica');
INSERT INTO "contatos" VALUES(5,'55555555000100','Padaria Nova','Nova Alimentos','nova@gmail.com','(85) 99999-0022','https://wa.me/5585999990022','Fortaleza','1389','Aldeota','Rua Cinco 5','Padaria e confeitaria','Pedido online','Microempresa','2025',0,1,50,'padaria nova nova alimentos fortaleza padaria e confeitaria');
INSERT INTO "meta" VALUES('uf','CE');
INSERT INTO "meta" VALUES('versao_receita','2026-09-14');
INSERT INTO "meta" VALUES('gerado_em','2026-10-04 17:12');
INSERT INTO "municipios" VALUES('1389','2304400','Fortaleza','CE',3,3,2,2,70.0);
INSERT INTO "municipios" VALUES('1447','2307304','Juazeiro Do Norte','CE',1,0,1,0,80.0);
INSERT INTO "municipios" VALUES('1559','2312908','Sobral','CE',1,1,0,1,60.0);
INSERT INTO "segmentos" VALUES('Metalurgia',1,1,0,0,70.0);
INSERT INTO "segmentos" VALUES('Oficina mecanica',1,1,0,1,60.0);
INSERT INTO "segmentos" VALUES('Padaria e confeitaria',3,2,3,2,73.333333333333329);

CREATE VIRTUAL TABLE IF NOT EXISTS contatos_fts USING fts5(
    busca,
    content='contatos',
    content_rowid='id',
    tokenize='unicode61'
);
INSERT INTO contatos_fts(contatos_fts) VALUES('rebuild');
