import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BaseInfo, DownloadInfo, date, number, repositoryUrl } from '../lib/api';
import { ApiService } from '../lib/api.service';
import { CollectionService } from '../lib/collection.service';
import { Store } from '../lib/store';
import { IconComponent } from './icon.component';

@Component({
  selector: 'ceara-base', imports: [FormsModule, IconComponent],
  templateUrl: './base.component.html'
})
export class BaseComponent {
  readonly base = input.required<BaseInfo>();
  readonly collection = inject(CollectionService);
  readonly store = inject(Store);
  private readonly api = inject(ApiService);
  readonly selected = signal<string[]>([]);
  readonly reuse = signal(false);
  readonly downloads = signal<DownloadInfo[]>([]);
  readonly downloadError = signal('');
  readonly number = number;
  readonly date = date;
  readonly repositoryUrl = repositoryUrl;
  readonly downloadLabels: Record<string, string> = { contatos: 'Empresas · CSV', municipios: 'Municípios · CSV', segmentos: 'Ramos · CSV', banco: 'Banco SQLite', metadados: 'Metadados', malha: 'Malha da UF · GeoJSON', brasil: 'Malha do Brasil · GeoJSON' };
  constructor() {
    effect(onCleanup => {
      const base = this.base();
      this.downloads.set([]); this.downloadError.set('');
      if (!base.hospedagem_publica && base.disponivel) {
        const subscription = this.api.request<DownloadInfo[]>('downloads').subscribe({
          next: items => this.downloads.set(items), error: error => this.downloadError.set(error.message)
        });
        onCleanup(() => subscription.unsubscribe());
      }
    });
  }
  toggle(uf: string) { this.selected.update(states => states.includes(uf) ? states.filter(s => s !== uf) : [...states, uf]); }
  selectAll() { this.selected.set(this.store.ufs().map(state => state.uf)); }
}
