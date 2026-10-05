import { Component, input } from '@angular/core';
import { BaseInfo, date, number } from '../lib/api';
import { IconComponent } from './icon.component';

@Component({
  selector: 'ceara-base', imports: [IconComponent],
  template: `
    <section class="base-layout"><div class="base-info"><div class="base-title"><ceara-icon name="database" [size]="24" /><h2>Base do Ceará</h2><span class="tag">{{ base().disponivel ? 'Disponível' : 'Indisponível' }}</span></div>
      <dl class="base-facts"><div><dt>Empresas com contato</dt><dd>{{ number(base().contatos) }}</dd></div><div><dt>Municípios</dt><dd>{{ number(base().municipios) }}</dd></div><div><dt>Ramos de atividade</dt><dd>{{ number(base().ramos) }}</dd></div><div><dt>Versão do cadastro</dt><dd>{{ date(base().versao_receita) }}</dd></div><div><dt>Base gerada em</dt><dd>{{ date(base().gerado_em) }}</dd></div><div><dt>Armazenamento do banco</dt><dd>{{ base().tamanho_mb ?? 0 }} MB</dd></div></dl>
      @if (base().erro) { <p class="inline-error" role="alert">{{ base().erro }}</p> }
      <div class="base-provenance"><h3>Como ler estes dados</h3><p>Dados Abertos CNPJ da Receita Federal, processados para o Ceará. A base reúne estabelecimentos ativos na data do cadastro com e-mail ou celular aproveitável.</p><p>O score é uma priorização para serviços digitais. “Sem domínio próprio” deriva do e-mail. O ano de abertura não contém mês ou dia.</p><p>Malhas e códigos de municípios: IBGE.</p></div></div>
      <div class="base-info"><h2>Versão publicada</h2><p>Esta aplicação consulta uma versão validada do cadastro. A data de geração e a versão da Receita Federal identificam os dados disponíveis.</p><p>Novas versões são preparadas e verificadas antes da publicação.</p><div class="base-provenance"><h3>Assistente</h3><p>{{ base().ia_configurada ? 'Gemini disponível nesta aplicação.' : 'O assistente ainda não está disponível nesta aplicação.' }}</p><p class="help-text">O Gemini recebe perguntas, estatísticas e categorias. A identificação das empresas aparece diretamente nesta aplicação.</p></div></div>
    </section>`
})
export class BaseComponent {
  readonly base = input.required<BaseInfo>();
  readonly number = number;
  readonly date = date;
}
