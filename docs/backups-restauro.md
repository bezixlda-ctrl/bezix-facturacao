# Backups e restauro

O backup diário copia a base de dados (BACPAC) e os ficheiros da aplicação para fora do Winhost, cifrados (decisão D23). Este guia cobre a configuração, a verificação e o restauro. O teste de restauro deve ser feito **todos os meses** e registado no fim deste documento.

## 1. Configurar

### Chave de cifra

Gerar uma chave de 256 bits (PowerShell, num computador de confiança):

```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

Guardar a chave em **dois sítios fora do servidor** (gestor de passwords da empresa e um cofre/papel em local seguro). **Sem esta chave os backups não se abrem**, e quem a tiver (com acesso ao armazenamento) consegue ler todos os dados.

### Destino

Criar um bucket privado num armazenamento compatível com S3 (Backblaze B2, Amazon S3, Wasabi, Cloudflare R2…) e uma chave de acesso **só para esse bucket**, com permissão para ler, escrever, listar e apagar. Activar o bloqueio de objectos (*object lock*) ou o versionamento, se o fornecedor o permitir, para que um atacante com acesso ao servidor não consiga apagar os backups antigos.

No `appsettings.Production.json` (fora do repositório):

```json
"Backups": {
  "Destino": "S3",
  "Chave": "<chave Base64 gerada acima>",
  "S3": {
    "Endpoint": "https://s3.eu-central-003.backblazeb2.com",
    "Regiao": "eu-central-003",
    "Bucket": "bezix-facturacao-backups",
    "Prefixo": "producao",
    "ChaveAcesso": "<key id>",
    "ChaveSecreta": "<application key>"
  }
}
```

Para Amazon S3, deixar `Endpoint` vazio e indicar a região (ex.: `eu-west-1`). Para copiar para outro disco ou partilha de rede: `"Destino": "Pasta", "Pasta": "D:\\Backups\\Bezix"`.

### Tarefa agendada

No *Scheduled Task Tool* do Winhost, uma segunda tarefa (além do `jobs/tick`), **de hora a hora**:

```
https://[dominio]/internal/jobs/backup?token=[Jobs:Token]
```

O backup só corre se o último tiver mais de 20 horas (`Backups:IntervaloHoras`). Chamar de hora a hora garante que, se uma tentativa falhar, a seguinte repete. A resposta é JSON com o que foi enviado e apagado.

### Retenção

Guardam-se o backup mais recente de cada um dos últimos 7 dias, das últimas 4 semanas e dos últimos 12 meses. Os restantes são apagados do destino depois de cada backup.

## 2. Verificar

- **Administração → Backups**: lista os ficheiros no destino. O botão **Verificar** descarrega um ficheiro, decifra-o e confirma que abre (BACPAC com `model.xml`; ZIP com os ficheiros), sem restaurar nada.
- **`/health`**: a verificação `backups` passa a "Degraded" se não houver backup com sucesso há mais de 30 horas. O monitor externo (UptimeRobot) deve alertar quando o estado não for `Healthy`.

## 3. Restaurar

Cada backup tem dois ficheiros com a mesma data e hora:

- `bezix-AAAAMMDD-HHMMSS-bd.bacpac.bzx`: base de dados.
- `bezix-AAAAMMDD-HHMMSS-ficheiros.zip.bzx`: pastas `uploads/` (PDFs, logótipo, comprovativos, exportações AT) e `chaves/` (chaves de cifra da aplicação).

### Passo 1: descarregar e decifrar

Descarregar os dois ficheiros do bucket (consola do fornecedor ou `aws s3 cp`). Decifrar com a própria aplicação (pasta publicada; não precisa de base de dados):

```powershell
$env:BEZIX_BACKUP_CHAVE = "<chave Base64>"
.\Bezix.Facturacao.Web.exe decifrar-backup bezix-20261009-020000-bd.bacpac.bzx bd.bacpac
.\Bezix.Facturacao.Web.exe decifrar-backup bezix-20261009-020000-ficheiros.zip.bzx ficheiros.zip
Remove-Item env:BEZIX_BACKUP_CHAVE
```

Se o ficheiro tiver sido alterado ou a chave estiver errada, o comando falha com "Não foi possível decifrar" e não escreve nada utilizável.

### Passo 2: base de dados

O BACPAC importa-se para uma **base de dados vazia**:

1. No painel do Winhost, criar uma base de dados nova (ou, em recuperação total, apagar e recriar a existente).
2. Num PC com o [SqlPackage](https://learn.microsoft.com/sql/tools/sqlpackage/sqlpackage-download) (ou SSMS → *Import Data-tier Application*):

   ```powershell
   SqlPackage /Action:Import /SourceFile:bd.bacpac /TargetConnectionString:"Server=<servidor Winhost>;Database=<base nova>;User Id=<utilizador>;Password=<password>;TrustServerCertificate=True"
   ```

3. A importação recria tabelas, índices, dados e os **triggers de imutabilidade**. Não é preciso correr migrações (o histórico `__EFMigrationsHistory` vem no backup).

### Passo 3: ficheiros

Descompactar `ficheiros.zip`:

- `uploads/` → para a pasta definida em `Ficheiros:Raiz` (por omissão `App_Data/uploads`).
- `chaves/` → para `App_Data/chaves`. Sem estas chaves, as sessões abertas terminam (os utilizadores entram de novo) e os segredos dos webhooks deixam de se poder decifrar (gerar novos em Integrações).

### Passo 4: apontar a aplicação e confirmar

1. Actualizar `ConnectionStrings:BaseDados` se a base tiver outro nome e reciclar o app pool.
2. `/health` → `Healthy` (ou `Degraded` só por tarefas/backups ainda não corridos).
3. Entrar, abrir um documento recente e o PDF; em Relatórios → Exportação AT → **Verificar todas as séries** (todas íntegras).
4. Registar o restauro na tabela abaixo.

## 4. Teste mensal de restauro

Restaurar o backup mais recente numa base **de teste** (nunca por cima da produção), seguindo a secção 3, e confirmar:

- [ ] Os dois ficheiros decifram com a chave guardada fora do servidor.
- [ ] O BACPAC importa sem erros.
- [ ] N.º de documentos e total facturado do mês anterior iguais aos da produção (Relatórios → Vendas).
- [ ] Verificação de integridade: todas as séries íntegras.
- [ ] Um PDF antigo abre a partir de `uploads/`.
- [ ] Apagar a base de teste no fim.

| Data | Backup restaurado | Quem | Resultado | Observações |
|---|---|---|---|---|
| 09/10/2026 | Teste automático (`FiscalTests.Backup_cifrado_e_restaurado…`) | Desenvolvimento | OK | BACPAC importado em LocalDB; documentos, hashes e triggers conferidos |
