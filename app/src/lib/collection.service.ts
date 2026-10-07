import { Injectable, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { ApiService } from './api.service';
import { Store } from './store';
import type { UfInfo } from './api';

export type CollectionStatus = { status: 'ocioso' | 'rodando' | 'concluido' | 'erro'; ufs: string[]; linhas: string[] };

@Injectable({ providedIn: 'root' })
export class CollectionService {
  private readonly api = inject(ApiService);
  private readonly store = inject(Store);
  readonly state = signal<CollectionStatus>({ status: 'ocioso', ufs: [], linhas: [] });
  readonly starting = signal(false);
  readonly error = signal('');
  readonly updated = new Subject<void>();
  private loaded = false;
  private timer?: ReturnType<typeof setTimeout>;

  load() { if (!this.loaded) { this.loaded = true; this.refresh(); } }
  start(ufs: string[], reaproveitar: boolean) {
    if (!ufs.length || this.starting() || this.state().status === 'rodando') return;
    this.starting.set(true); this.error.set('');
    this.api.request<CollectionStatus>('coleta', { ufs, reaproveitar }).subscribe({
      next: state => { this.starting.set(false); this.state.set(state); this.schedule(); },
      error: error => { this.starting.set(false); this.error.set(error.message); this.refresh(false); }
    });
  }
  private schedule() { clearTimeout(this.timer); this.timer = setTimeout(() => this.refresh(), 2000); }
  refresh(clearError = true) {
    this.api.request<CollectionStatus>('coleta').subscribe({
      next: state => {
        if (clearError) this.error.set(''); this.state.set(state);
        if (state.status === 'rodando') this.schedule();
        else if (state.status === 'concluido') {
          this.api.request<UfInfo[]>('ufs').subscribe({ next: states => {
            this.store.ufs.set(states);
            if (!states.some(s => s.uf === this.store.uf() && s.disponivel))
              this.store.switchUf(states.find(s => s.disponivel && state.ufs.includes(s.uf))?.uf ?? this.store.uf());
            this.updated.next();
          }, error: error => this.error.set(error.message) });
        }
      }, error: error => { this.error.set(error.message); if (this.state().status === 'rodando') this.schedule(); }
    });
  }
}
