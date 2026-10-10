# Instalação no Winhost

Guia para o deploy de teste (fase 1) e de produção. Requer o plano **Ultimate** ou **Power** (Scheduled Task Tool e mais memória por site).

## 1. Preparar no painel do Winhost

O sistema fica em **https://bezixcenter.net/facturacao/**, como sub-aplicação do site `bezixcenter.net`, ao lado dos outros sistemas da Bezix (ex.: `https://bezixcenter.net/portal/`). Não precisa de domínio nem de site próprio.

1. **Pasta:** pelo FTP (ou *File Manager*), criar a pasta `facturacao` na raiz do site `bezixcenter.net`.
2. **Aplicação:** em *Site Manager → Application Starting Point*, marcar `/facturacao` como ponto de partida de aplicação. Sem isto o IIS não arranca o ASP.NET Core nessa pasta e os pedidos caem no site principal.
3. **App pool:** em *Site Info Manager*, ver se o app pool está em 32 ou 64 bits (*Enable 32-bit applications*). O pacote tem de ser da mesma arquitectura: `-Arquitectura x64` (por omissão) ou `-Arquitectura x86` no script de publicação. Com a arquitectura errada aparece o erro **HTTP 500.32 – Failed to load .NET Core host**. O ideal é **No Managed Code**; se o site principal precisar de outra opção (ex.: aplicações ASP.NET clássicas), manter: o ASP.NET Core funciona na mesma.
   Atenção: o modo *in-process* só permite **uma** aplicação ASP.NET Core por app pool. Se mais tarde for instalado outro sistema ASP.NET Core em `bezixcenter.net`, pedir ao suporte do Winhost um app pool separado para um deles.
4. **Base de dados:** *MS SQL Manager* → criar uma base de dados SQL Server para a facturação (não usar a dos outros sistemas). Anotar servidor, nome, utilizador e password.
5. **SSL:** o certificado de `bezixcenter.net` já cobre `/facturacao`. A aplicação força HTTPS.
6. **Regras de reescrita do site principal:** se o `web.config` da raiz de `bezixcenter.net` tiver regras de *URL Rewrite* que enviam tudo para um `index.php`, excluir a pasta da facturação, por exemplo com esta condição na regra:

   ```xml
   <add input="{REQUEST_URI}" pattern="^/facturacao(/|$)" negate="true" />
   ```

   Sem regras desse tipo (como em `/portal/login.php`), não é preciso nada.

**Convivência com os outros sistemas**

- **Cookies:** a sessão (`bezix.sessao`) e o antiforgery ficam limitados ao caminho `/facturacao`: não são enviados ao portal nem aos outros sistemas, e os deles não interferem.
- **HSTS:** a aplicação pede aos browsers HTTPS obrigatório para `bezixcenter.net` durante 180 dias, **sem** subdomínios (`Seguranca:HstsSubdominios = false`). Só activar os subdomínios quando todos tiverem HTTPS.
- **Links:** todos os endereços da aplicação (páginas, ficheiros, API, emails para clientes, Swagger) incluem `/facturacao`; há um teste automático que o confirma.
- **Subdomínio em alternativa:** para usar `facturacao.bezixcenter.net`, criar o subdomínio no painel apontado para a pasta `facturacao` e seguir os mesmos passos. Não é preciso mudar nada na aplicação.

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
    "Remetente": "facturacao@bezixcenter.net",
    "NomeRemetente": "[Nome da empresa]",
    "Seguranca": "StartTls"
  },
  "Backups": {
    "Destino": "S3",
    "Chave": "[chave AES-256 em Base64: ver backups-restauro.md]",
    "S3": { "Endpoint": "[endpoint]", "Regiao": "[região]", "Bucket": "[bucket]", "Prefixo": "producao", "ChaveAcesso": "[key id]", "ChaveSecreta": "[secret]" }
  },
  "Certificacao": {
    "NumeroCertificado": ""
  }
}
```

- `Backups`: destino dos backups diários cifrados ([backups-restauro.md](backups-restauro.md)). Sem esta secção, o `/health` avisa que não há backups externos.
- `Certificacao:NumeroCertificado`: deixar vazio até a AT atribuir o número; depois preencher (aparece no rodapé dos documentos) e reciclar o app pool.
- Os administradores têm de activar a verificação em dois passos no primeiro acesso (decisão D25).

- Sem `Email:Servidor`, os emails não saem: ficam gravados como ficheiros `.eml` em `App_Data/emails` (útil para testes). Os emails que falham ficam na fila e são repetidos pelo `jobs/tick` (1, 5 e 30 min, 2 h e 12 h).

- O primeiro administrador só é criado se a base de dados ainda não tiver utilizadores. Depois de entrar e mudar a password, **apagar o bloco `Inicializacao`** do ficheiro.
- Para gerar o token (PowerShell): `$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)`, ou um gerador de passwords.

## 3. Publicar

### Gerar o pacote

No computador de desenvolvimento, com o código no commit a publicar (de preferência no ramo `main`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publicar-winhost.ps1
```

