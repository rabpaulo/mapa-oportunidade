import type { ExpressionSpecification, LayerSpecification, StyleSpecification } from 'maplibre-gl';
import type { Area } from './api';
import type * as GeoJSON from 'geojson';

export type MapTheme = 'claro' | 'escuro';
export type MapMetric = 'contatos' | 'com_celular' | 'sem_dominio' | 'score_medio';

export const mapRamps = {
  claro: ['#e5efff', '#bdd5fc', '#8cb5f9', '#5d94f2', '#3478e5', '#1d4ed8'],
  escuro: ['#1b2a40', '#203d63', '#28558c', '#2f6db5', '#3984da', '#60a5fa'],
};

// Estilos neutros compatíveis com MapLibre, como os mapas da referência mapcn.
export const basemapUrls = {
  claro: 'https://tiles.openfreemap.org/styles/positron',
  escuro: 'https://tiles.openfreemap.org/styles/dark',
};

export function cityData(geometry: GeoJSON.FeatureCollection, areas: Area[], metric: MapMetric): GeoJSON.FeatureCollection {
  const byCode = new Map(areas.map(area => [area.codigo, area]));
  const max = Math.max(...areas.map(area => area[metric]), 1);
  return { ...geometry, features: geometry.features.map(feature => {
    const code = String(feature.properties?.codarea ?? '');
    const area = byCode.get(code);
    return { ...feature, id: Number(code), properties: { ...feature.properties, nome: area?.nome ?? '',
      intensidade: area ? (metric === 'score_medio' ? area[metric] / max : Math.log1p(area[metric]) / Math.log1p(max)) : 0 } };
  }) };
}

export function cityColors(theme: MapTheme): ExpressionSpecification {
  const colors = mapRamps[theme];
  return ['interpolate', ['linear'], ['get', 'intensidade'], 0, colors[0], .2, colors[1], .4, colors[2], .6, colors[3], .8, colors[4], 1, colors[5]];
}

export function mapStyle(theme: MapTheme, brasil: GeoJSON.FeatureCollection, cities: GeoJSON.FeatureCollection, basemap?: StyleSpecification): StyleSpecification {
  const dark = theme === 'escuro';
  const overlays: LayerSpecification[] = [
    { id: 'cities', type: 'fill', source: 'ceara', paint: { 'fill-color': cityColors(theme),
      'fill-opacity': basemap ? ['interpolate', ['linear'], ['zoom'], 6, .65, 9, .3, 12, .12] : .9 } },
    { id: 'city-lines', type: 'line', source: 'ceara', paint: { 'line-color': dark ? '#93b8ea' : '#4e7fbe', 'line-opacity': .45, 'line-width': .7 } },
    { id: 'city-highlight', type: 'line', source: 'ceara', filter: ['==', ['get', 'codarea'], ''],
      paint: { 'line-color': dark ? '#bfdbfe' : '#1d4ed8', 'line-width': 2.5 } },
  ];
  const background: LayerSpecification[] = basemap?.layers ?? [
    { id: 'sea', type: 'background', paint: { 'background-color': dark ? '#20272e' : '#e4e8ec' } },
    { id: 'land', type: 'fill', source: 'brasil', paint: { 'fill-color': dark ? '#121519' : '#f5f6f7' } },
    { id: 'neighbors', type: 'line', source: 'brasil', paint: { 'line-color': dark ? '#363d46' : '#c9cfd7', 'line-width': 1 } },
  ];
  // As ruas e os nomes continuam legíveis acima da comparação dos municípios.
  const labels = background.findIndex(layer => layer.type === 'symbol');
  const insertion = labels < 0 ? background.length : labels;
  return { ...basemap, version: 8, sources: { ...basemap?.sources,
    brasil: { type: 'geojson', data: brasil },
    ceara: { type: 'geojson', data: cities, attribution: 'Malhas: IBGE · Dados: Receita Federal' },
  }, layers: [...background.slice(0, insertion), ...overlays, ...background.slice(insertion)] };
}
