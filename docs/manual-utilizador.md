# Manual do utilizador

Bezix Facturação: facturas, recibos e cobranças em meticais, de acordo com as regras fiscais moçambicanas. Este manual segue as tarefas do dia-a-dia. O que cada pessoa vê no menu depende do seu perfil (ver o fim do manual).

## 1. Entrar e proteger a conta

1. Abra o endereço do sistema e entre com o email e a password que o administrador lhe deu.
2. Ao fim de 5 tentativas erradas a conta fica bloqueada durante 15 minutos.
3. **Primeiro acesso:** se a password lhe foi dada pelo Administrador (conta nova ou password redefinida), o sistema pede-lhe logo para a mudar. Escreva a password recebida em "Password actual" e escolha uma sua; até lá, as outras páginas não abrem.
4. No menu do seu nome → **A minha conta** pode:
   - mudar a password;
   - activar, se quiser, a **verificação em dois passos**: leia o código QR com uma aplicação como Google Authenticator ou Microsoft Authenticator, escreva o código de 6 algarismos e guarde os códigos de recuperação num sítio seguro;
   - escrever a **assinatura** que aparece no fim dos emails que envia aos clientes.

## 2. O painel

A primeira página mostra, conforme o perfil:

- quanto foi facturado e recebido no mês, o IVA, o que está por receber e vencido, com a comparação com o mês anterior;
- os gráficos de facturação (diário, mensal, anual) e da antiguidade das dívidas;
- **Para hoje**: facturas vencidas e a vencer (com o botão **Enviar lembrete**), rascunhos por emitir, aprovações pendentes, pagamentos comunicados por clientes, facturas recorrentes da semana, emails ou notificações que falharam, exportação AT do mês anterior por fazer, séries por fechar.

A pesquisa no topo (**Ctrl+K**) encontra clientes, documentos e artigos por nome, NUIT, número ou telefone.

## 3. Clientes e artigos

- **Clientes → Novo cliente**: nome, tipo (empresa ou particular), NUIT (obrigatório para empresas), contactos, email de facturação, prazo de pagamento e limite de crédito. Se o NUIT já existir noutro cliente, o sistema avisa.
- Na ficha do cliente: documentos, conta corrente, emails enviados e actividades (chamadas, reuniões, tarefas, notas).
- **Artigos**: código, descrição, unidade, preço sem IVA e taxa de IVA. Na factura, escrever o código ou parte da descrição preenche a linha.
- Clientes e artigos podem ser **importados de um ficheiro CSV** (Excel → Guardar como CSV), com pré-visualização antes de gravar.

## 4. Facturar

### Criar e emitir

1. **Novo → Nova factura** (ou factura-recibo, se o cliente paga logo).
2. Escolha o cliente; a data e o vencimento são propostos (pode mudar).
3. Adicione as linhas: artigo ou descrição livre, quantidade, preço, desconto e taxa de IVA. Os totais actualizam-se à medida que escreve. Pode haver também um desconto global.
4. **Guardar rascunho** guarda sem numerar; o rascunho pode ser alterado ou apagado.
5. **Emitir** atribui o número definitivo (ex.: `FT A2026/0042`), gera o PDF e **fecha o documento**: a partir daqui não se altera nem se apaga.

Se o desconto passar o limite do seu perfil, o documento fica **a aguardar aprovação**: um gestor aprova (e emite) ou rejeita com o motivo, em **Aprovações** ou no painel.

### Outros documentos

- **Cotação** e **guia de remessa**: não têm valor fiscal; podem ser convertidas em factura (detalhe → **Mais → Converter em factura**).
- **Factura global**: junta várias guias de remessa numa factura.
- **Nota de crédito** (devolução, desconto posterior, erro) e **nota de débito**: criam-se a partir da factura (detalhe → **Mais → Criar nota de crédito**). A NC não pode passar o valor ainda por creditar da factura. Para o perfil Facturador, a NC fica a aguardar aprovação.
- **Anular** (detalhe → **Mais → Anular**, só com permissão): exige um motivo e fica registado. Uma factura com pagamentos não se anula: anule primeiro o recibo ou emita uma NC.

### Imprimir e enviar

No detalhe do documento: **Imprimir** (abre o PDF), **Mais → Imprimir 2.ª via** e **Enviar por email**. O email usa o modelo do tipo de documento (pode ser alterado antes de enviar) e leva o PDF e um **link seguro**. Por esse link o cliente vê o documento sem login, descarrega o PDF, vê as contas bancárias e pode **comunicar um pagamento** com comprovativo. O detalhe mostra quando o cliente abriu o link.

## 5. Receber

