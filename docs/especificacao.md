# Especificação — Sistema de Facturação Web (Winhost)

> Texto original da especificação aprovada (09/10/2026). Alterações decididas depois estão em [decisoes.md](decisoes.md) e prevalecem sobre este documento.

## 1. Visão geral e âmbito

Software interno, para uma única empresa, de facturação automatizada e interacção com clientes, em ASP.NET Core + SQL Server, alojado no Winhost, que emite documentos fiscais conformes com o Código do IVA de Moçambique e gere clientes, recibos e conta corrente.

**Objectivos**

- Emitir facturas, facturas-recibo, notas de crédito e débito, factura global, cotações e guias de remessa, com numeração por série.
- Emitir recibos com pagamento alocado a várias facturas; extractos e antiguidade de saldos.
- Imprimir, descarregar em PDF e enviar por email qualquer documento.
- Automatizar lembretes, facturas recorrentes e cobranças.
- Expor API REST e webhooks para sistemas externos.
- Gerar o ficheiro mensal de facturas para submissão à Autoridade Tributária (AT).

**Fora de âmbito (v1):** contabilidade geral, salários, POS de loja, inventário avançado.

**Utilizadores:** colaboradores internos da empresa, com perfis definidos na secção Perfis e permissões. Clientes não fazem login; recebem documentos por email com link seguro.

**Referência visual:** factura da Bezix nº 08/26 (logótipo topo esquerdo, dados do emitente, bloco do cliente, linhas, subtotal, desconto, base tributável, IVA 16%, total, contas bancárias, rodapé "Processado por computador").

## 2. Stack técnica e restrições Winhost

Usar ASP.NET Core 8 (LTS) + SQL Server, publicado no plano Winhost Ultimate ou Power, porque só esses têm Scheduled Task Tool e mais memória por site.

| Camada | Escolha | Nota |
|---|---|---|
| Backend | ASP.NET Core 8, C#, Razor Pages ou MVC + Web API | Winhost suporta ASP.NET Core até 9.x (planos) |
| Base de dados | SQL Server 2022 (partilhado) via EF Core 8 | 5 GB no Ultimate, 10 GB no Power; DBO, backup/restore no painel |
| Frontend | Razor + HTMX ou Blazor Server; Bootstrap 5 | Sem build Node no servidor |
| PDF | QuestPDF (licença Community até limite de receita) | Puro .NET, sem dependência nativa; evitar wkhtmltopdf/Chromium em alojamento partilhado |
| Email | MailKit via SMTP (Winhost ou SendGrid/Brevo) | Fila na BD, envio por job |
| Jobs | Hangfire com storage SQL Server | Acordado por pedido HTTP do Scheduled Task Tool |
| Ficheiros | Pasta App_Data/uploads fora de wwwroot | Logótipos, anexos; incluir no backup |
| Logs | Serilog → ficheiro + tabela | Rotação diária |

**Restrições a respeitar**

- Alojamento partilhado: sem SQL Server Agent, sem Windows Services, app pool recicla. Jobs não podem depender de processo sempre vivo.
- Solução: endpoint protegido `POST /internal/jobs/tick` chamado pelo Scheduled Task Tool a cada 15–60 min; executa lembretes, recorrentes, webhooks pendentes, backup lógico. Hangfire serve como fila e histórico.
- Memória de site limitada (300 MB Max / 500 MB Ultimate / 1 GB Power): gerar PDFs em streaming, paginar listagens, sem cache grande em memória.
- Publicar com `dotnet publish -c Release` em modo framework-dependent ou self-contained win-x64; deploy por Web Deploy ou FTP; web.config com `hostingModel="inprocess"`.
- Activar SSL no painel (SSL Manager) e forçar HTTPS + HSTS.
- Para crescimento: migrar para Private SQL Server Winhost ou VPS sem mudar código.

**Arquitectura:** monólito modular, um projecto por camada — Web (UI + API), Application (casos de uso), Domain (entidades, regras fiscais), Infrastructure (EF Core, PDF, email, jobs). Empresa única: dados da empresa num registo de configuração, sem TenantId.

## 3. Regras fiscais Moçambique

O software tem de ser certificado pela AT e exportar mensalmente os dados das facturas para o portal e-Declaração; contactar a AT antes de fechar o modelo de dados.

**Base legal e avisos**