O script:

1. recusa continuar se houver alterações por gravar (o pacote corresponde sempre a um commit);
2. corre os testes;
3. publica em `artifacts/publish/winhost` (self-contained win-x64, ~193 MB);
4. gera `artifacts/publish/migracoes.sql`;
5. confirma que o pacote não leva `appsettings.Development.json`, `appsettings.Production.json`, símbolos `.pdb` nem `App_Data`;
6. cria `artifacts/bezix-facturacao-winhost-<data>-<commit>.zip` (~80 MB) com o `.sha256` ao lado, e copia `artifacts/app_offline.htm`.

O ficheiro `VERSAO.txt` dentro do pacote indica o commit e a data. Para um pacote de teste com alterações por gravar: `-PermitirAlteracoes`. O CI gera o mesmo conteúdo (artefacto `bezix-facturacao-winhost`).

O pacote de 09/10/2026 foi testado localmente com o executável publicado, em modo Produção: `/health` responde, HTTP redirecciona para HTTPS e os cookies saem com `secure`, `httponly` e `samesite`.

### Primeira instalação

1. Descompactar o ZIP no computador e enviar o conteúdo para a pasta **`/facturacao`** por **FTP** (ou Web Deploy). Nunca para a raiz de `bezixcenter.net`.
2. Criar no servidor o `appsettings.Production.json` a partir de [appsettings.Production.exemplo.json](appsettings.Production.exemplo.json) (secção 2).
3. Dar ao utilizador do app pool permissão de **escrita** em `App_Data` (é criada no primeiro arranque; no painel do Winhost, nas permissões de ficheiros do site).
4. Abrir `https://bezixcenter.net/facturacao/health` e seguir a verificação da secção 7.

### Actualizações

1. Enviar `artifacts/app_offline.htm` para a pasta `/facturacao` (**não** para a raiz do domínio, que pararia o site principal): a aplicação pára, os ficheiros ficam livres e os utilizadores vêem "O sistema está a ser actualizado".
2. Enviar os ficheiros do novo pacote **por cima** dos existentes.
3. Apagar `/facturacao/app_offline.htm` do servidor. No primeiro pedido, a aplicação arranca e aplica as migrações em falta.
4. Abrir `/health` e confirmar a versão em `VERSAO.txt`.

**Atenção:** a pasta `App_Data` do servidor guarda logótipos, PDFs emitidos (retenção legal de 10 anos), comprovativos, chaves de cifra e logs. Nunca a apagar nem sobrepor. O pacote não a inclui; no cliente FTP, não usar a opção "apagar ficheiros que não existem localmente". O `appsettings.Production.json` também só existe no servidor.

**Voltar à versão anterior:** guardar o ZIP de cada versão publicada. Para reverter, repetir os passos com o ZIP anterior. Se a versão nova tiver aplicado uma migração, restaurar também a base de dados do backup anterior à actualização ([backups-restauro.md](backups-restauro.md)); por isso, fazer **Backups → Fazer backup agora** antes de cada actualização.

## 4. Base de dados

No arranque, a aplicação aplica as migrações em falta e cria os dados iniciais (perfis, taxas de IVA, séries do ano, primeiro administrador). Se preferir aplicar as migrações à mão, o CI gera `migracoes.sql` (script idempotente) para correr no *MS SQL Manager*; nesse caso definir `"BaseDados": { "AplicarMigracoesNoArranque": false }`. Os dados iniciais são sempre criados no arranque, se faltarem.

## 5. Tarefas agendadas (Scheduled Task Tool)

O Winhost não tem processos permanentes. Criar uma tarefa que chama, **a cada 15 minutos**:

```
https://bezixcenter.net/facturacao/internal/jobs/tick?token=[Jobs:Token]
```

Esta chamada também aquece o site depois de uma reciclagem do app pool. Corre os emails em fila, as facturas recorrentes, os lembretes de pagamento, os webhooks e a limpeza (decisão D18). O resultado de cada execução aparece em **Automação → Últimas execuções**.

