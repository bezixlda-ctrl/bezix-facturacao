# Dossier para a certificação AT

Documento de apoio ao pedido de certificação do programa de facturação (Aviso n.º 40/AT/DGI/2025). Mostra como cada requisito fiscal está implementado e onde verificá-lo, e o que falta à espera da resposta ao [ofício](oficio-AT.md). Actualizar quando a AT responder.

## Identificação

| | |
|---|---|
| Programa | Bezix Facturação, versão 1.0 |
| Produtor / utilizador | [Nome legal da empresa], NUIT [ ] (uso próprio) |
| Tecnologia | ASP.NET Core 10, SQL Server; alojamento Winhost |
| Identificação nos documentos | Configuração `Certificacao` (nome, versão, n.º de certificado), impressa no rodapé de cada PDF |

## Requisitos e implementação

| # | Requisito | Implementação | Onde verificar |
|---|---|---|---|
| 1 | Numeração sequencial sem saltos por tipo, série e ano (`FT A2026/0001`) | Número atribuído numa transacção com a série bloqueada (`UPDLOCK, HOLDLOCK`); índice único (série, número) | Decisões D15 e D24; teste "12 emissões simultâneas" e teste de carga (2.498 emissões, sem saltos) |
| 2 | Documento emitido imutável | Duas camadas: interceptor da aplicação e **triggers SQL** em documentos, linhas, resumo de IVA, pagamentos e alocações; tentativas ficam na auditoria | D14; testes de imutabilidade por UI, API e SQL |
| 3 | Correcção só por NC/ND ou anulação com motivo | NC/ND ligadas ao documento de origem, limitadas ao valor por creditar; anulação exige motivo e regista utilizador e data | Documentos → Detalhe; relatório de anulados |
| 4 | Data não anterior à do último documento da série | Validada com a série bloqueada | Teste de emissão |
| 5 | Campos obrigatórios (emitente, adquirente, NUIT, base por taxa, IVA, motivo de isenção, total, MZN, "Processado por computador") | Dados do emitente e do cliente congelados no documento na emissão; resumo de IVA por taxa; motivo obrigatório para IVA 0 | PDF de qualquer factura; [regras-calculo.md](regras-calculo.md) |
| 6 | Desconto antes do IVA; arredondamento documentado | Calculadora de domínio; factura modelo 30.804,10 − 20% → 24.643,28; IVA 3.942,92; total 28.586,20 | [regras-calculo.md](regras-calculo.md); teste da factura modelo |
| 7 | Taxas e isenções parametrizáveis com vigência | Tabela de taxas com data de início; a emissão usa a taxa em vigor na data do documento | Configurações → Taxas de IVA |
| 8 | Encadeamento de integridade | SHA-256 de (data; data/hora de emissão; número; total; hash anterior da série); 4 caracteres no rodapé; verificação de toda a série antes de cada exportação | D22; Relatórios → Exportação AT → Verificar todas as séries |
| 9 | Exportação mensal comprimida, com histórico | `IExportadorAT`; ZIP com SHA-256 guardado; histórico com quem gerou e referência da submissão | D22; Relatórios → Exportação AT |
| 10 | Retenção (assumido 10 anos) | Documentos não se apagam (triggers); PDFs guardados fora de wwwroot; backups diários cifrados com retenção 7/4/12 e restauro testado | D23; [backups-restauro.md](backups-restauro.md) |
| — | Auditoria | Emissão, anulação, alterações de configuração, entradas, exportações, integridade | Administração → Auditoria |
| — | Controlo de acessos | Perfis com permissões; 2FA obrigatório para administradores; teste automático da matriz de acessos | D25; matriz em [especificacao.md](especificacao.md) |

## A aguardar a AT

| Item | Estado | Impacto quando chegar |
|---|---|---|
| Estrutura oficial do ficheiro mensal | Formato provisório `csv-provisorio-v1` | Nova implementação de `IExportadorAT`; sem mudanças no modelo de dados |
| Assinatura digital (algoritmo, chave, entrega da chave pública) | Só hash SHA-256 encadeado | Assinar o hash na emissão (campo novo no documento), com a chave privada fora da base de dados |
| Menção/código de certificação | Texto "certificação AT pendente" | Preencher `Certificacao:NumeroCertificado`; sem novo deploy de código |
| Limite do NUIT para não sujeitos passivos | Configurável, 0 por omissão | Configurações → Empresa |
| Regras da factura global | Implementada como factura com várias guias de origem | Ajustar validações, se necessário |

## Para a submissão

- [ ] Ofício entregue e registado (ver [oficio-AT.md](oficio-AT.md)).
- [ ] Resposta da AT arquivada nesta pasta.
- [ ] Exportador oficial implementado e validado com o validador da AT (se existir).
- [ ] Assinatura digital implementada.
- [ ] Conjunto de documentos de teste (FT, FR, FG, NC, ND, RC, anulado) gerado num mês de teste e exportado.
- [ ] Este dossier actualizado e entregue com o pedido.