- Código do IVA, art. 27, n.º 10: sujeitos passivos submetem electronicamente os dados das facturas emitidas (Vendus).
- Aviso da AT de 27/03/2025 (Aviso n.º 40/AT/DGI/2025): ficheiro mensal extraído de programa certificado, submetido comprimido no e-Declaração; produtores de software devem pedir à AT a estrutura de dados e o procedimento de certificação (DLA Piper). Novo procedimento em vigor desde Maio de 2025.
- Taxa normal de IVA: 16% (Lei n.º 22/2022), mantida no Pacote Fiscal 2026, que também alterou regras de facturação e serviços digitais (RSM).
- Mudança de taxa ou de série exige nova série e registo da última série usada (PwC, Circular 01/AT/DGI-GDG/001.1/2023).

**Regras a implementar no domínio**

1. Numeração sequencial, sem saltos, por tipo de documento + série + ano. Formato: `FT A2026/0001`.
2. Documento emitido é imutável: sem editar nem apagar. Correcção só por nota de crédito/débito ou anulação com motivo, utilizador e data registados.
3. Data de emissão nunca anterior à do último documento da mesma série.
4. Campos obrigatórios na factura: nome, endereço e NUIT do emitente; nome, endereço e NUIT do adquirente (NUIT obrigatório acima de limite configurável e sempre para empresas); número e data; descrição, quantidade, preço unitário; base tributável por taxa; taxa e valor do IVA; motivo de isenção quando IVA 0; total; moeda (MZN); menção "Processado por computador" + identificação do software certificado.
5. Desconto aplicado antes do IVA (como na factura modelo: 30.804,10 − 20% = 24.643,28; IVA 16% = 3.942,92). Atenção: a factura modelo mostra IVA 4.928,66, que é 20% e não 16% — o sistema deve calcular sempre a partir da taxa configurada e testar este caso.
6. Arredondamento: 2 casas por linha, IVA por taxa sobre a soma das bases (configurável), regra documentada.
7. Taxas de IVA, isenções e motivos em tabela parametrizável com data de vigência.
8. Encadeamento de integridade: cada documento guarda hash (SHA-256) do anterior da série + assinatura, conforme requisitos que a AT fornecer na certificação.
9. Exportação mensal: gerar ficheiro (CSV/XML conforme estrutura AT) por período, comprimir (.zip), guardar histórico de exportações.
10. Retenção: guardar documentos e PDFs no mínimo pelo prazo legal (confirmar com contabilista; assumir 10 anos).

**Exportação AT enquanto se aguarda resposta ao ofício**

- Desenvolvimento não pára: o módulo de exportação é um componente isolado, interface `IExportadorAT` com método `Gerar(periodo) → ficheiro`.
- Fase 3–5: implementação provisória (CSV com todos os campos de documento, linhas, IVA por taxa, NUIT, hash) para testes internos.
- Quando a AT responder: nova implementação conforme ofício, sem mexer em documentos, séries ou cálculo.
- Modelo de dados já guarda tudo o que um ficheiro fiscal costuma pedir (snapshot do cliente, IVA por taxa, motivo de isenção, hash, utilizador, data/hora de emissão), para não exigir migração.
- Requisitos de certificação recebidos (assinatura, código no rodapé) entram como alteração controlada na fase 6.

**Questões em aberto (confirmar com AT/contabilista)**

- Enviar ofício à AT (fase 1) a pedir estrutura dos ficheiros de facturação, aquisição e auto-facturação e o procedimento de certificação; arquivar ofício e resposta no projecto.
- Requisitos de certificação: assinatura, chave, código no rodapé.
- Limite a partir do qual NUIT do cliente é obrigatório.
- Regras de factura global (periodicidade, conteúdo).

## 4. Tipos de documentos e numeração por série

Cada tipo tem código, séries próprias e contador independente por ano; só os documentos fiscais entram na exportação AT.

| Código | Documento | Valor fiscal | Movimenta conta corrente | Origem / ligação |
|---|---|---|---|---|
| FT | Factura | Sim | Débito ao cliente | Pode nascer de cotação ou guia |
| FR | Factura-Recibo | Sim | Débito + crédito (liquidada) | Pagamento no acto |
| FG | Factura global | Sim | Débito | Agrega guias/vendas de um período |
| NC | Nota de crédito | Sim | Crédito | Referencia obrigatoriamente FT/FR/FG + motivo |
| ND | Nota de débito | Sim | Débito | Referencia documento de origem + motivo |
| RC | Recibo | Sim (quitação) | Crédito | Aloca pagamento a uma ou várias FT/ND |
| CT | Cotação / Proforma | Não ("Este documento não serve de factura") | Não | Converte em FT/FR |
| GR | Guia de remessa | Transporte | Não | Converte em FT ou FG |

