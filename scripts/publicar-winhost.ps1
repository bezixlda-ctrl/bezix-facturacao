<#
.SYNOPSIS
    Prepara o pacote de publicação para o Winhost (self-contained win-x64).

.DESCRIPTION
    1. Confirma que não há alterações por gravar (o pacote corresponde a um commit).
    2. Corre os testes (salvo -SemTestes).
    3. Publica com o perfil Winhost para artifacts/publish/winhost.
    4. Gera artifacts/publish/migracoes.sql (idempotente, para aplicar à mão se preferir).
    5. Verifica o conteúdo: sem appsettings.Development.json, sem .pdb, sem App_Data, com web.config in-process.
    6. Cria artifacts/bezix-facturacao-winhost-<data>-<commit>.zip e o respectivo SHA-256.

    O pacote não leva segredos: o appsettings.Production.json é criado à parte no servidor
    (modelo em docs/appsettings.Production.exemplo.json).

.EXAMPLE
    pwsh scripts/publicar-winhost.ps1
    pwsh scripts/publicar-winhost.ps1 -SemTestes
#>
[CmdletBinding()]
param(
    [switch] $SemTestes,
    [switch] $PermitirAlteracoes,
    # x64 (por omissão) ou x86: tem de corresponder ao app pool do Winhost ("Enable 32-bit applications" = x86).
    [ValidateSet("x64", "x86")] [string] $Arquitectura = "x64"
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

function Passo([string] $texto) { Write-Host "`n== $texto" -ForegroundColor Cyan }
function Falha([string] $texto) { Write-Host "ERRO: $texto" -ForegroundColor Red; exit 1 }

Passo "Estado do repositório"
$alteracoes = git status --porcelain
if ($alteracoes -and -not $PermitirAlteracoes) { Falha "Há alterações por gravar. Faça commit (ou use -PermitirAlteracoes para um pacote de teste)." }
$commit = (git rev-parse --short HEAD).Trim()
$ramo = (git rev-parse --abbrev-ref HEAD).Trim()
$data = Get-Date -Format "yyyyMMdd-HHmm"
Write-Host "Ramo $ramo, commit $commit$(if ($alteracoes) { ' (com alterações por gravar)' })"
if ($ramo -ne "main") { Write-Host "Aviso: o pacote não é do ramo main." -ForegroundColor Yellow }

if (-not $SemTestes) {
    Passo "Testes"
    dotnet test --nologo -warnaserror
    if ($LASTEXITCODE -ne 0) { Falha "Os testes falharam; o pacote não foi criado." }
}

Passo "Publicação (self-contained win-$Arquitectura)"
# Pasta limpa: a publicação não substitui ficheiros nativos de outra arquitectura deixados por um pacote anterior.
$saidaAnterior = Join-Path $raiz "artifacts/publish/winhost"
if (Test-Path $saidaAnterior) { Remove-Item -LiteralPath $saidaAnterior -Recurse -Force }
# "-r" (e não só -p:RuntimeIdentifier) para o restore trazer o runtime e as bibliotecas nativas da mesma arquitectura.
dotnet publish src/Bezix.Facturacao.Web -p:PublishProfile=Winhost -r win-$Arquitectura --nologo
if ($LASTEXITCODE -ne 0) { Falha "A publicação falhou." }
$saida = Join-Path $raiz "artifacts/publish/winhost"

Passo "Script SQL das migrações"
dotnet tool restore | Out-Null
dotnet ef migrations script --idempotent -p src/Bezix.Facturacao.Infrastructure -s src/Bezix.Facturacao.Web -o artifacts/publish/migracoes.sql
if ($LASTEXITCODE -ne 0) { Falha "Não foi possível gerar o script das migrações." }

Passo "Verificação do conteúdo"
$proibidos = @(Get-ChildItem $saida -Recurse -File | Where-Object {
    $_.Name -eq "appsettings.Development.json" -or $_.Name -eq "appsettings.Production.json" -or $_.Extension -eq ".pdb" -or $_.FullName -match '\\App_Data\\'
})
if ($proibidos.Count -gt 0) { Falha ("O pacote tem ficheiros que não devem ir para o servidor: " + ($proibidos.Name -join ", ")) }
$webConfig = Get-Content (Join-Path $saida "web.config") -Raw
if ($webConfig -notmatch 'hostingModel="inprocess"' -or $webConfig -notmatch 'Bezix\.Facturacao\.Web\.exe') { Falha "web.config inesperado." }
# Todas as peças nativas têm de ser da arquitectura pedida (uma só de outra dá HTTP 500.32 no IIS).
function Maquina([string] $ficheiro) {
    $b = [IO.File]::ReadAllBytes($ficheiro); $pe = [BitConverter]::ToInt32($b, 0x3C)
    switch ([BitConverter]::ToUInt16($b, $pe + 4)) { 0x14c { "x86" } 0x8664 { "x64" } default { "outra" } }
}
foreach ($n in @("Bezix.Facturacao.Web.exe", "coreclr.dll", "hostfxr.dll", "libSkiaSharp.dll", "QuestPdfSkia.dll", "Microsoft.Data.SqlClient.SNI.dll")) {
    $f = Get-ChildItem $saida -Recurse -Filter $n | Select-Object -First 1
    if (-not $f) { Falha "Falta $n no pacote." }
    if ((Maquina $f.FullName) -ne $Arquitectura) { Falha "$n é $(Maquina $f.FullName), mas o pacote é $Arquitectura." }
}
Write-Host "Binários nativos: todos $Arquitectura"
foreach ($f in @("Bezix.Facturacao.Web.exe", "wwwroot/lib/phosphor/regular/Phosphor.woff2", "wwwroot/css/site.css")) {
    if (-not (Test-Path (Join-Path $saida $f))) { Falha "Falta $f no pacote." }
}
@"
Bezix Facturação
Commit:   $commit ($ramo)
Bits:     win-$Arquitectura
Gerado:   $(Get-Date -Format "yyyy-MM-dd HH:mm") por $env:USERNAME
"@ | Set-Content -Encoding utf8 (Join-Path $saida "VERSAO.txt")
$tamanho = (Get-ChildItem $saida -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("{0:N0} MB, {1} ficheiros" -f $tamanho, (Get-ChildItem $saida -Recurse -File).Count)

Passo "Pacote"
$zip = Join-Path $raiz "artifacts/bezix-facturacao-winhost-$Arquitectura-$data-$commit.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($saida, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -Encoding ascii "$zip.sha256"
Copy-Item (Join-Path $raiz "scripts/app_offline.htm") (Join-Path $raiz "artifacts/app_offline.htm") -Force

Write-Host ""
Write-Host "Pacote:   $zip" -ForegroundColor Green
Write-Host ("Tamanho:  {0:N0} MB comprimido" -f ((Get-Item $zip).Length / 1MB))
Write-Host "SHA-256:  $hash"
Write-Host "SQL:      artifacts/publish/migracoes.sql (opcional; a aplicação aplica as migrações no arranque)"
Write-Host ""
Write-Host "Para actualizar o site (docs/instalacao-winhost.md, secção 3):"
Write-Host "  1. Enviar artifacts/app_offline.htm para a raiz do site (pára a aplicação e liberta os ficheiros)."
Write-Host "  2. Enviar o conteúdo do ZIP por cima (não apagar App_Data nem appsettings.Production.json)."
Write-Host "  3. Apagar app_offline.htm do servidor e abrir /health."
