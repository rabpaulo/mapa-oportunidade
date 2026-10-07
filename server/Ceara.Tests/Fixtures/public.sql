CREATE TABLE contatos(id INTEGER PRIMARY KEY,cnpj TEXT NOT NULL,cidade TEXT NOT NULL,cod_municipio TEXT NOT NULL,segmento TEXT NOT NULL,oportunidade TEXT NOT NULL,porte TEXT NOT NULL,abertura TEXT NOT NULL,score INTEGER NOT NULL);
CREATE TABLE meta(chave TEXT PRIMARY KEY,valor TEXT);
CREATE TABLE municipios(cod_municipio TEXT PRIMARY KEY,codigo_ibge TEXT,nome TEXT,uf TEXT,contatos INTEGER,score_medio REAL);
CREATE TABLE segmentos(nome TEXT PRIMARY KEY,contatos INTEGER,score_medio REAL);
INSERT INTO contatos VALUES
 (1,'11111111000100','Fortaleza','1389','Padaria e confeitaria','Pedido online','Microempresa','2019',30),
 (2,'22222222000100','Fortaleza','1389','Metalurgia','ERP','Pequeno porte','2024',35),
 (3,'33333333000100','Juazeiro Do Norte','1447','Padaria e confeitaria','Pedido online','Microempresa','2020',30),
 (4,'44444444000100','Sobral','1559','Oficina mecanica','Agenda','Medio/grande','2022',45),
 (5,'55555555000100','Fortaleza','1389','Padaria e confeitaria','Pedido online','Microempresa','2025',25);
INSERT INTO meta VALUES('uf','CE'),('versao_receita','2026-09-14'),('gerado_em','2026-10-04 17:12'),('publicacao_restrita','1'),('politica_publicacao','empresas-sem-dados-pessoais-v2');
INSERT INTO municipios SELECT cod_municipio,CASE cod_municipio WHEN '1389' THEN '2304400' WHEN '1447' THEN '2307304' ELSE '2312908' END,cidade,'CE',COUNT(*),AVG(score) FROM contatos GROUP BY cod_municipio,cidade;
INSERT INTO segmentos SELECT segmento,COUNT(*),AVG(score) FROM contatos GROUP BY segmento;
CREATE VIRTUAL TABLE contatos_fts USING fts5(cnpj,cidade,segmento,porte,content='contatos',content_rowid='id',tokenize='unicode61');
INSERT INTO contatos_fts(contatos_fts) VALUES('rebuild');