**Séries**

- Tabela Serie: tipo, código (ex.: A, B, ONLINE), ano, próximo número, estado (activa/fechada), data de abertura e fecho, última data usada.
- Atribuição de número na transacção de emissão com `UPDLOCK, HOLDLOCK` ou sequence por série, para não haver duplicados nem saltos com utilizadores simultâneos.
- Rascunho não tem número; número só ao emitir (Status: Rascunho → Emitido → Anulado).
- Fecho de série no fim do ano ou mudança de taxa; nova série abre com contador 1.
- Anulação: só documentos sem pagamentos ou NC associadas; regista motivo; mantém número.

**Regras por tipo**

- NC não pode exceder o valor ainda não creditado do documento de origem.
- RC: soma das alocações = valor recebido; excesso fica como adiantamento (crédito do cliente).
- FR: método de pagamento obrigatório; não gera saldo pendente.
- Conversão CT → FT copia linhas e mantém ligação para rastreio.

## 5. Módulos funcionais

Sete módulos; Configurações e Vendas são o núcleo da v1.

**1. Configurações da empresa**

- Dados fiscais: nome, NUIT, endereço, telefones, email, site, slogan.
- Personalização de Documentos → campo Logótipo: upload PNG ou JPG, máx. 2 MB, redimensionar para 600 px de largura, validar tipo real (assinatura do ficheiro, não só extensão).
- Informação bancária: várias contas com banco, nº conta, NIB, IBAN, SWIFT; marcar quais aparecem no rodapé do documento.
- Séries, taxas de IVA e isenções, moeda, termos de pagamento (ex.: 30 dias), texto de rodapé, modelo de email.
- Utilizadores e perfis; autenticação 2FA opcional.

**2. Clientes (CRM)**

- Ficha: nome, NUIT, endereço, contactos, pessoa de contacto (Att.), condições de pagamento, limite de crédito, desconto padrão, notas, etiquetas.
- Histórico: documentos, pagamentos, emails enviados, actividades (chamada, reunião, tarefa com data).
- Pesquisa por nome/NUIT/telefone; importação CSV; detecção de NUIT duplicado.

**3. Produtos e serviços**

- Código, descrição, unidade, preço, taxa de IVA ou isenção, activo/inactivo.

**4. Vendas e documentos**

- Editor de documento: cliente, série, data, vencimento, linhas (qtd, ref, descrição, preço, desconto %, IVA), desconto global, observações.
- Cálculo em tempo real no ecrã, recálculo definitivo no servidor.
- Acções: emitir, duplicar, converter, anular, criar NC/ND, imprimir, PDF, enviar email.

**5. Recibos e conta corrente**

- Recibo com valor, data, método (numerário, transferência, M-Pesa, e-Mola, cheque, POS), referência e alocação a várias facturas.
- Extracto de cliente por período (saldo inicial, movimentos, saldo final) em PDF e email.
- Antiguidade de saldos: 0–30, 31–60, 61–90, >90 dias, por cliente e total.

**6. Automação**

- Facturas recorrentes: modelo + periodicidade (semanal, mensal, anual) + data fim; emite como rascunho ou automático e envia por email.
- Lembretes de cobrança: X dias antes e Y dias depois do vencimento, modelos de email editáveis, parar ao pagar.
- Cobranças: lista de vencidos, envio em massa de extractos.

**7. Relatórios**

- Vendas por período, cliente, produto; mapa de IVA liquidado por taxa; documentos anulados; exportação AT mensal; Excel/CSV.

## 6. Dashboard e experiência de utilização

Dashboard é a página inicial: em 5 segundos o utilizador vê quanto facturou, quanto recebeu, quem deve e o que fazer hoje. Conteúdo adapta-se ao perfil.

**Referência visual (estilo aprovado pelo cliente)**

Estilo de painel administrativo moderno: menu lateral fixo à esquerda, fundo claro, cartões brancos com cantos arredondados, cartões de indicadores com gradiente colorido, gráfico de área suave, donut, linha do tempo de actividades e tabela com etiquetas de estado coloridas. Usar como referência de estilo; não copiar template comercial sem licença (comprar licença se o programador usar um).

