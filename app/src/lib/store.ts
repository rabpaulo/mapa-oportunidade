import { Injectable, signal } from '@angular/core';
import { emptyFilters, type ChatResponse, type Filters, type UfInfo } from './api';

export type Screen = 'mapa' | 'empresas' | 'ramos' | 'assistente' | 'base' | 'privacidade';
export type ChatTurn = { role: 'user' | 'model'; text: string; response?: ChatResponse };

@Injectable({ providedIn: 'root' })
export class Store {
  readonly uf = signal('CE');
  readonly ufs = signal<UfInfo[]>([]);
  switchUf(uf: string) { this.uf.set(uf); this.filters.set(emptyFilters()); this.turns.set([]); }
  readonly screen = signal<Screen>('mapa');
  readonly filters = signal<Filters>(emptyFilters());
  readonly turns = signal<ChatTurn[]>([]);
  readonly restricted = signal(false);
  readonly publicDeployment = signal(false);
  explore(filters: Filters) { this.filters.set(filters); this.screen.set('empresas'); }
}
