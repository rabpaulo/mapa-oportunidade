import { Component, input } from '@angular/core';
import { BaseInfo, date, number, repositoryUrl } from '../lib/api';
import { IconComponent } from './icon.component';

@Component({
  selector: 'ceara-base', imports: [IconComponent],
  template: `
    <section class="base-layout"><div class="base-info"><div class="base-title"><ceara-icon name="database" [size]="24" /><h2>Base do Ceará</h2><span class="tag">{{ base().disponivel ? 'Disponível' : 'Indisponível' }}</span></div>
      <dl class="base-facts"><div><dt>{{ base().publicacao_restrita ? 'Empresas publicadas' : 'Empresas com contato' }}</dt><dd>{{ number(base().contatos) }}</dd></div><div><dt>Municípios</dt><dd>{{ number(base().municipios) }}</dd></div><div><dt>Ramos de atividade</dt><dd>{{ number(base().ramos) }}</dd></div><div><dt>Versão do cadastro</dt><dd>{{ date(base().versao_receita) }}</dd></div><div><dt>Base gerada em</dt><dd>{{ date(base().gerado_em) }}</dd></div><div><dt>Armazenamento do banco</dt><dd>{{ base().tamanho_mb ?? 0 }} MB</dd></div></dl>
      @if (base().erro) { <p class="inline-error" role="alert">{{ base().erro }}</p> }
      <div class="base-provenance"><h3>Como ler estes dados</h3><p>Dados Abertos CNPJ da Receita Federal, processados para o Ceará.</p><p>{{ base().publicacao_restrita ? base().recorte : 'A base reúne estabelecimentos ativos na data do cadastro com e-mail ou celular aproveitável.' }}</p><p>{{ base().publicacao_restrita ? 'O score público usa atividade, porte e ano de abertura, sem indicadores de contato. Os nomes são revisados para retirar sequências de CPF.' : 'O score é uma priorização para serviços digitais. “Sem domínio próprio” deriva do e-mail.' }} O ano de abertura não contém mês ou dia.</p><p>Malhas e códigos de municípios: IBGE.</p></div></div>
      <div class="base-info"><h2>Versão publicada</h2><p>Esta aplicação consulta uma versão validada do cadastro. A data de geração e a versão da Receita Federal identificam os dados disponíveis.</p>@if (!base().publicacao_restrita) { <p>A cópia local integral é incorporada ao deploy e consultada pelo servidor. Nenhuma coluna ou registro foi omitido na preparação deste snapshot.</p> }<p>Novas versões são preparadas e verificadas antes da publicação.</p><div class="base-provenance"><h3>Executar no seu computador</h3><p>O código está disponível no GitHub. Clone o repositório, instale as dependências, baixe e prepare a base pública pelo coletor local e inicie a aplicação. O banco e as chaves não acompanham o clone.</p><a class="text-button" [href]="repositoryUrl + '#executar-localmente'" target="_blank" rel="noopener noreferrer">Código e instruções locais <ceara-icon name="arrow-up-right" [size]="15" /></a></div><div class="base-provenance"><h3>Assistente</h3>@if (base().hospedagem_publica) { <p>O chat público está desativado. Nenhuma pergunta é enviada ao Gemini nesta versão.</p> } @else { <p>{{ base().ia_configurada ? 'Gemini disponível nesta aplicação.' : 'O assistente ainda não está disponível nesta aplicação.' }}</p><p class="help-text">O Gemini recebe perguntas, histórico da conversa, estatísticas e categorias. A identificação das empresas aparece diretamente nesta aplicação. Não inclua dados pessoais ou confidenciais; o filtro de identificadores não garante anonimização do texto.</p> }</div></div>
    </section>`
})
export class BaseComponent {
  readonly base = input.required<BaseInfo>();
  readonly number = number;
  readonly date = date;
  readonly repositoryUrl = repositoryUrl;
}