| Zona da referência | Conteúdo no nosso sistema |
|---|---|
| Menu lateral | Dashboard, Documentos, Clientes, Artigos, Recibos, Conta corrente, Relatórios, Automação, Configurações (visível conforme perfil) |
| Barra superior | Logótipo da empresa, pesquisa global, notificações (aprovações, pagamentos por validar, falhas), utilizador |
| Bloco principal com gráfico de área | Facturado e recebido do mês + gráfico 12 meses; separadores Diário / Mensal / Anual |
| Mini-indicadores sob o gráfico | Por receber, Vencido, IVA do mês, Prazo médio de recebimento |
| Donut | Antiguidade de saldos (0–30, 31–60, 61–90, >90 dias) ou facturação por tipo de documento |
| 4 cartões com gradiente | Facturas emitidas, Recibos, Clientes activos, Valor vencido — cada um com mini-gráfico |
| Actividades recentes (linha do tempo) | Documento emitido, pagamento recebido, email visto pelo cliente, aprovação pedida |
| Tabela de pedidos | Últimos documentos: Nº, Cliente, Data, Total, Estado (Rascunho, Emitido, Pago, Parcial, Vencido, Anulado); botões Nova factura, filtros, imprimir |

Cores: gradientes só nos cartões de indicadores; cor primária = cor da marca da empresa (configurável); vermelho reservado a vencido/erro.

**Indicadores (cartões no topo, com variação vs período anterior)**

| Indicador | Cálculo | Perfis |
|---|---|---|
| Facturado no mês | Σ FT + FR + FG + ND − NC emitidas | Admin, Gestor, Facturador, Contabilista |
| Recebido no mês | Σ recibos + FR | Admin, Gestor, Tesouraria, Contabilista |
| Por receber | Saldo aberto de todos os clientes | Admin, Gestor, Tesouraria |
| Vencido | Saldo com vencimento ultrapassado | Admin, Gestor, Tesouraria |
| IVA liquidado no mês | Σ IVA dos documentos fiscais | Admin, Gestor, Contabilista |
| Prazo médio de recebimento | Dias médios entre emissão e pagamento | Admin, Gestor |

**Gráficos**

- Facturação vs recebimentos, últimos 12 meses (barras + linha).
- Antiguidade de saldos: 0–30, 31–60, 61–90, >90 dias.
- Top 10 clientes por facturação e top 10 devedores.

**Listas de acção ("o que fazer hoje")**

- Facturas a vencer nos próximos 7 dias e vencidas, com botão "Enviar lembrete" num clique.
- Recorrentes a emitir esta semana; rascunhos por emitir.
- Pagamentos comunicados por clientes à espera de validação.
- Alertas: emails falhados, webhooks falhados, exportação AT do mês por fazer, série a fechar.
- Últimos documentos e últimos recebimentos.

Acções rápidas: Nova factura, Nova factura-recibo, Novo recibo, Novo cliente — sempre visíveis.

**Princípios de design**

- Visual limpo: Bootstrap 5 ou Tabler, gráficos ApexCharts ou Chart.js, ícones Lucide/Bootstrap Icons, cor da marca configurável (logótipo + cor primária).
- Responsivo (telemóvel e tablet), modo claro/escuro.
- Pesquisa global (Ctrl+K): cliente, NUIT, nº documento, valor.
- Editor de factura numa só página: autocompletar cliente e artigo, totais ao vivo, pré-visualização do PDF antes de emitir, confirmação clara "Emitir é definitivo".
- Estados com cor e etiqueta: Rascunho (cinza), Emitido (azul), Pago (verde), Parcial (amarelo), Vencido (vermelho), Anulado (riscado).
- Formato Moçambique: 30.804,10 MT, datas dd/mm/aaaa, interface em português.
- Mensagens de sucesso/erro discretas (toasts); filtros guardados por utilizador.
- Performance: indicadores em consultas agregadas com cache de 5 min; carregar gráficos depois da página.

## 7. Perfis e permissões

Seis perfis recomendados, construídos sobre permissões granulares: o Administrador pode criar perfis novos ou ajustar estes sem alterar código.

