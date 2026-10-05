'use client';

import { useEffect, useRef, useState } from 'react';
import * as maplibregl from 'maplibre-gl';
import { ArrowUpRight, Compass, LocateFixed } from 'lucide-react';
import { api, type Area, type BaseInfo, number, percent } from '@/lib/api';

maplibregl.setWorkerUrl('/maplibre/maplibre-gl-worker.mjs');

type Metric = 'contatos' | 'com_celular' | 'sem_dominio' | 'score_medio';
const metrics: { id: Metric; label: string }[] = [{ id: 'contatos', label: 'Total de empresas' }, { id: 'com_celular', label: 'Com celular' }, { id: 'sem_dominio', label: 'E-mail sem domínio próprio' }, { id: 'score_medio', label: 'Score médio' }];
const ramps = {
  claro: ['#f3e8db', '#e4c5ad', '#cc9a79', '#b77350', '#a95133', '#743020'],
  escuro: ['#3b302b', '#69412e', '#9c5c3b', '#b87851', '#d59668', '#e8b286'],
};

// Geometria e enquadramento adaptados de código © 2026 Ivo Braatz (AGPL-3.0-only).
function bounds(geo: GeoJSON.FeatureCollection): maplibregl.LngLatBoundsLike {
  let west = 180, south = 90, east = -180, north = -90;
  const visit = (coords: unknown): void => {
    if (typeof (coords as number[])[0] === 'number') {
      const [x, y] = coords as number[]; west = Math.min(west, x); south = Math.min(south, y); east = Math.max(east, x); north = Math.max(north, y);
    } else (coords as unknown[]).forEach(visit);
  };
  geo.features.forEach(f => { if (f.geometry && 'coordinates' in f.geometry) visit(f.geometry.coordinates); });
  return [[west, south], [east, north]];
}
function center(f: GeoJSON.Feature): [number, number] | null {
  const g = f.geometry;
  if (!g || (g.type !== 'Polygon' && g.type !== 'MultiPolygon')) return null;
  const polygons = g.type === 'Polygon' ? [g.coordinates] : g.coordinates;
  let best: number[][] = [];
  for (const p of polygons) if (p[0].length > best.length) best = p[0];
  if (!best.length) return null;
  return [best.reduce((sum, p) => sum + p[0], 0) / best.length, best.reduce((sum, p) => sum + p[1], 0) / best.length];
}

