import { Component, ElementRef, Pipe, PipeTransform, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { marked } from 'marked';
import { BaseInfo, ChatResponse, Contact, AnalysisRow, filterLabel, number } from '../lib/api';
import { ApiService } from '../lib/api.service';
import { Store } from '../lib/store';
import { IconComponent } from './icon.component';
import { ContactDetailsComponent } from './contact-details.component';

@Pipe({ name: 'markdown' })
class MarkdownPipe implements PipeTransform {
  transform(text: string) { return marked.parse(text, { async: false }); }
}

@Component({
  selector: 'ceara-assistant', imports: [ReactiveFormsModule, IconComponent, ContactDetailsComponent, MarkdownPipe],
  templateUrl: './assistant.component.html'
})
export class AssistantComponent {
  readonly base = input.required<BaseInfo>();
  readonly store = inject(Store);
  private readonly api = inject(ApiService);
  readonly question = new FormControl('', { nonNullable: true });
  readonly hasQuestion = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly detail = signal<Contact | null>(null);
  readonly bottom = viewChild<ElementRef<HTMLDivElement>>('bottom');
  readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea');
  readonly number = number;
  readonly filterLabel = (filters: Parameters<typeof filterLabel>[0]) => filterLabel(filters, this.store.uf());
  readonly suggestions = computed(() => ['Quais municípios têm mais empresas na base?', 'Compare os municípios com mais empresas.', this.store.restricted() ? 'Encontre padarias no Ceará.' : 'Encontre padarias com celular nesta UF.', 'Me ajude a planejar uma pesquisa de mercado.']);
  readonly labels: Record<string, string> = { cidade: 'Município', bairro: 'Bairro', segmento: 'Ramo', porte: 'Porte', abertura: 'Ano', contatos: 'Empresas', com_email: 'Com e-mail', com_celular: 'Com celular', sem_dominio: 'E-mail sem domínio', score_medio: 'Score médio' };
  constructor() {
    this.question.valueChanges.pipe(takeUntilDestroyed()).subscribe(v => this.hasQuestion.set(!!v.trim()));
    effect(() => { this.store.turns(); this.loading(); this.bottom()?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); });
  }
  private readonly untilDestroyed = takeUntilDestroyed<ChatResponse>();
  submit(value = this.question.value) {
    const text = value.trim();
    if (!text || this.loading() || !this.base().ia_configurada) return;
    const history = this.store.turns().slice(-12).map(t => ({ role: t.role, text: t.text.slice(0, 6000) }));
    const pendingTurn = { role: 'user' as const, text };
    this.store.turns.update(turns => [...turns, pendingTurn]);
    this.question.setValue(''); this.loading.set(true); this.error.set('');
    this.api.request<ChatResponse>('chat', { pergunta: text, historico: history }).pipe(this.untilDestroyed).subscribe({
      next: response => { this.store.turns.update(turns => [...turns, { role: 'model', text: response.texto, response }]); this.finish(); },
      error: error => { this.store.turns.update(turns => turns.filter(turn => turn !== pendingTurn)); this.error.set(error.message); this.question.setValue(text); this.finish(); }
    });
  }
  private finish() { this.loading.set(false); this.textarea()?.nativeElement.focus(); }
  keydown(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); this.submit(); }
  }
  columns(groups: string[]) { return [...groups, 'contatos', ...(this.store.restricted() ? [] : ['com_celular', 'com_email']), 'score_medio']; }
  cell(row: AnalysisRow, column: string) { const v = row[column]; return typeof v === 'number' ? number(v) : v || 'Não informado'; }
  newConversation() { this.store.turns.set([]); this.error.set(''); }
}
