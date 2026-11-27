<#
.SYNOPSIS
  Telecharge dans packages\ les composants par defaut de manifest\components.json (version recommandee de chacun),
  en verifiant le SHA-256 du manifeste. Echoue au moindre ecart : aucun fichier non verifie n'est conserve.
.DESCRIPTION
  Utilise par build.ps1 (-WithPackages) et par le workflow de release. StackR.App installe depuis ces fichiers
  (dossier packages\ a cote de l'executable) sans reseau ; le nom de chaque fichier est celui de l'URL de la release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Manifest
)
$ErrorActionPreference = 'Stop'
if (-not $Manifest) { $Manifest = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\manifest\components.json' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

# Doit rester identique a StackREngine.DefaultComponents.
$defaults = 'Apache', 'Php', 'MySql', 'Mailpit', 'Composer', 'Adminer', 'AdminerCss', 'AdminerDarkCss', 'HeidiSql'

$releases = (Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json).releases
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

foreach ($kind in $defaults) {
    $r = $releases | Where-Object { $_.component -eq $kind } |
        Sort-Object { [version]$_.version } -Descending | Select-Object -First 1
    if (-not $r) { throw "Composant absent du manifeste : $kind" }
    if ($r.url -notmatch '^https://') { throw "URL non HTTPS pour $kind" }
    $name = [IO.Path]::GetFileName(([Uri]$r.url).AbsolutePath)
    $target = Join-Path $OutDir $name
    $expected = $r.sha256.ToUpperInvariant()

    if ((Test-Path -LiteralPath $target) -and ((Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash -eq $expected)) {
        Write-Host "OK (deja present) : $name"
        continue
    }

    $tmp = "$target.download"
    Write-Host "Telechargement : $($r.url)"
    try {
        # curl.exe (Windows 10+, runners GitHub) : Invoke-WebRequest de Windows PowerShell 5.1 ne suit pas les redirections 308.
        & curl.exe --fail --silent --show-error --location --proto '=https' --proto-redir '=https' --user-agent 'StackR-build' --output $tmp $r.url
        if ($LASTEXITCODE -ne 0) { throw "Telechargement echoue (curl $LASTEXITCODE) : $($r.url)" }
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $tmp).Hash
        if ($actual -ne $expected) {
            throw "SHA-256 different pour $kind $($r.version) ($name) : attendu $expected, calcule $actual"
        }
        Move-Item -LiteralPath $tmp -Destination $target -Force
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    }
    Write-Host "OK : $name"
}
Write-Host "Paquets verifies dans $OutDir" -ForegroundColor Green
