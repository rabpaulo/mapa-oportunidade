import { AfterViewInit, Component, computed, ElementRef, inject, input, OnDestroy, output, viewChild } from '@angular/core';
import { Store } from '../lib/store';
import { type Contact } from '../lib/api';
import { IconComponent } from './icon.component';

@Component({
  selector: 'ceara-contact-details',
  imports: [IconComponent],
  template: `
    <div class="detail-backdrop" (click)="close.emit()">
      <aside #panel tabindex="-1" class="contact-detail" role="dialog" aria-modal="true" [attr.aria-label]="'Detalhes de ' + contact().nome" (click)="$event.stopPropagation()">
        <header><span class="eyebrow">Perfil da empresa</span><button class="icon-button" aria-label="Fechar detalhes" (click)="close.emit()"><ceara-icon name="x" [size]="19" /></button></header>
        <span class="score large">{{ contact().score }}<small>/100</small></span><h2>{{ contact().nome }}</h2><p class="muted">{{ contact().empresa }}</p><span class="tag">{{ contact().segmento }}</span>
        <dl class="detail-list">
          @for (field of fields(); track field[0]) { <div><dt>{{ field[0] }}</dt><dd>{{ value(field[1]) }}</dd></div> }
        </dl>
        <div class="detail-actions">
          @if (phoneHref(); as phone) { <a class="button" [href]="phone">Ligar</a> }
          @if (contact().whatsapp) { <a class="button primary" [href]="contact().whatsapp" target="_blank" rel="noopener noreferrer">Abrir WhatsApp <ceara-icon name="arrow-up-right" /></a> }
          @if (contact().email) { <a class="button" [href]="'mailto:' + contact().email"><ceara-icon name="mail" /> Enviar e-mail</a> }
        </div>
        <div class="data-note"><strong>Critérios de priorização</strong><p>{{ contact().oportunidade }}</p><p>{{ store.restricted() ? 'O score usa atividade, porte e ano de abertura. Contatos e endereços não são publicados.' : 'O score prioriza serviços digitais. Domínio próprio é inferido pelo e-mail e não comprova presença ou ausência de site.' }}</p></div>
        @if (store.publicDeployment()) { <button class="text-button" (click)="close.emit(); store.screen.set('privacidade')">Solicitar correção ou retirada</button> }
      </aside>
    </div>`
})
export class ContactDetailsComponent implements AfterViewInit, OnDestroy {
  readonly store = inject(Store);
  readonly contact = input.required<Contact>();
  readonly close = output<void>();
  readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');
  readonly fields = computed<[string, keyof Contact][]>(() => this.store.restricted()
    ? [['CNPJ', 'cnpj'], ['Município', 'cidade'], ['Porte', 'porte'], ['Ano de abertura', 'abertura']]
    : [['CNPJ', 'cnpj'], ['Município', 'cidade'], ['Bairro', 'bairro'], ['Endereço', 'endereco'], ['Porte', 'porte'], ['Ano de abertura', 'abertura'], ['Telefone', 'telefone'], ['E-mail', 'email'], ['WhatsApp', 'whatsapp'], ['Código do município (Receita)', 'cod_municipio'], ['ID na base', 'id'], ['E-mail com domínio próprio', 'dominio_proprio'], ['Tem celular', 'tem_celular'], ['Texto de busca', 'busca']]);
  value(field: keyof Contact): string {
    const value = this.contact()[field];
    return typeof value === 'boolean' ? value ? 'Sim' : 'Não' : value === null || value === undefined || value === '' ? 'Não informado' : String(value);
  }
  phoneHref(): string {
    const digits = (this.contact().telefone || '').split('/')[0].replace(/\D/g, '');
    return digits ? `tel:${digits.length === 10 || digits.length === 11 ? '+55' : '+'}${digits}` : '';
  }
  private readonly before = document.activeElement as HTMLElement | null;
  private readonly onKey = (event: KeyboardEvent) => {
    if (event.key === 'Escape') this.close.emit();
    if (event.key !== 'Tab') return;
    const panel = this.panel().nativeElement;
    const nodes = Array.from(panel.querySelectorAll<HTMLElement>('button, a[href]'));
    const first = nodes[0], last = nodes[nodes.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === panel)) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && (document.activeElement === last || document.activeElement === panel)) { event.preventDefault(); first?.focus(); }
  };
  ngAfterViewInit() { this.panel().nativeElement.focus(); document.addEventListener('keydown', this.onKey); }
  ngOnDestroy() { document.removeEventListener('keydown', this.onKey); this.before?.focus(); }
}
