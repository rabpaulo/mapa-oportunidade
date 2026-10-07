import { afterNextRender, Component, computed, inject, Injector, OnInit, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../lib/api.service';
import { Store, type Screen } from '../lib/store';
import { date, emptyFilters, number, percent, repositoryUrl, type Area, type BaseInfo, type UfInfo } from '../lib/api';
import { IconComponent } from '../componentes/icon.component';
import { CompaniesComponent } from '../componentes/companies.component';
import { SegmentsComponent } from '../componentes/segments.component';
import { AssistantComponent } from '../componentes/assistant.component';
import { BaseComponent } from '../componentes/base.component';
import { MapComponent } from '../componentes/map.component';
import { PrivacyComponent } from '../componentes/privacy.component';
import { IntroComponent } from '../componentes/intro.component';
import { CollectionService } from '../lib/collection.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

@Component({
  selector: 'ceara-root',
  imports: [FormsModule, IconComponent, CompaniesComponent, SegmentsComponent, AssistantComponent, BaseComponent, MapComponent, PrivacyComponent, IntroComponent],
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  readonly showIntro = signal(true);
  readonly projectOpened = signal(false);
  readonly store = inject(Store);
  private readonly api = inject(ApiService);
  private readonly injector = inject(Injector);
  private readonly collection = inject(CollectionService);
  private readonly untilDestroyed = takeUntilDestroyed<void>();
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
  readonly nav = this.navigation;
  private readonly titles = {
    mapa: ['Empresas por município', 'Compare os municípios e consulte suas empresas.'],
    empresas: ['Empresas', 'Filtre por município e atividade e consulte os dados de cada empresa.'],
    ramos: ['Ramos de atividade', 'Compare o volume de empresas e a disponibilidade de contatos por atividade.'],
    assistente: ['Assistente', 'Consulte os dados da UF selecionada ou faça uma pergunta geral.'],
    base: ['Base', 'Confira a origem e a versão do cadastro disponível.'],
    privacidade: ['Privacidade', 'Conheça o uso dos dados e o canal de atendimento.']
  };
  readonly heading = computed(() => this.store.restricted() && this.store.screen() === 'ramos'
    ? ['Ramos de atividade', 'Compare o volume de empresas por atividade.'] : this.titles[this.store.screen()]);
  private requestVersion = 0;
  enterProject(screen: Screen) {
    this.store.screen.set(screen);
    this.projectOpened.set(true);
  }
  finishIntro() {
    this.showIntro.set(false);
    window.scrollTo({ top: 0, behavior: 'instant' });
    afterNextRender(() => document.getElementById('main')?.focus({ preventScroll: true }), { injector: this.injector });
  }
  ngOnInit() {
    this.collection.updated.pipe(this.untilDestroyed).subscribe(() => this.reload());
    try { this.theme.set(localStorage.getItem('ceara-tema') === 'claro' ? 'claro' : 'escuro'); } catch { }
    document.documentElement.dataset['tema'] = this.theme();
    this.api.request<UfInfo[]>('ufs').subscribe({ next: states => {
      this.store.ufs.set(states);
      if (!states.some(s => s.uf === this.store.uf() && s.disponivel)) this.store.switchUf(states.find(s => s.disponivel)?.uf ?? 'CE');
      this.reload();
    }, error: error => this.error.set(error.message) });
  }
  changeUf(uf: string) {
    this.store.switchUf(uf); this.base.set(null); this.areas.set([]); this.segments.set([]); this.reload();
  }
  reload() {
    const version = ++this.requestVersion;
    this.error.set('');
    this.api.request<UfInfo[]>('ufs').subscribe({ next: states => { if (version === this.requestVersion) this.store.ufs.set(states); } });
    this.api.request<BaseInfo>('base').subscribe({
      next: base => {
        if (version !== this.requestVersion) return;
        this.base.set(base);
        this.store.restricted.set(!!base.publicacao_restrita);
        this.store.publicDeployment.set(!!base.hospedagem_publica);
        if (!base.hospedagem_publica) this.collection.load();
        if (base.disponivel) forkJoin({ areas: this.api.request<Area[]>('municipios'), segments: this.api.request<Area[]>('ramos') })
          .subscribe({ next: values => { if (version !== this.requestVersion) return; this.areas.set(values.areas); this.segments.set(values.segments); }, error: error => { if (version === this.requestVersion) this.error.set(error.message); } });
      }, error: error => { if (version === this.requestVersion) this.error.set(error.message); }
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
