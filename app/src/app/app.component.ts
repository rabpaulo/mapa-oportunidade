import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ApiService } from '../lib/api.service';
import { Store, type Screen } from '../lib/store';
import { date, emptyFilters, number, percent, repositoryUrl, type Area, type BaseInfo } from '../lib/api';
import { IconComponent } from '../componentes/icon.component';
import { CompaniesComponent } from '../componentes/companies.component';
import { SegmentsComponent } from '../componentes/segments.component';
import { AssistantComponent } from '../componentes/assistant.component';
import { BaseComponent } from '../componentes/base.component';
import { MapComponent } from '../componentes/map.component';
import { PrivacyComponent } from '../componentes/privacy.component';

@Component({
  selector: 'ceara-root',
  imports: [IconComponent, CompaniesComponent, SegmentsComponent, AssistantComponent, BaseComponent, MapComponent, PrivacyComponent],
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  readonly store = inject(Store);
  private readonly api = inject(ApiService);
  readonly base = signal<BaseInfo | null>(null);
  readonly areas = signal<Area[]>([]);
  readonly segments = signal<Area[]>([]);
  readonly error = signal('');
  readonly theme = signal<'claro' | 'escuro'>('escuro');
  readonly number = number;
  readonly percent = percent;
  readonly date = date;
  readonly repositoryUrl = repositoryUrl;
  private readonly navigation: { id: Screen; title: string; icon: string }[] = [
    { id: 'mapa', title: 'Mapa', icon: 'map' }, { id: 'empresas', title: 'Empresas', icon: 'users' },
    { id: 'ramos', title: 'Ramos', icon: 'layers' }, { id: 'assistente', title: 'Assistente', icon: 'message-square' },
    { id: 'base', title: 'Base', icon: 'database' }
  ];
  readonly nav = computed(() => this.navigation.filter(item => !this.store.publicDeployment() || item.id !== 'assistente'));
  private readonly titles = {
    mapa: ['Empresas por município', 'Compare os municípios e consulte suas empresas.'],
    empresas: ['Empresas', 'Filtre por município e atividade e consulte os dados de cada empresa.'],
    ramos: ['Ramos de atividade', 'Compare o volume de empresas e a disponibilidade de contatos por atividade.'],
    assistente: ['Assistente', 'Consulte os dados do Ceará ou faça uma pergunta geral.'],
    base: ['Base do Ceará', 'Confira a origem e a versão do cadastro disponível.'],
    privacidade: ['Privacidade', 'Conheça o uso dos dados e o canal de atendimento.']
  };
  readonly heading = computed(() => this.titles[this.store.screen()]);
  ngOnInit() {
    try { this.theme.set(localStorage.getItem('ceara-tema') === 'claro' ? 'claro' : 'escuro'); } catch { }
    document.documentElement.dataset['tema'] = this.theme();
    this.reload();
  }
  reload() {
    this.error.set('');
    this.api.request<BaseInfo>('base').subscribe({
      next: base => {
        this.base.set(base);
        this.store.restricted.set(!!base.publicacao_restrita);
        this.store.publicDeployment.set(!!base.hospedagem_publica);
        if (base.disponivel) forkJoin({ areas: this.api.request<Area[]>('municipios'), segments: this.api.request<Area[]>('ramos') })
          .subscribe({ next: values => { this.areas.set(values.areas); this.segments.set(values.segments); }, error: error => this.error.set(error.message) });
      }, error: error => this.error.set(error.message)
    });
  }
  toggleTheme() {
    this.theme.update(theme => theme === 'claro' ? 'escuro' : 'claro');
    document.documentElement.dataset['tema'] = this.theme();
    try { localStorage.setItem('ceara-tema', this.theme()); } catch { }
  }
  openCity(city: string) { this.store.explore({ ...emptyFilters(), cidade: city }); }
  openSegment(segment: string) { this.store.explore({ ...emptyFilters(), segmento: segment }); }
}