export default function MapView({ base, areas, theme, openCity }: { base: BaseInfo; areas: Area[]; theme: 'claro' | 'escuro'; openCity: (city: string) => void }) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<maplibregl.Map | null>(null);
  const [geometries, setGeometries] = useState<[GeoJSON.FeatureCollection, GeoJSON.FeatureCollection] | null>(null);
  const [metric, setMetric] = useState<Metric>('contatos');
  const [selected, setSelected] = useState<Area | null>(null);
  const [hover, setHover] = useState<Area | null>(null);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [ready, setReady] = useState(false);
  const ramp = ramps[theme];

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([api<GeoJSON.FeatureCollection>('malhas/malha_23.geojson', undefined, controller.signal), api<GeoJSON.FeatureCollection>('malhas/malha_br.geojson', undefined, controller.signal)])
      .then(v => { setGeometries(v); setError(''); }).catch(e => { if (!controller.signal.aborted) setError(e.message); });
    return () => controller.abort();
  }, [base.gerado_em]);

  useEffect(() => {
    if (!container.current || !geometries || !areas.length) return;
    const [ce, brasil] = geometries;
    const byCode = new Map(areas.map(a => [a.codigo, a]));
    const max = Math.max(...areas.map(a => a[metric]), 1);
    const data: GeoJSON.FeatureCollection = { ...ce, features: ce.features.map(f => {
      const code = String(f.properties?.codarea ?? '');
      const area = byCode.get(code);
      return { ...f, id: Number(code), properties: { ...f.properties, nome: area?.nome ?? '', intensidade: area ? (metric === 'score_medio' ? area[metric] / max : Math.log1p(area[metric]) / Math.log1p(max)) : 0 } };
    }) };
    const colors = ramps[theme];
    const dark = theme === 'escuro';
    let disposed = false;
    setReady(false);
    let m: maplibregl.Map;
    try {
      m = new maplibregl.Map({ container: container.current, center: [-39.5, -5.2], zoom: 6, dragRotate: false, maxPitch: 0, attributionControl: false,
        style: { version: 8, sources: {}, layers: [{ id: 'sea', type: 'background', paint: { 'background-color': dark ? '#242b2b' : '#eaece5' } }] } });
    } catch {
      setError('Seu navegador não disponibilizou gráficos para o mapa. Explore os municípios pela lista.');
      return;
    }
    map.current = m;
    m.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'bottom-right');
    m.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-right');
    const markers: maplibregl.Marker[] = [];
    m.on('load', () => {
      if (disposed) return;
      m.addSource('brasil', { type: 'geojson', data: brasil });
      m.addLayer({ id: 'land', type: 'fill', source: 'brasil', paint: { 'fill-color': dark ? '#35312d' : '#e1dcd0' } });
      m.addLayer({ id: 'neighbors', type: 'line', source: 'brasil', paint: { 'line-color': dark ? '#50453b' : '#cdc5b6', 'line-width': 1 } });
      m.addSource('ceara', { type: 'geojson', data });
      m.addLayer({ id: 'cities', type: 'fill', source: 'ceara', paint: { 'fill-color': ['interpolate', ['linear'], ['get', 'intensidade'], 0, colors[0], .2, colors[1], .4, colors[2], .6, colors[3], .8, colors[4], 1, colors[5]] } });
      m.addLayer({ id: 'city-lines', type: 'line', source: 'ceara', paint: { 'line-color': dark ? '#dbbba5' : '#fffaf0', 'line-width': 0.8 } });
      // Três referências distantes entre si evitam sobreposição na vista do estado.
      const labeled = new Set(areas.filter(a => ['fortaleza', 'sobral', 'juazeiro do norte'].includes(a.nome.toLowerCase())).map(a => a.nome));
      for (const f of data.features) {
        const name = String(f.properties?.nome || '');
        if (!labeled.has(name)) continue;
        const point = center(f); if (!point) continue;
        const el = document.createElement('div'); el.className = 'map-label'; el.textContent = name;
        markers.push(new maplibregl.Marker({ element: el, anchor: 'center' }).setLngLat(point).addTo(m));
      }
      m.on('mousemove', 'cities', event => {
        m.getCanvas().style.cursor = 'pointer';
        const code = String(event.features?.[0]?.properties?.codarea || ''); setHover(byCode.get(code) ?? null);
      });
      m.on('mouseleave', 'cities', () => { m.getCanvas().style.cursor = ''; setHover(null); });
      m.on('click', 'cities', event => { const code = String(event.features?.[0]?.properties?.codarea || ''); setSelected(byCode.get(code) ?? null); });
      m.fitBounds(bounds(ce), { padding: 42, duration: 0 });
      m.once('idle', () => { if (!disposed) { setReady(true); setError(''); } });
    });
    m.on('error', () => { if (!disposed) setError('Não foi possível desenhar o mapa. Use a lista de municípios ao lado.'); });
    const observer = new ResizeObserver(() => m.resize()); observer.observe(container.current);
    return () => { disposed = true; observer.disconnect(); markers.forEach(marker => marker.remove()); m.remove(); map.current = null; };
  }, [geometries, areas, metric, theme]);

  const focus = selected ?? hover;
  const ranked = [...areas].filter(a => a.nome.toLocaleLowerCase('pt-BR').includes(search.toLocaleLowerCase('pt-BR'))).sort((a, b) => b[metric] - a[metric]);
  return <section className="map-layout" aria-label="Mapa e municípios do Ceará">
    <div className="map-panel">
      <div className="map-tools"><span className="map-stamp"><Compass size={16} /> CEARÁ <span>/ 184 municípios</span></span><button className="icon-button" aria-label="Enquadrar Ceará" onClick={() => { if (geometries) map.current?.fitBounds(bounds(geometries[0]), { padding: 42 }); }}><LocateFixed size={18} /></button></div>
      <div ref={container} className="map-canvas" aria-label="Mapa interativo do Ceará" />
      {(!ready || error) && <div className="map-status" role="status">{error || 'Preparando o mapa do Ceará…'}</div>}
      <div className="map-legend"><span>{metrics.find(m => m.id === metric)?.label}</span><div className="legend-ramp">{ramp.map(c => <i key={c} style={{ background: c }} />)}</div><div className="legend-ends"><span>Menor</span><span>Maior</span></div>{metric !== 'score_medio' && <small>Escala logarítmica</small>}</div>
      <div className="map-source">Malha IBGE · Dados Receita Federal</div>
    </div>
    <aside className="municipality-panel">
      <div className="panel-heading"><span className="eyebrow">Explore o território</span><h2>{focus?.nome || 'Onde estão as empresas?'}</h2><p>{focus ? `${number(focus.contatos)} empresas na base` : 'Selecione um município no mapa ou na lista.'}</p></div>
      {focus && <div className="city-summary"><dl><div><dt>Com celular</dt><dd>{percent(focus.com_celular, focus.contatos)}</dd></div><div><dt>Com e-mail</dt><dd>{percent(focus.com_email, focus.contatos)}</dd></div><div><dt>Score médio</dt><dd>{focus.score_medio.toFixed(1)}</dd></div></dl><button className="button primary" onClick={() => openCity(focus.nome)}>Explorar empresas <ArrowUpRight size={16} /></button>{selected && <button className="text-button" onClick={() => setSelected(null)}>Voltar ao Ceará</button>}</div>}
      <label className="field"><span>Comparar por</span><select value={metric} onChange={e => setMetric(e.target.value as Metric)}>{metrics.map(m => <option key={m.id} value={m.id}>{m.label}</option>)}</select></label>
      <input className="municipality-search" aria-label="Buscar município" placeholder="Buscar município…" value={search} onChange={e => setSearch(e.target.value)} />
      <ol className="municipality-list">{ranked.map((a, i) => <li key={a.codigo}><button className={selected?.codigo === a.codigo ? 'selected' : ''} onClick={() => setSelected(a)}><span className="rank-index">{String(i + 1).padStart(2, '0')}</span><span>{a.nome}</span><strong>{metric === 'score_medio' ? a[metric].toFixed(1) : number(a[metric])}</strong><ArrowUpRight size={14} /></button></li>)}</ol>
      {!ranked.length && <p className="muted">Nenhum município com esse nome.</p>}
    </aside>
  </section>;
}