| Acção | Administrador | Gestor | Facturador | Tesouraria | Contabilista | Consulta |
|---|---|---|---|---|---|---|
| Ver dashboard | Completo | Completo | Vendas | Cobranças | Fiscal | Resumo |
| Criar/editar clientes e artigos | Sim | Sim | Sim | Clientes | Não | Não |
| Criar cotação, guia, rascunho | Sim | Sim | Sim | Não | Não | Não |
| Emitir FT, FR, FG | Sim | Sim | Sim | FR | Não | Não |
| Emitir ND | Sim | Sim | Sim | Não | Não | Não |
| Emitir NC | Sim | Sim | Pede aprovação | Não | Não | Não |
| Anular documento | Sim | Sim | Não | Não | Não | Não |
| Desconto acima do limite | Sim | Sim | Pede aprovação | Não | Não | Não |
| Registar recibos e alocar | Sim | Sim | Não | Sim | Não | Não |
| Validar pagamento comunicado pelo cliente | Sim | Sim | Não | Sim | Não | Não |
| Enviar documentos e lembretes | Sim | Sim | Sim | Sim | Não | Não |
| Relatórios de vendas | Sim | Sim | Próprios | Não | Sim | Sim |
| Relatórios fiscais e exportação AT | Sim | Sim | Não | Não | Sim | Não |
| Recorrentes e automações | Sim | Sim | Ver | Ver | Não | Não |
| Configurações, séries, taxas IVA | Sim | Não | Não | Não | Taxas (ver) | Não |
| Modelos de texto e email | Sim | Sim | Não | Não | Não | Não |
| Utilizadores, perfis, chaves API, webhooks | Sim | Não | Não | Não | Não | Não |
| Ver auditoria | Sim | Sim | Não | Não | Sim | Não |

**Lógica recomendada**

- Separação de funções: quem emite não anula nem cria NC sozinho; quem recebe dinheiro não altera facturas.
- Limite de desconto por perfil (configurável, ex.: Facturador até 10%); acima disso o documento fica "Aguarda aprovação" e o Gestor aprova no dashboard.
- "Próprios": Facturador vê só documentos e clientes que criou ou que lhe estão atribuídos (opção configurável).
- Pelo menos 2 Administradores activos; o último Administrador não pode ser desactivado.
- Toda a acção sensível (emitir, anular, NC, alterar permissões, alterar configurações) fica na auditoria com utilizador, data e IP.
- Implementação: permissões como claims (`Documentos.Emitir`, `Documentos.Anular`, `Recibos.Registar`, ...), perfis = conjuntos de permissões guardados na BD, verificação por policy em UI e API.

## 8. Interacção com clientes e textos configuráveis

Nenhum texto dirigido ao cliente fica fixo no código: todos são modelos editáveis em Configurações → Modelos de comunicação, com variáveis e pré-visualização.

**Modelos a prever**

- Envio de factura, factura-recibo, nota de crédito/débito, cotação, recibo, extracto.
- Lembrete antes do vencimento (ex.: 3 dias antes).
- Lembretes após vencimento: 1.º, 2.º e 3.º aviso, com tom crescente.
- Confirmação de pagamento recebido / agradecimento.
- Factura recorrente emitida.
- Textos do PDF: rodapé, condições de pagamento, observações padrão por tipo de documento.

**Editor de modelos**

- Campos: assunto, corpo (editor rich text), CC/BCC padrão, anexos automáticos (PDF do documento, extracto).
- Variáveis: `{cliente.nome}`, `{cliente.contacto}`, `{documento.tipo}`, `{documento.numero}`, `{documento.data}`, `{documento.vencimento}`, `{documento.total}`, `{saldo.vencido}`, `{dias.atraso}`, `{link.documento}`, `{empresa.nome}`, `{empresa.contas}`, `{utilizador.nome}`.
- Botões: pré-visualizar com dados reais, enviar teste para mim, repor texto original.
- Assinatura de email da empresa e por utilizador.

**Link seguro para o cliente (sem login)**

- Cada email leva link com token assinado e expirável (ex.: 90 dias) para página pública com o documento, botão descarregar PDF, dados bancários e saldo em aberto.
- Sistema regista quando o cliente abre ("Visto em 09/10/2026 10:15") no histórico do cliente e no documento.
- Cliente pode comunicar pagamento: indica valor e anexa comprovativo (PDF/JPG/PNG); fica "Pagamento por validar" para a Tesouraria, que confirma e gera o recibo.

**Regras de envio automático**

