import { AfterViewInit, Component, ElementRef, input, OnDestroy, output, viewChild } from '@angular/core';
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
          @for (field of fields; track field[0]) { <div><dt>{{ field[0] }}</dt><dd>{{ contact()[field[1]] || 'Não informado' }}</dd></div> }
        </dl>
        <div class="detail-actions">
          @if (contact().whatsapp) { <a class="button primary" [href]="contact().whatsapp" target="_blank" rel="noopener noreferrer">Abrir WhatsApp <ceara-icon name="arrow-up-right" /></a> }
          @if (contact().email) { <a class="button" [href]="'mailto:' + contact().email"><ceara-icon name="mail" /> Enviar e-mail</a> }
        </div>
        <div class="data-note"><strong>Critérios de priorização</strong><p>{{ contact().oportunidade }}</p><p>O score prioriza serviços digitais. Domínio próprio é inferido pelo e-mail e não comprova presença ou ausência de site.</p></div>
      </aside>
    </div>`
})
export class ContactDetailsComponent implements AfterViewInit, OnDestroy {
  readonly contact = input.required<Contact>();
  readonly close = output<void>();
  readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');
  readonly fields: [string, keyof Contact][] = [['CNPJ', 'cnpj'], ['Município', 'cidade'], ['Bairro', 'bairro'], ['Endereço', 'endereco'], ['Porte', 'porte'], ['Ano de abertura', 'abertura'], ['Telefone', 'telefone'], ['E-mail', 'email']];
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
