import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { ApiService } from '../lib/api.service';
import { Store } from '../lib/store';
import { emptyFilters, filterLabel, number, type Area, type Contact, type Filters, type PageContacts } from '../lib/api';
import { IconComponent } from './icon.component';
import { ContactDetailsComponent } from './contact-details.component';

@Component({
  selector: 'ceara-companies', imports: [ReactiveFormsModule, IconComponent, ContactDetailsComponent], templateUrl: './companies.component.html'
})
export class CompaniesComponent {
  readonly store = inject(Store);
  private readonly api = inject(ApiService);
  readonly areas = input.required<Area[]>();
  readonly segments = input.required<Area[]>();
  readonly generation = input<string>();
  readonly number = number;
  readonly filterLabel = filterLabel;
  readonly data = signal<PageContacts>({ total: 0, itens: [] });
  readonly facets = signal<{ municipios: Area[]; segmentos: Area[] } | null>(null);
  readonly page = signal(1);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly detail = signal<Contact | null>(null);
  readonly showFilters = signal(window.innerWidth > 760);
  readonly size = 50;
  readonly skeletons = Array.from({ length: 8 }, (_, i) => i);
  readonly ports = ['Microempresa', 'Pequeno porte', 'Medio/grande', 'Nao informado'];
  readonly checks = computed<[keyof Filters, string][]>(() => this.store.restricted() ? [] : [['somente_celular', 'Com celular'], ['somente_email', 'Com e-mail'], ['somente_sem_dominio', 'E-mail sem domínio próprio']]);
  readonly term = new FormControl(this.store.filters().termo, { nonNullable: true });
  readonly form = new FormGroup({
    cidade: new FormControl('', { nonNullable: true }), segmento: new FormControl('', { nonNullable: true }),
    porte: new FormControl('', { nonNullable: true }), bairro: new FormControl('', { nonNullable: true }),
    score_minimo: new FormControl(0, { nonNullable: true }), ano_minimo: new FormControl<number | null>(null),
    ano_maximo: new FormControl<number | null>(null), somente_celular: new FormControl(false, { nonNullable: true }),
    somente_email: new FormControl(false, { nonNullable: true }), somente_sem_dominio: new FormControl(false, { nonNullable: true }),
    ordem: new FormControl<Filters['ordem']>('score', { nonNullable: true })
  });
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.data().total / this.size)));
  readonly range = computed(() => this.data().total ? `${number((this.page() - 1) * this.size + 1)}–${number(Math.min(this.page() * this.size, this.data().total))} de ${number(this.data().total)}` : '0 resultados');
  readonly cities = computed(() => this.addMissing(this.facets()?.municipios ?? this.areas(), this.store.filters().cidade));
  readonly branches = computed(() => this.addMissing(this.facets()?.segmentos ?? this.segments(), this.store.filters().segmento));
  private readonly query = computed(() => ({ filtros: this.store.filters(), pagina: this.page(), por_pagina: this.size, generation: this.generation() }));

  constructor() {
    effect(() => {
      const filters = this.store.filters();
      this.form.patchValue(filters, { emitEvent: false });
      this.term.setValue(filters.termo, { emitEvent: false });
    });
    effect(() => { this.generation(); untracked(() => { this.page.set(1); this.detail.set(null); }); });
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => { this.page.set(1); this.store.filters.update(filters => ({ ...filters, ...this.form.getRawValue() })); });
    this.term.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed()).subscribe(termo => {
      this.page.set(1); this.store.filters.update(filters => ({ ...filters, termo }));
    });
    toObservable(this.query).pipe(switchMap(query => {
      this.loading.set(true); this.error.set('');
      return this.api.request<PageContacts>('contatos/buscar', { filtros: query.filtros, pagina: query.pagina, por_pagina: query.por_pagina })
        .pipe(catchError(error => { this.error.set(error.message); return of(null); }));
    }), takeUntilDestroyed()).subscribe(data => { if (data) this.data.set(data); this.loading.set(false); });
    toObservable(this.store.filters).pipe(switchMap(filters => this.api.request<{ municipios: Area[]; segmentos: Area[] }>('facetas', filters).pipe(catchError(() => of(null)))), takeUntilDestroyed())
      .subscribe(facets => this.facets.set(facets));
  }
  private addMissing(list: Area[], value: string): Area[] {
    return value && !list.some(area => area.nome === value) ? [{ codigo: value, nome: value, contatos: 0, com_email: 0, com_celular: 0, sem_dominio: 0, score_medio: 0 }, ...list] : list;
  }
  clear() { this.page.set(1); this.store.filters.set(emptyFilters()); }
  retry() { this.store.filters.update(filters => ({ ...filters })); }
}
