import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../lib/api.service';
import { PrivacyNotice, date, repositoryUrl } from '../lib/api';

@Component({
  selector: 'ceara-privacy',
  template: `
    <section class="privacy-content" aria-label="Aviso de privacidade">
      @if (error()) { <p class="inline-error" role="alert">{{ error() }}</p> }
      @if (notice(); as n) {
        <p class="muted">Atualizado em {{ date(n.atualizado_em) }}</p>
        <h2>Responsável e atendimento</h2>
        <p>{{ n.responsavel || 'Identificação do responsável ainda não configurada.' }}</p>
        @if (n.email) { <p><a [href]="'mailto:' + n.email + '?subject=Pedido%20de%20privacidade%20-%20Mapa%20de%20Oportunidades'">{{ n.email }}</a></p> }
        <h2>Finalidade e dados publicados</h2><p>{{ n.finalidade }}</p><p>{{ n.dados_publicados }}</p>
        <p>Fonte: Dados Abertos CNPJ da Receita Federal, obtidos pelo espelho Casa dos Dados. Malhas e códigos municipais: IBGE. A finalidade cadastral da Receita não é uma autorização para qualquer uso comercial de dados pessoais.</p>
        <p><a [href]="repositoryUrl + '#executar-localmente'" target="_blank" rel="noopener noreferrer">Código e execução local no GitHub</a>. O clone contém o código; os dados públicos são baixados e preparados no computador do usuário.</p>
        <h2>Dados dos visitantes</h2><p>{{ n.visitantes }}</p>
        <h2>Hospedagem e serviços externos</h2><p>{{ n.compartilhamento }}</p><p>{{ n.transferencias }}</p>
        <p><a href="https://vercel.com/legal/privacy-notice" target="_blank" rel="noopener noreferrer">Privacidade da Vercel</a> · <a href="https://vercel.com/legal/dpa" target="_blank" rel="noopener noreferrer">Contrato de tratamento da Vercel</a></p>
        <h2>Retenção e atualizações</h2><p>{{ n.retencao }}</p>
        <h2>Como exercer seus direitos</h2><p>{{ n.direitos }}</p>
        <p>Quando necessário, será solicitada uma comprovação proporcional para evitar alterações indevidas. A confirmação ou acesso simplificado pode ser imediato; a declaração completa de acesso tem prazo legal de até 15 dias. Outros pedidos são avaliados conforme o direito aplicável, sem promessa de exclusão automática.</p>
        <p>Você também pode procurar a <a href="https://www.gov.br/anpd/pt-br/assuntos/titular-de-dados" target="_blank" rel="noopener noreferrer">ANPD</a> se considerar que seus direitos não foram atendidos.</p>
        <h2>Uso responsável</h2><p>{{ n.limites }}</p><p>O score é uma hipótese de priorização comercial. Não comprova interesse de compra ou capacidade econômica. Não use o site para assédio, discriminação ou mensagens não solicitadas em massa.</p>
      } @else if (!error()) { <p role="status">Carregando informações de privacidade…</p> }
    </section>`
})
export class PrivacyComponent {
  private readonly api = inject(ApiService);
  readonly notice = signal<PrivacyNotice | null>(null);
  readonly error = signal('');
  readonly date = date;
  readonly repositoryUrl = repositoryUrl;
  constructor() {
    this.api.request<PrivacyNotice>('privacidade').subscribe({ next: notice => this.notice.set(notice), error: error => this.error.set(error.message) });
  }
}