Criar uma **segunda tarefa**, de hora a hora, para os backups (só fazem backup se o último tiver mais de 20 horas):

```
https://bezixcenter.net/facturacao/internal/jobs/backup?token=[Jobs:Token]
```

Os lembretes saem a partir da hora definida no plano (8 h por omissão, hora de Maputo). Com a tarefa de 15 em 15 minutos, saem no máximo 15 minutos depois dessa hora.

### Endereço público e chaves de cifra

- Em **Configurações → Empresa**, preencher o **endereço público** (ex.: `https://facturacao.empresa.co.mz`): é a base dos links que os clientes recebem por email. Sem ele, usa-se o endereço por onde chegou o pedido (nos lembretes e facturas recorrentes, o endereço configurado na tarefa agendada), o que pode não ser o que os clientes devem ver.
- As chaves de cifra (sessões, segredos dos webhooks) ficam em `App_Data/chaves`. **Incluir esta pasta nos backups**: se se perder, as sessões terminam e os segredos dos webhooks têm de ser gerados de novo.

## 6. Monitorização

Configurar o UptimeRobot (ou semelhante) para verificar `https://bezixcenter.net/facturacao/health` a cada 5 minutos, com um monitor de **palavra-chave** que alerta quando a resposta não contém `"estado":"Healthy"`:

| Verificação | Unhealthy (503) | Degraded (200) |
|---|---|---|
| `baseDados` | Sem ligação | — |
| `ficheiros` | Pasta sem escrita | Menos de 200 MB livres |
| `tarefas` | — | `jobs/tick` não corre há mais de 1 hora |
| `emails` | — | Emails na fila há mais de 1 hora ou falhados nas últimas 24 h |
| `backups` | — | Sem backup externo com sucesso há mais de 30 horas, ou não configurado |

## 7. Verificação depois do deploy

- [ ] `https://bezixcenter.net/facturacao/health` responde `Healthy`.
- [ ] A página de entrada abre em HTTPS e o cadeado do browser está válido.
- [ ] Entrar com o primeiro administrador, mudar a password e activar a verificação em dois passos.
- [ ] Apagar o bloco `Inicializacao` do `appsettings.Production.json`.
- [ ] Seguir a lista "Preparar o sistema para facturar" no painel (dados fiscais, logótipo, contas, segundo administrador).
- [ ] A tarefa agendada aparece no log (`App_Data/logs`) como `jobs/tick executado`.
- [ ] Administração → Backups: **Fazer backup agora**, depois **Verificar** os dois ficheiros; fazer o primeiro teste de restauro ([backups-restauro.md](backups-restauro.md)).
- [ ] Configurações → Empresa: preencher o endereço público `https://bezixcenter.net/facturacao` (base dos links enviados aos clientes).
- [ ] Abrir `https://bezixcenter.net/portal/login.php` e confirmar que os outros sistemas continuam a funcionar.
- [ ] Correr o teste de carga contra o ambiente de teste no Winhost e registar em [carga.md](carga.md).
- [ ] Confirmar que o runtime self-contained arranca no Winhost (decisão D1).

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| Erro HTTP 500.30 | A aplicação não arrancou: ver `App_Data/logs/log-*.txt`. Normalmente connection string errada. Para mais detalhe, pôr `stdoutLogEnabled="true"` no `web.config` temporariamente. |
| Erro HTTP 500.19 | Módulo ASP.NET Core em falta no servidor: pedir ao suporte do Winhost o *ASP.NET Core Module V2*. |
| Login volta sempre ao ecrã de entrada | O site está a ser servido por HTTP: os cookies de sessão só funcionam em HTTPS. Activar o SSL. |
| `jobs/tick` responde 404 | Token em falta ou diferente do `Jobs:Token`. |
| HTTP 500.32 *Failed to load .NET Core host* | Pacote de 64 bits num app pool de 32 bits (ou o contrário). Mudar o app pool para 64 bits ou gerar o pacote com `-Arquitectura x86`. |
| HTTP 500.19 *Cannot add duplicate collection entry* | O `web.config` do site principal já declara a mesma regra; o nosso tem de a remover antes (`<remove …/>`). Já corrigido para `App_Data` e para o handler `aspNetCore`. |
| `jobs/backup` responde 500 | Ver a mensagem: credenciais ou bucket do destino errados, ou `Backups:Chave` que não é Base64 de 32 bytes. |