- **Recibos → Registar recibo**: escolha o cliente, o valor, a data e o método (transferência, M-Pesa, e-Mola, numerário, cheque, POS) e reparta o valor pelas facturas em aberto. O que sobrar fica como **adiantamento**, que pode ser usado mais tarde noutras facturas.
- **Pagamentos comunicados**: o que os clientes indicaram pelo link. Confira o comprovativo com o extracto bancário e carregue em **Validar e emitir recibo** (o recibo vem preenchido) ou **Rejeitar** com o motivo. Nada entra na conta corrente antes de validado.
- **Conta corrente**: saldo, vencido e adiantamentos de cada cliente; extracto por período (PDF ou email).
- **Antiguidade de saldos**: o que cada cliente deve por escalão de atraso (0–30, 31–60, 61–90, mais de 90 dias), em Excel.
- **Cobranças**: clientes com facturas vencidas; escolha vários e envie-lhes o extracto de uma vez.

## 6. Comunicação e automação

- **Modelos de email**: o texto de cada tipo de email (envio de factura, recibo, extracto, lembretes…). Use variáveis como `{cliente.nome}`, `{documento.numero}` ou `{documento.emaberto}`; **Pré-visualizar** mostra o resultado com dados reais e **Enviar teste para mim** envia-o para o seu email. **Repor texto original** desfaz as alterações.
- **Emails enviados**: tudo o que saiu, com o estado. Os que falham são repetidos automaticamente; pode **Reenviar**.
- **Automação → Lembretes de pagamento**: aviso X dias antes do vencimento e até 3 avisos depois (ex.: 1, 15 e 30 dias), a partir de uma hora definida e só em dias úteis, se quiser. Param quando a factura fica paga.
- **Facturas recorrentes**: numa factura emitida, **Mais → Tornar recorrente**. Escolha a periodicidade (semanal, mensal, trimestral, anual), a próxima data e se o sistema cria só um rascunho para rever ou emite e envia automaticamente.

## 7. Relatórios e obrigações fiscais

- **Vendas**: por cliente, artigo, mês ou tipo de documento, num período à escolha. As notas de crédito subtraem e os anulados não contam. Quem não tem permissão para ver tudo vê só os seus documentos.
- **Mapa de IVA**: IVA liquidado por taxa (facturado menos creditado) e lista de documentos anulados, para a declaração periódica.
- **Exportação AT**: no início de cada mês, escolha o mês anterior e carregue em **Gerar ficheiro do mês**. Antes de gerar, o sistema confirma que nenhum documento foi alterado e que não há saltos de numeração. Descarregue o ZIP, submeta-o no e-Declaração e carregue em **Marcar submetido** com a referência do comprovativo. *Enquanto a AT não enviar o formato oficial, o ficheiro é provisório e serve para conferência (aviso na página).*
- Todos os relatórios exportam para **Excel (CSV)**.

## 8. Administração (Administrador)

- **Configurações → Empresa**: dados fiscais, logótipo, cor, rodapé, prazo de pagamento, assinatura dos emails, endereço público dos links e a validade dos links.
- **Contas bancárias**, **Taxas de IVA** (com data de início) e **Séries** (fechar no fim do ano e abrir as do ano seguinte).
- **Utilizadores** e **Perfis e permissões**.
- **Integrações**: chaves da API e webhooks para outros sistemas ([api.md](api.md)).
- **Backups**: estado dos backups diários, verificar um backup, fazer um agora ([backups-restauro.md](backups-restauro.md)).
- **Auditoria**: quem fez o quê e quando (emissões, anulações, alterações de configuração, entradas, exportações).

## Perfis

| Perfil | Faz |
|---|---|
| Administrador | Tudo, incluindo configurações, utilizadores, integrações e backups |
| Gestor | Vendas, cobranças, aprovações, relatórios e exportação AT; não altera configurações nem utilizadores |
| Facturador | Clientes, artigos, emissão de facturas; NC e descontos altos pedem aprovação; relatórios só dos seus documentos |
| Tesouraria | Recibos, validação de pagamentos comunicados, cobranças, facturas-recibo |
| Contabilista | Consulta, relatórios fiscais, mapa de IVA e exportação AT |
| Consulta | Só consulta |

## Problemas frequentes

| Situação | O que fazer |
|---|---|
| "A data não pode ser anterior à do último documento da série" | Já foi emitido um documento com data mais recente nesta série; use a data de hoje |
| "Preencha o NUIT da empresa" ao emitir | Administrador: Configurações → Empresa |
| O cliente diz que o link não abre | Os links expiram (90 dias por omissão); reenvie o documento por email para gerar um novo |
| Email não chegou | Emails enviados → ver o erro; confirmar o email do cliente; Reenviar |
| Enganou-se numa factura emitida | Nota de crédito (ou anulação com motivo, se ainda não foi entregue ao cliente) e nova factura |