- Calendário de lembretes configurável (dias antes/depois, máximo de avisos, horário de envio, só dias úteis).
- Excepções por cliente: desactivar lembretes, email alternativo para facturação, contacto em CC.
- Pára automaticamente quando a factura fica paga ou em disputa.
- Todo o envio fica no histórico do cliente (data, modelo, destinatário, estado entregue/falhou).
- Canal v1: email. Canal v2 (opcional): SMS/WhatsApp via fornecedor com API, usando os mesmos modelos em versão curta.

## 9. Modelo de dados

Todas as tabelas levam CreatedAt, CreatedBy, UpdatedAt; valores em decimal(18,2), quantidades decimal(18,4), datas em UTC com exibição em Africa/Maputo.

| Tabela | Campos principais |
|---|---|
| Empresa (registo único) | Nome, NUIT, Endereco, Telefones, Email, Site, LogoPath, RodapeTexto, Moeda |
| ContaBancaria | Banco, NumeroConta, NIB, IBAN, SWIFT, MostrarNoDocumento |
| Utilizador / Perfil | Email, Hash password, Perfil, 2FA, Activo |
| Cliente | Nome, NUIT, Endereco, Cidade, Telefone, Email, Contacto, PrazoPagamento, LimiteCredito, DescontoPadrao |
| Actividade | ClienteId, Tipo, Data, Nota, Responsavel |
| Artigo | Codigo, Descricao, Unidade, Preco, TaxaIvaId |
| TaxaIva | Codigo, Percentagem, MotivoIsencao, VigenteDesde, VigenteAte |
| Serie | TipoDoc, Codigo, Ano, ProximoNumero, Estado, UltimaData |
| Documento | TipoDoc, SerieId, Numero, NumeroFormatado, Data, Vencimento, ClienteId + snapshot (nome, NUIT, endereço), Subtotal, DescontoGlobal, BaseTributavel, TotalIva, Total, Estado, MotivoAnulacao, DocOrigemId, Hash, HashAnterior, PdfPath |
| DocumentoLinha | DocumentoId, ArtigoId, Ref, Descricao, Qtd, PrecoUnit, DescontoPct, TaxaIva, Base, Iva, TotalLinha |
| DocumentoIvaResumo | DocumentoId, Taxa, Base, Iva |
| Pagamento | ClienteId, Data, Valor, Metodo, Referencia, ReciboId |
| PagamentoAlocacao | PagamentoId, DocumentoId, Valor |
| Recorrencia | ModeloDocumentoId, Periodicidade, ProximaData, DataFim, EnviarEmail |
| EmailFila | Para, Assunto, Corpo, Anexos, Estado, Tentativas, ErroUltimo |
| ApiKey | Nome, HashChave, Escopos, UltimoUso |
| WebhookSubscricao / WebhookEntrega | Url, Segredo, Eventos / Evento, Payload, Estado, Tentativas, CodigoResposta |
| ExportacaoAT | Periodo, FicheiroPath, GeradoEm, GeradoPor, NumDocs |
| Auditoria | Entidade, EntidadeId, Accao, Antes, Depois, Utilizador, IP, Data |

**Notas**

- Documento guarda cópia dos dados do cliente e do emitente no momento da emissão; editar a ficha depois não altera documentos.
- Índice único (SerieId, Numero).
- Saldo do cliente = Σ débitos (FT, FG, ND) − Σ créditos (NC, RC); vista SQL ou tabela de movimentos ContaCorrente alimentada na emissão.
- Trigger ou interceptor EF que bloqueia UPDATE/DELETE em documentos emitidos (excepto campos de estado de anulação e PdfPath).

**Tabelas adicionais (dashboard, perfis, comunicação)**

| Tabela | Campos principais |
|---|---|
| Permissao / Perfil / PerfilPermissao | Codigo (ex.: Documentos.Anular), Descricao / Nome, Sistema (sim/não) / ligação N:N |
| LimiteDesconto | PerfilId, PercentagemMax |
| Aprovacao | DocumentoId, Tipo (desconto, NC), PedidoPor, DecididoPor, Estado, Data |
| ModeloComunicacao | Codigo, Canal, Assunto, Corpo, CC, AnexosAuto, Activo |
| PlanoLembretes | DiasAntes, DiasDepois (lista), MaxAvisos, Horario, SoDiasUteis |
| LinkPartilha | DocumentoId ou ClienteId, TokenHash, ExpiraEm, PrimeiraVisita, Visitas |
| PagamentoComunicado | ClienteId, Valor, Data, ComprovativoPath, Estado, ValidadoPor, ReciboId |
| Interacao | ClienteId, Tipo (email, visita link, chamada, nota), Referencia, Data, Utilizador |

