# Instalação no Winhost

Guia para o deploy de teste (fase 1) e de produção. Requer o plano **Ultimate** ou **Power** (Scheduled Task Tool e mais memória por site).

## 1. Preparar no painel do Winhost

1. **Site:** criar o site (ou subdomínio de teste, ex.: `teste.bezix.co.mz`). Em *Site Info Manager*, confirmar que o app pool está em **No Managed Code** e em **64 bits**.
2. **Base de dados:** *MS SQL Manager* → criar uma base de dados SQL Server. Anotar servidor, nome, utilizador e password.
3. **SSL:** *SSL Manager* → activar o certificado para o domínio. A aplicação força HTTPS e HSTS fora do ambiente de desenvolvimento.

## 2. Configurar os segredos

Os segredos nunca vão para o repositório. Criar no servidor, na pasta do site, o ficheiro `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "BaseDados": "Server=[servidor];Database=[base];User Id=[utilizador];Password=[password];Encrypt=True;TrustServerCertificate=True"
  },
  "Inicializacao": {
    "AdministradorEmail": "[email do primeiro administrador]",
    "AdministradorNome": "[Nome]",
    "AdministradorPassword": "[password temporária forte]"
  },
  "Jobs": {
    "Token": "[texto aleatório com 32 ou mais caracteres]"
  },
  "Email": {
    "Servidor": "[smtp do Winhost, SendGrid ou Brevo]",
    "Porta": 587,
    "Utilizador": "[utilizador SMTP]",
    "Password": "[password SMTP]",
    "Remetente": "facturacao@[dominio]",
    "NomeRemetente": "[Nome da empresa]",
    "Seguranca": "StartTls"
  }
}
```

- Sem `Email:Servidor`, os emails não saem: ficam gravados como ficheiros `.eml` em `App_Data/emails` (útil para testes). Os emails que falham ficam na fila e são repetidos pelo `jobs/tick` (1, 5 e 30 min, 2 h e 12 h).

- O primeiro administrador só é criado se a base de dados ainda não tiver utilizadores. Depois de entrar e mudar a password, **apagar o bloco `Inicializacao`** do ficheiro.
- Para gerar o token: `[Convert]::ToBase64String((1..32 | % { Get-Random -Max 256 }) -as [byte[]])` no PowerShell, ou um gerador de passwords.

## 3. Publicar

No computador de desenvolvimento (ou descarregar o artefacto `bezix-facturacao-winhost` do CI):

```bash
dotnet publish src/Bezix.Facturacao.Web -p:PublishProfile=Winhost
```

O resultado fica em `artifacts/publish/winhost` (self-contained, ~135 MB). Enviar o conteúdo para a raiz do site por **FTP** ou **Web Deploy**.

**Atenção:** a pasta `App_Data` do servidor guarda logótipos, PDFs emitidos (retenção legal de 10 anos) e logs. Nunca a apagar nem sobrepor. O perfil de publicação já a exclui; no FTP, não usar a opção "apagar ficheiros que não existem localmente".

Para actualizar com o site parado (evita ficheiros bloqueados): enviar primeiro um ficheiro `app_offline.htm` para a raiz, enviar os ficheiros novos, e apagar o `app_offline.htm`.

## 4. Base de dados

No arranque, a aplicação aplica as migrações em falta e cria os dados iniciais (perfis, taxas de IVA, séries do ano, primeiro administrador). Se preferir aplicar as migrações à mão, o CI gera `migracoes.sql` (script idempotente) para correr no *MS SQL Manager*; nesse caso definir `"BaseDados": { "AplicarMigracoesNoArranque": false }`. Os dados iniciais são sempre criados no arranque, se faltarem.

## 5. Tarefas agendadas (Scheduled Task Tool)

O Winhost não tem processos permanentes. Criar uma tarefa que chama, **a cada 15 minutos**:

```
https://[dominio]/internal/jobs/tick?token=[Jobs:Token]
```

Esta chamada também aquece o site depois de uma reciclagem do app pool. Corre os emails em fila, as facturas recorrentes, os lembretes de pagamento, os webhooks e a limpeza (decisão D18). O resultado de cada execução aparece em **Automação → Últimas execuções**.

Os lembretes saem a partir da hora definida no plano (8 h por omissão, hora de Maputo). Com a tarefa de 15 em 15 minutos, saem no máximo 15 minutos depois dessa hora.

### Endereço público e chaves de cifra

- Em **Configurações → Empresa**, preencher o **endereço público** (ex.: `https://facturacao.empresa.co.mz`): é a base dos links que os clientes recebem por email. Sem ele, usa-se o endereço por onde chegou o pedido (nos lembretes e facturas recorrentes, o endereço configurado na tarefa agendada), o que pode não ser o que os clientes devem ver.
- As chaves de cifra (sessões, segredos dos webhooks) ficam em `App_Data/chaves`. **Incluir esta pasta nos backups**: se se perder, as sessões terminam e os segredos dos webhooks têm de ser gerados de novo.

## 6. Monitorização

Configurar o UptimeRobot (ou semelhante) para verificar `https://[dominio]/health` a cada 5 minutos. Resposta 200 com `"estado":"Healthy"` = base de dados e pasta de ficheiros a funcionar.

## 7. Verificação depois do deploy

- [ ] `https://[dominio]/health` responde `Healthy`.
- [ ] A página de entrada abre em HTTPS e o cadeado do browser está válido.
- [ ] Entrar com o primeiro administrador, mudar a password e activar a verificação em dois passos.
- [ ] Apagar o bloco `Inicializacao` do `appsettings.Production.json`.
- [ ] Seguir a lista "Preparar o sistema para facturar" no painel (dados fiscais, logótipo, contas, segundo administrador).
- [ ] A tarefa agendada aparece no log (`App_Data/logs`) como `jobs/tick executado`.
- [ ] Confirmar que o runtime self-contained arranca no Winhost (decisão D1).

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| Erro HTTP 500.30 | A aplicação não arrancou: ver `App_Data/logs/log-*.txt`. Normalmente connection string errada. Para mais detalhe, pôr `stdoutLogEnabled="true"` no `web.config` temporariamente. |
| Erro HTTP 500.19 | Módulo ASP.NET Core em falta no servidor: pedir ao suporte do Winhost o *ASP.NET Core Module V2*. |
| Login volta sempre ao ecrã de entrada | O site está a ser servido por HTTP: os cookies de sessão só funcionam em HTTPS. Activar o SSL. |
| `jobs/tick` responde 404 | Token em falta ou diferente do `Jobs:Token`. |
