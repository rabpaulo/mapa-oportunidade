import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Store } from '../lib/store';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Area, number, percent } from '../lib/api';
import { IconComponent } from './icon.component';

@Component({
  selector: 'ceara-segments', imports: [ReactiveFormsModule, IconComponent],
  template: `
    <section class="segments-panel">
      <div class="table-toolbar"><div class="search-input"><ceara-icon name="search" [size]="18" /><input aria-label="Buscar ramo" placeholder="Buscar ramo de atividade…" [formControl]="searchControl" /></div>
        <label class="sort-field">Comparar por <select aria-label="Ordenar ramos" [formControl]="orderControl"><option value="contatos">Empresas</option><option value="score_medio">Score médio</option>@if (!store.restricted()) { <option value="com_celular">Com celular</option><option value="com_email">Com e-mail</option> }</select></label></div>
      <div class="table-scroll"><table class="data-table segments-table"><thead><tr><th>Ramo de atividade</th><th class="numeric">Empresas</th><th>Distribuição</th>@if (!store.restricted()) { <th class="numeric">Celular</th><th class="numeric">E-mail</th><th class="numeric">Sem domínio*</th> }<th class="numeric">Score médio</th><th><span class="sr-only">Explorar</span></th></tr></thead>
        <tbody>@for (s of visible(); track s.nome; let i = $index) { <tr><td><button class="company-name" (click)="openSegment.emit(s.nome)"><span class="rank-index">{{ rank(i) }}</span>{{ s.nome }}</button></td><td class="numeric strong">{{ number(s.contatos) }}</td><td><div class="distribution-bar"><i [style.width.%]="width(s.contatos)"></i></div></td>@if (!store.restricted()) { <td class="numeric">{{ percent(s.com_celular, s.contatos) }}</td><td class="numeric">{{ percent(s.com_email, s.contatos) }}</td><td class="numeric">{{ percent(s.sem_dominio, s.contatos) }}</td> }<td class="numeric">{{ s.score_medio.toFixed(1) }}</td><td><button class="icon-button" [attr.aria-label]="'Explorar ' + s.nome" (click)="openSegment.emit(s.nome)"><ceara-icon name="arrow-up-right" [size]="17" /></button></td></tr> }</tbody></table></div>
      @if (!visible().length) { <div class="empty"><ceara-icon name="layers" [size]="28" /><h2>Nenhum ramo encontrado</h2><p>Tente buscar por uma atividade mais ampla.</p></div> }
      <p class="table-footnote">{{ store.restricted() ? 'Os ramos são agrupamentos comerciais derivados do CNAE. A comparação usa apenas empresas do recorte público, sem empresários individuais.' : '* E-mail em provedor gratuito. Esse indicador não comprova ausência de site. Os ramos são agrupamentos comerciais derivados do CNAE.' }}</p>
    </section>`
})
export class SegmentsComponent {
  readonly store = inject(Store);
  readonly segments = input.required<Area[]>();
  readonly openSegment = output<string>();
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly orderControl = new FormControl<'contatos' | 'score_medio' | 'com_celular' | 'com_email'>('contatos', { nonNullable: true });
  private readonly search = signal('');
  private readonly order = signal(this.orderControl.value);
  readonly visible = computed(() => this.segments().filter(s => s.nome.toLocaleLowerCase('pt-BR').includes(this.search().toLocaleLowerCase('pt-BR'))).sort((a, b) => b[this.order()] - a[this.order()]));
  readonly maximum = computed(() => Math.max(...this.segments().map(s => s.contatos), 1));
  readonly number = number;
  readonly percent = percent;
  constructor() {
    this.searchControl.valueChanges.pipe(takeUntilDestroyed()).subscribe(v => this.search.set(v));
    this.orderControl.valueChanges.pipe(takeUntilDestroyed()).subscribe(v => this.order.set(v));
  }
  rank(index: number) { return String(index + 1).padStart(2, '0'); }
  width(count: number) { return Math.max(2, count / this.maximum() * 100); }
}
