# Arranque em produção

Passagem do servidor actual (com documentos de teste) para produção real, sem levar documentos de teste para a numeração fiscal. Decisão [D28](decisoes.md#d28-separação-entre-teste-e-produção).

## Princípio

Documentos emitidos não se apagam (triggers, decisão D14). Os documentos de teste que estão hoje na base de dados ficariam para sempre nos relatórios, na conta corrente e na exportação AT, e já consumiram números da série. Por isso:

| | Hoje | Depois |
|---|---|---|
| `https://bezixcenter.net/facturacao` | Aplicação + BD com documentos de teste | **Produção**: mesma versão, **BD nova e vazia** |
| `https://bezixcenter.net/facturacao-teste` | — | **Teste**: cópia da aplicação actual, BD actual (com os documentos de teste), faixa "AMBIENTE DE TESTE", emails só para endereços internos |

Clientes e artigos passam para a produção por CSV. Utilizadores, empresa, contas, carimbo e modelos de email configuram-se de novo (são poucos e assim as palavras-passe também são novas).

## 0. Antes de começar

- [x] Facturar antes da licença AT: permitido (decisão [D29](decisoes.md#d29-emissão-antes-da-licença-at)). A memória descritiva foi submetida; a licença confirma depois o programa como autorizado.
- [ ] Código-fonte num repositório remoto privado (`git push`), não só no PC.
- [ ] Fornecedor de backups externos escolhido e configurado ([backups-restauro.md](backups-restauro.md)).
- [ ] Pacote da versão a instalar gerado com `scripts\publicar-winhost.ps1` (o mesmo pacote serve para os dois sites).
- [ ] Hora combinada com os utilizadores: a facturação fica parada ~1 hora.

## 1. Backup do estado actual

1. Na aplicação actual: **Administração → Backups → Fazer backup agora** e **Verificar**.
2. No painel Winhost: backup da base de dados (MS SQL Manager → Backup) e descarregar o `.bak`.
3. Descarregar por FTP a pasta `/facturacao/App_Data` inteira (PDFs, logótipo, carimbo, chaves).

Guardar os três com o nome `pre-producao-<data>`.

## 2. Criar o site de teste

1. Painel Winhost: criar a pasta `/facturacao-teste` e marcá-la como aplicação (Application Starter), com o mesmo app pool x86 da `/facturacao`.
2. Copiar por FTP todo o conteúdo de `/facturacao` para `/facturacao-teste` (incluindo `App_Data`).
3. Em `/facturacao-teste/appsettings.Production.json`:
   - manter a `ConnectionStrings:BaseDados` actual (BD com os documentos de teste);
   - acrescentar ou mudar:

   ```json
   "Certificacao": { "AmbienteTeste": true },
   "Email": {
     "RedireccionarPara": "testes@bezixcenter.net",
     "PrefixoAssunto": "[TESTE] "
   },
   "Jobs": { "Token": "[token NOVO, diferente do da produção]" }
   ```

   Se `Email:RedireccionarPara` ficar vazio, o teste não envia nenhum email: grava-os em `App_Data/emails`.
   - Em `Backups`, usar outra pasta/bucket do que a produção, ou retirar a secção (o teste não precisa de backups externos).
4. Abrir `https://bezixcenter.net/facturacao-teste`: tem de aparecer a faixa amarela **AMBIENTE DE TESTE** e os PDFs saem com **SEM VALOR FISCAL**.
5. No teste: **Configurações → Empresa → endereço público** = `https://bezixcenter.net/facturacao-teste`; **Integrações → Webhooks**: desactivar todas as subscrições.
6. Agendamento do teste (opcional): uma tarefa `jobs/tick` para `/facturacao-teste/internal/jobs/tick?token=...` só se quiser testar lembretes e recorrentes.

## 3. Exportar clientes e artigos

No SSMS, ligado à BD actual (a de teste), correr [scripts/sql/exportar-dados-mestre.sql](../scripts/sql/exportar-dados-mestre.sql) como indicado no início do ficheiro. Resultado: `clientes.csv`, `artigos.csv` e os totais de controlo.

## 4. Produção com base de dados nova

1. Painel Winhost: criar a base de dados nova (ex.: `bezix_facturacao_prod`) e o utilizador SQL.
2. Enviar `scripts/app_offline.htm` para `/facturacao` (a aplicação pára).
3. Por FTP, mudar o nome de `/facturacao/App_Data` para `/facturacao/App_Data-teste-<data>` (os PDFs de teste não ficam na produção; a cópia já está no site de teste e no backup do passo 1).
4. Enviar o conteúdo do pacote para `/facturacao` (como em [instalacao-winhost.md](instalacao-winhost.md), "Actualizações").
5. Em `/facturacao/appsettings.Production.json`:
   - `ConnectionStrings:BaseDados` → **BD nova**;
   - bloco `Inicializacao` com o primeiro administrador (como numa primeira instalação);
   - `Certificacao:AmbienteTeste` **ausente ou `false`**; `Email:RedireccionarPara` e `Email:PrefixoAssunto` **ausentes**;
   - restantes secções (Email SMTP, Jobs, Backups) como estavam.
6. Apagar `/facturacao/app_offline.htm`. No primeiro pedido a aplicação cria as tabelas, as taxas, os perfis e o administrador.

## 5. Configurar a produção

Seguir a lista "Preparar o sistema para facturar" no painel:

- [ ] Entrar com o administrador e mudar a password; apagar o bloco `Inicializacao` do servidor.
- [ ] Configurações → Empresa: dados fiscais, endereço público `https://bezixcenter.net/facturacao`, logótipo, carimbo e assinatura (carregar os ficheiros originais).
- [ ] Contas bancárias, séries (confirmar que começam no n.º 1), taxas de IVA e motivo de isenção (confirmados pelo contabilista).
- [ ] Utilizadores: criar de novo, com passwords temporárias novas (mudança obrigatória no primeiro acesso). Segundo administrador.
- [ ] Modelos de comunicação e lembretes, se tinham sido alterados no teste.
- [ ] Clientes → Importar `clientes.csv`; Artigos → Importar `artigos.csv`. Conferir com os totais de controlo do passo 3.
- [ ] Webhooks e chaves API: criar de novo só os necessários.

**Não emitir documentos para "experimentar" na produção.** Para ver o aspecto do PDF, abrir o PDF de um **rascunho** (sai com a marca RASCUNHO), ou usar o site de teste.

## 6. Verificação

- [ ] `https://bezixcenter.net/facturacao/health` → `Healthy`; sem faixa de teste; PDF de um rascunho sem "SEM VALOR FISCAL".
- [ ] `https://bezixcenter.net/facturacao-teste/health` → `Healthy`; com faixa de teste.
- [ ] Relatórios → Exportação AT na produção: zero documentos.
- [ ] Backup da produção: **Fazer backup agora** e **Verificar**.
- [ ] Tarefa agendada da produção a correr (`jobs/tick executado` no log).
- [ ] `https://bezixcenter.net/portal/login.php` continua a funcionar.
- [ ] Restantes pontos de [instalacao-winhost.md](instalacao-winhost.md) §7.

## Se correr mal

Enquanto não houver documentos reais na produção, nada se perde: voltar a pôr `app_offline.htm`, corrigir a configuração (ou repor a `ConnectionStrings:BaseDados` antiga e o nome original de `App_Data`) e repetir. Depois do primeiro documento real, a BD de produção só se restaura a partir dos backups dela.