## 10. PDF, impressão e email

Um único gerador (QuestPDF) produz o PDF; imprimir, descarregar e enviar por email usam sempre esse mesmo ficheiro.

**Layout (A4, baseado na factura modelo)**

1. Topo esquerdo: logótipo + slogan + nome legal do emitente.
2. Dados do emitente: província, endereço, NUIT, telefones, email, site.
3. Bloco do cliente à direita: "Exmo. Sr(a)", nome, cidade, NUIT, celular, telefone, Att.
4. Cabeçalho do documento: tipo + número (ex.: Factura nº FT A2026/0008), data, vencimento, cliente ID, moeda.
5. Tabela de linhas: Qt, Ref, Descrição, Preço unitário, Desc. %, IVA %, Total líquido; quebra de página com cabeçalho repetido.
6. Totais: Subtotal, Desconto, Base tributável, IVA por taxa, TOTAL; valor por extenso opcional.
7. Contas bancárias seleccionadas (banco, conta, NIB/IBAN/SWIFT).
8. Rodapé: "Processado por computador", identificação/certificação do software, hash curto (4 caracteres), utilizador, página X/Y.
9. Marcas: "ANULADO" em diagonal para anulados; "2ª VIA" em reimpressões; cotação com "Este documento não serve de factura".

**Regras**

- PDF gerado e guardado na emissão (imutável); reimpressão usa o guardado ou regenera idêntico com marca de via.
- Imprimir: abrir PDF inline no browser (`Content-Disposition: inline`).
- Download: attachment, nome `FT_A2026-0008_Cliente.pdf`.
- Email: modelo com variáveis (`{cliente}`, `{numero}`, `{total}`, `{vencimento}`), PDF anexo, CC/BCC, registo no histórico do cliente; enviado pela fila EmailFila com repetições.
- Extractos e recibos seguem o mesmo cabeçalho e rodapé.

## 11. API e webhooks

API REST versionada em `/api/v1`, autenticação por chave (`Authorization: Bearer <chave>`) com escopos, documentada em OpenAPI/Swagger.

| Método | Rota | Função |
|---|---|---|
| GET/POST | /api/v1/clientes | Listar (paginado, filtro), criar |
| GET/PUT | /api/v1/clientes/{id} | Ler, actualizar |
| GET/POST | /api/v1/artigos | Listar, criar |
| POST | /api/v1/documentos | Criar rascunho ou emitir (emitir: true) |
| GET | /api/v1/documentos/{id} | Ler documento |
| GET | /api/v1/documentos/{id}/pdf | Descarregar PDF |
| POST | /api/v1/documentos/{id}/email | Enviar por email |
| POST | /api/v1/documentos/{id}/anular | Anular com motivo |
| POST | /api/v1/recibos | Registar pagamento + alocações |
| GET | /api/v1/clientes/{id}/extracto | Extracto por período |

**Regras da API**

- Cabeçalho `Idempotency-Key` obrigatório em POST de documentos e recibos (evita duplicar facturas em repetições).
- Limite de pedidos por chave (ex.: 60/min) com 429.
- Erros em formato `application/problem+json`.
- A API nunca permite alterar documento emitido.

**Webhooks**

- Eventos: `documento.emitido`, `documento.anulado`, `documento.enviado`, `pagamento.recebido`, `documento.vencido`, `cliente.criado`.
- Payload JSON com id, evento, ocorridoEm, dados.
- Assinatura `X-Signature: sha256=<HMAC do corpo com segredo da subscrição>` + `X-Timestamp`.
- Entrega via fila; repetições com espera crescente (1 min, 5, 30, 2 h, 12 h); desactivar após 5 falhas seguidas; log de entregas visível ao utilizador e botão reenviar.
- Nota Winhost: entregas pendentes processadas também no jobs/tick, porque não há processo permanente.

## 12. Segurança, backups, disponibilidade e performance

Em alojamento partilhado, "alta disponibilidade" significa recuperação rápida e backups fora do Winhost, não redundância activa; para SLA real, planear VPS ou cloud mais tarde.

**Segurança**

