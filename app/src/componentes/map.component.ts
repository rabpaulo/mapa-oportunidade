import { AfterViewInit, Component, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin } from 'rxjs';
import * as maplibregl from 'maplibre-gl';
import type * as GeoJSON from 'geojson';
import { ApiService } from '../lib/api.service';
import { Area, BaseInfo, number, percent } from '../lib/api';
import { basemapUrls, cityColors, cityData, mapRamps, mapStyle, MapMetric, MapTheme } from '../lib/map-style';
import { IconComponent } from './icon.component';

maplibregl.setWorkerUrl('/maplibre/maplibre-gl-worker.mjs');

// Geometria e enquadramento adaptados de código © 2026 Ivo Braatz (AGPL-3.0-only).
function bounds(geo: GeoJSON.FeatureCollection): maplibregl.LngLatBoundsLike {
  let west = 180, south = 90, east = -180, north = -90;
  const visit = (coords: unknown): void => {
    if (!Array.isArray(coords)) return;
    if (typeof coords[0] === 'number') {
      const [x, y] = coords as number[];
      west = Math.min(west, x); south = Math.min(south, y); east = Math.max(east, x); north = Math.max(north, y);
    } else coords.forEach(visit);
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

@Component({ selector: 'ceara-map', imports: [ReactiveFormsModule, IconComponent], templateUrl: './map.component.html' })
export class MapComponent implements AfterViewInit {
  readonly base = input.required<BaseInfo>();
  readonly areas = input.required<Area[]>();
  readonly theme = input.required<MapTheme>();
  readonly openCity = output<string>();
  private readonly api = inject(ApiService);
  private readonly container = viewChild<ElementRef<HTMLDivElement>>('container');
  private readonly viewReady = signal(false);
  private readonly geometries = signal<[GeoJSON.FeatureCollection, GeoJSON.FeatureCollection] | null>(null);
  private readonly mapCreated = signal(0);
  private map: maplibregl.Map | null = null;
  readonly metric = signal<MapMetric>('contatos');
  readonly metricControl = new FormControl<MapMetric>('contatos', { nonNullable: true });
  readonly searchControl = new FormControl('', { nonNullable: true });
  private readonly search = signal('');
  readonly selected = signal<Area | null>(null);
  readonly hover = signal<Area | null>(null);
  readonly focus = computed(() => this.selected() ?? this.hover());
  readonly error = signal('');
  readonly ready = signal(false);
  readonly basemapMode = signal<'ruas' | 'offline'>('ruas');
  readonly basemapStatus = signal<'carregando' | 'online' | 'local'>('carregando');
  readonly number = number;
  readonly percent = percent;
  readonly metrics = computed<{ id: MapMetric; label: string }[]>(() => this.base().publicacao_restrita
    ? [{ id: 'contatos', label: 'Total de empresas' }, { id: 'score_medio', label: 'Score médio' }]
    : [{ id: 'contatos', label: 'Total de empresas' }, { id: 'com_celular', label: 'Com celular' }, { id: 'sem_dominio', label: 'E-mail sem domínio próprio' }, { id: 'score_medio', label: 'Score médio' }]);
  readonly ramp = computed(() => mapRamps[this.theme()]);
  readonly metricLabel = computed(() => this.metrics().find(m => m.id === this.metric())?.label);
  readonly ranked = computed(() => this.areas().filter(a => a.nome.toLocaleLowerCase('pt-BR').includes(this.search().toLocaleLowerCase('pt-BR'))).sort((a, b) => b[this.metric()] - a[this.metric()]));
  constructor() {
    effect(() => { if (this.base().hospedagem_publica) this.basemapMode.set('offline'); });
    this.metricControl.valueChanges.pipe(takeUntilDestroyed()).subscribe(v => this.metric.set(v));
    this.searchControl.valueChanges.pipe(takeUntilDestroyed()).subscribe(v => this.search.set(v));
    effect(onCleanup => {
      this.base().gerado_em;
      const subscription = forkJoin([this.api.request<GeoJSON.FeatureCollection>('malhas/malha_' + (this.base().codigo_uf ?? '23') + '.geojson'), this.api.request<GeoJSON.FeatureCollection>('malhas/malha_br.geojson')])
        .subscribe({ next: v => { this.geometries.set(v); this.error.set(''); }, error: e => this.error.set(e.message) });
      onCleanup(() => subscription.unsubscribe());
    });
    effect(onCleanup => {
      const container = this.container()?.nativeElement, geometries = this.geometries(), areas = this.areas();
      if (!this.viewReady() || !container || !geometries || !areas.length) return;
      const [ce, brasil] = geometries, byCode = new Map(areas.map(a => [a.codigo, a]));
      const data = cityData(ce, areas, untracked(this.metric));
      let disposed = false, m: maplibregl.Map;
      this.ready.set(false);
      try {
        m = new maplibregl.Map({ container, center: [-39.5, -5.2], zoom: 6, dragRotate: false, maxPitch: 0, attributionControl: false,
          locale: { 'NavigationControl.ZoomIn': 'Aproximar mapa', 'NavigationControl.ZoomOut': 'Afastar mapa', 'FullscreenControl.Enter': 'Abrir mapa em tela cheia', 'FullscreenControl.Exit': 'Sair da tela cheia', 'AttributionControl.ToggleAttribution': 'Mostrar créditos do mapa' },
          style: mapStyle(untracked(this.theme), brasil, data) });
      } catch { this.error.set('Seu navegador não disponibilizou gráficos para o mapa. Explore os municípios pela lista.'); return; }
      this.map = m; this.mapCreated.update(v => v + 1);
      m.getCanvas().setAttribute('aria-label', 'Mapa interativo de ' + (this.base().nome_uf ?? this.base().uf ?? 'CE'));
      m.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right');
      m.addControl(new maplibregl.FullscreenControl({ container: container.parentElement! }), 'top-right');
      m.addControl(new maplibregl.AttributionControl({ compact: true }), 'bottom-right');
      m.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-right');
      const markers: maplibregl.Marker[] = [];
      m.on('style.load', () => {
        if (disposed) return;
        (m.getSource('ceara') as maplibregl.GeoJSONSource).setData(cityData(ce, areas, this.metric()));
        m.setPaintProperty('cities', 'fill-color', cityColors(this.theme()));
        m.setFilter('city-highlight', ['==', ['get', 'codarea'], this.focus()?.codigo ?? '']);
      });
      m.on('sourcedata', event => { if (!disposed && event.sourceId === 'ceara' && event.isSourceLoaded) { this.ready.set(true); this.error.set(''); } });
      const labeled = new Set(areas.filter(a => ['fortaleza', 'sobral', 'juazeiro do norte'].includes(a.nome.toLowerCase())).map(a => a.nome));
      for (const f of data.features) {
        const name = String(f.properties?.['nome'] || '');
        if (!labeled.has(name)) continue;
        const point = center(f); if (!point) continue;
        const el = document.createElement('button'); el.className = 'map-label'; el.type = 'button';
        el.textContent = name; el.setAttribute('aria-label', `Selecionar ${name}`);
        el.addEventListener('click', event => { event.stopPropagation(); this.selected.set(byCode.get(String(f.properties?.['codarea'])) ?? null); });
        markers.push(new maplibregl.Marker({ element: el, anchor: 'bottom' }).setLngLat(point).addTo(m));
      }
      m.on('mousemove', 'cities', event => { m.getCanvas().style.cursor = 'pointer'; this.hover.set(byCode.get(String(event.features?.[0]?.properties?.['codarea'] || '')) ?? null); });
      m.on('mouseleave', 'cities', () => { m.getCanvas().style.cursor = ''; this.hover.set(null); });
      m.on('click', 'cities', event => this.selected.set(byCode.get(String(event.features?.[0]?.properties?.['codarea'] || '')) ?? null));
      m.fitBounds(bounds(ce), { padding: { top: 80, bottom: 65, left: 35, right: 65 }, duration: 0 });
      m.on('error', event => {
        if (disposed) return;
        const sourceId = 'sourceId' in event && typeof event.sourceId === 'string' ? event.sourceId : undefined;
        const externalStyle = Object.keys(m.getStyle()?.sources ?? {}).some(id => !['ceara', 'brasil'].includes(id));
        if (sourceId && !['ceara', 'brasil'].includes(sourceId) && !externalStyle) return;
        if (externalStyle && (!sourceId || !['ceara', 'brasil'].includes(sourceId))) {
          this.basemapStatus.set('local'); m.setStyle(mapStyle(this.theme(), brasil, cityData(ce, areas, this.metric())));
        } else this.error.set('Não foi possível desenhar o mapa. Use a lista de municípios ao lado.');
      });
      const observer = new ResizeObserver(() => m.resize()); observer.observe(container);
      onCleanup(() => { disposed = true; observer.disconnect(); markers.forEach(marker => marker.remove()); m.remove(); this.map = null; });
    });
    effect(onCleanup => {
      this.mapCreated();
      const geometries = this.geometries(), theme = this.theme(), mode = this.basemapMode(), areas = this.areas(), m = this.map;
      if (!m || !geometries) return;
      const controller = new AbortController(); let disposed = false;
      const [ce, brasil] = geometries;
      const localStyle = () => mapStyle(theme, brasil, cityData(ce, areas, untracked(this.metric)));
      this.basemapStatus.set(mode === 'offline' ? 'local' : 'carregando'); m.setStyle(localStyle());
      if (mode === 'offline' || this.base().hospedagem_publica) return;
      const timeout = setTimeout(() => controller.abort(), 8000);
      fetch(basemapUrls[theme], { signal: controller.signal }).then(async response => {
        if (!response.ok) throw new Error('Mapa de ruas indisponível');
        const style: maplibregl.StyleSpecification = await response.json();
        if (controller.signal.aborted || disposed) return;
        m.setStyle(mapStyle(theme, brasil, cityData(ce, areas, this.metric()), style)); this.basemapStatus.set('online');
      }).catch(() => { if (this.map === m && !disposed) this.basemapStatus.set('local'); }).finally(() => clearTimeout(timeout));
      onCleanup(() => { disposed = true; clearTimeout(timeout); controller.abort(); });
    });
    effect(() => {
      this.mapCreated(); const geometries = this.geometries(), areas = this.areas(), metric = this.metric(), theme = this.theme(), focus = this.focus(), m = this.map;
      if (!m?.getLayer('cities') || !geometries) return;
      (m.getSource('ceara') as maplibregl.GeoJSONSource).setData(cityData(geometries[0], areas, metric));
      m.setPaintProperty('cities', 'fill-color', cityColors(theme));
      m.setFilter('city-highlight', ['==', ['get', 'codarea'], focus?.codigo ?? '']);
    });
  }
  ngAfterViewInit() { this.viewReady.set(true); }
  selectCity(area: Area) {
    this.selected.set(area); this.hover.set(null);
    const f = this.geometries()?.[0].features.find(f => String(f.properties?.['codarea']) === area.codigo);
    if (f) this.map?.fitBounds(bounds({ type: 'FeatureCollection', features: [f] }), { padding: 80, maxZoom: 10, duration: 800 });
  }
  resetMap() {
    this.selected.set(null); this.hover.set(null); const geometries = this.geometries();
    if (geometries) this.map?.fitBounds(bounds(geometries[0]), { padding: { top: 80, bottom: 65, left: 35, right: 65 }, duration: 800 });
  }
  rank(index: number) { return String(index + 1).padStart(2, '0'); }
}