- ASP.NET Core Identity, passwords com hash, bloqueio após 5 tentativas, 2FA (TOTP) para administradores.
- Autorização por permissão em cada página, acção e endpoint (`[Authorize(Policy = "...")]`), conforme matriz de Perfis e permissões.
- HTTPS obrigatório, HSTS, cookies Secure/HttpOnly/SameSite=Lax, anti-forgery em formulários.
- Validação de uploads (tipo real, tamanho, nome aleatório, fora de wwwroot).
- Segredos (connection string, SMTP, chaves) em appsettings.Production.json fora do repositório ou variáveis de ambiente do painel.
- Tabela de auditoria para emissão, anulação, alteração de configurações, login.

**Backups**

- Diário: backup SQL pelo painel Winhost + job próprio que exporta .bak ou BACPAC e copia para armazenamento externo (Azure Blob, S3 ou Backblaze) com cifra.
- Diário: cópia da pasta de uploads/PDFs para o mesmo destino.
- Retenção: 7 diários, 4 semanais, 12 mensais.
- Teste de restauro mensal documentado.

**Disponibilidade e performance**

- Página `/health` (BD, disco, fila de email) monitorizada externamente (UptimeRobot ou similar) a cada 5 min.
- Aquecimento: Scheduled Task chama o site para evitar arranque a frio após reciclagem.
- Índices em Documento(Data, Estado), Documento(ClienteId, Estado), PagamentoAlocacao(DocumentoId).
- Listagens paginadas (25–100), consultas AsNoTracking, compressão de resposta, cache de configurações por tenant.
- Meta: ecrãs < 1 s, PDF < 2 s, emissão < 500 ms com 50 utilizadores simultâneos.

## 13. Plano de entregas e critérios de aceitação

Seis fases em cerca de 15 semanas; a certificação AT corre em paralelo desde a fase 1, porque condiciona o modelo de dados.

1. **Fundação (2 sem):** solução, Identity, perfis e permissões, configurações, logótipo, contas bancárias, deploy de teste no Winhost, CI/CD. Iniciar contacto com AT.
2. **Clientes e artigos (1 sem):** CRM base, importação CSV.
3. **Documentos (4 sem):** séries, FT/FR/NC/ND/FG/CT/GR, cálculo, emissão imutável, hash, PDF, imprimir/download/email.
4. **Recibos e conta corrente (2 sem):** pagamentos, alocação múltipla, extractos, antiguidade de saldos.
5. **Dashboard, comunicação, automação, API, webhooks (4 sem):** dashboard por perfil, modelos de comunicação editáveis, link seguro, pagamento comunicado, jobs/tick, recorrentes, lembretes, API v1, webhooks, Swagger.
6. **Fiscal e produção (2 sem):** exportação mensal AT, backups externos, monitorização, testes de carga, documentação, submissão para certificação.

**Critérios de aceitação**

- 2 utilizadores a emitir em simultâneo na mesma série nunca geram número duplicado nem salto.
- Documento emitido não pode ser alterado por UI, API ou SQL da aplicação; tentativa fica em auditoria.
- Caso de teste da factura modelo: 30.804,10 − 20% → base 24.643,28 → IVA 16% 3.942,92 → total 28.586,20.
- NC limitada ao valor por creditar; saldo do cliente actualiza correctamente.
- Recibo de 1 pagamento alocado a 3 facturas fecha as 3 e o extracto bate certo.
- Logótipo PNG/JPG aparece no PDF; outros formatos rejeitados.
- PDF, impressão e email produzem o mesmo documento; email chega com anexo.
- Factura recorrente mensal emitida e enviada no dia certo via Scheduled Task.
- Webhook assinado entregue e repetido após falha.
- Ficheiro mensal AT gerado no formato oficial e validado.
- Restauro de backup testado com sucesso.
- Cada perfil só acede aos ecrãs, acções e endpoints permitidos (teste automático por perfil).
- Dashboard abre em menos de 2 s e mostra indicadores certos para cada perfil.
- Facturador com desconto acima do limite gera pedido de aprovação; Gestor aprova no dashboard.
- Alterar texto de um modelo de email reflecte-se no envio seguinte sem novo deploy.
- Link seguro abre documento sem login, expira na data e regista visualização.
- Pagamento comunicado pelo cliente só gera recibo após validação da Tesouraria.

**Entregáveis do programador:** código-fonte em repositório Git, scripts de migração EF, manual de instalação no Winhost, manual do utilizador, colecção de testes da API, documentação OpenAPI.
