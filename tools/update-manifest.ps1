<#
.SYNOPSIS
  Régénère manifest/components.json depuis les sources officielles (Windows PowerShell 5.1).
.DESCRIPTION
  Hashes lus dans les fichiers publiés par les éditeurs ; seul MySQL (MD5 uniquement publié)
  est téléchargé puis haché avec Get-FileHash -Algorithm SHA256. Toute version contenant
  RC, alpha, beta ou dev est ignorée. Aucun hash n'est jamais deviné.
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\manifest\components.json'),
    [string]$MySqlSeries = '8.4'
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'
$Unstable = 'RC|alpha|beta|dev'
$releases = New-Object System.Collections.ArrayList

function Get-Text([string]$Url) { (Invoke-WebRequest -Uri $Url -UseBasicParsing).Content }
function Get-Size([string]$Url) {
    $r = Invoke-WebRequest -Uri $Url -Method Head -UseBasicParsing
    [long]($r.Headers['Content-Length'] | Select-Object -First 1)
}
function Add-Release($Component, $Version, $Url, $Sha, $Size, $Package, $Requires, $Root) {
    if ($Version -match $Unstable) { Write-Warning "Ignoré (non stable) : $Component $Version"; return }
    if ($Sha -notmatch '^[0-9a-fA-F]{64}$') { throw "SHA-256 invalide pour $Component $Version" }
    [void]$releases.Add([ordered]@{
        component = $Component; version = $Version; channel = 'stable'; url = $Url
        sha256 = $Sha.ToLowerInvariant(); sizeBytes = $Size; package = $Package
        requires = @($Requires); archiveRoot = $Root
    })
}

# --- PHP (releases.json, Thread Safe x64) ---
$php = (Get-Text 'https://windows.php.net/downloads/releases/releases.json') | ConvertFrom-Json
foreach ($branch in '8.4', '8.3') {
    $b = $php.$branch
    $build = $b.PSObject.Properties | Where-Object { $_.Name -match '^ts-v[cs]\d+-x64$' } | Select-Object -First 1
    $zip = $build.Value.zip
    $url = "https://windows.php.net/downloads/releases/$($zip.path)"
    Add-Release 'Php' $b.version $url $zip.sha256 (Get-Size $url) 'Zip' @('vcredist-x64') $null
}

# --- Apache Lounge (page de téléchargement + fichier .zip.txt de checksums) ---
$page = Get-Text 'https://www.apachelounge.com/download/'
if ($page -match 'href="(/download/VS\d+/binaries/(httpd-(2\.4\.\d+)-\d+-Win64-VS\d+\.zip))"') {
    $url = "https://www.apachelounge.com$($Matches[1])"; $ver = $Matches[3]
    $sums = Get-Text "$url.txt"
    if ($sums -notmatch '(?s)SHA256-Checksum for:[^\r\n]*\r?\n([0-9A-Fa-f]{64})') { throw 'Checksum Apache introuvable' }
    Add-Release 'Apache' $ver $url $Matches[1] (Get-Size $url) 'Zip' @('vcredist-x64') 'Apache24'
} else { throw 'Build Apache Win64 introuvable sur Apache Lounge' }

# --- Node.js 22 et 24 LTS (SHASUMS256.txt) ---
$index = (Get-Text 'https://nodejs.org/dist/index.json') | ConvertFrom-Json
foreach ($major in 22, 24) {
    $n = $index | Where-Object { $_.version -like "v$major.*" -and $_.lts } | Select-Object -First 1
    $v = $n.version; $file = "node-$v-win-x64.zip"
    $line = (Get-Text "https://nodejs.org/dist/$v/SHASUMS256.txt") -split "`n" | Where-Object { $_ -match [regex]::Escape($file) } | Select-Object -First 1
    $url = "https://nodejs.org/dist/$v/$file"
    Add-Release 'Node' $v.TrimStart('v') $url ($line -split '\s+')[0] (Get-Size $url) 'Zip' @() "node-$v-win-x64"
}

# --- Mailpit (digest SHA-256 publié par GitHub pour l'asset de la release) ---
$gh = (Get-Text 'https://api.github.com/repos/axllent/mailpit/releases/latest') | ConvertFrom-Json
$asset = $gh.assets | Where-Object { $_.name -eq 'mailpit-windows-amd64.zip' }
if (-not $asset.digest -or $asset.digest -notmatch '^sha256:') { throw 'Digest GitHub absent pour Mailpit' }
Add-Release 'Mailpit' $gh.tag_name.TrimStart('v') $asset.browser_download_url ($asset.digest -replace '^sha256:', '') $asset.size 'Zip' @() $null

# --- Composer 2.x (.sha256sum officiel) ---
$cv = ((Get-Text 'https://getcomposer.org/versions') | ConvertFrom-Json).stable | Select-Object -First 1
$ver = $cv.version
$url = "https://getcomposer.org/download/$ver/composer.phar"
$sha = ((Get-Text "$url.sha256sum") -split '\s+')[0]
Add-Release 'Composer' $ver $url $sha (Get-Size $url) 'File' @() $null

# --- MySQL 8.4 LTS : seul le MD5 est publié, SHA-256 calculé sur le zip officiel ---
$my = $null
for ($i = 40; $i -ge 0 -and -not $my; $i--) {
    $u = "https://cdn.mysql.com/archives/mysql-$MySqlSeries/mysql-$MySqlSeries.$i-winx64.zip"
    try { $null = Invoke-WebRequest -Uri $u -Method Head -UseBasicParsing; $my = @{ Url = $u; Ver = "$MySqlSeries.$i" } } catch { }
}
if (-not $my) { throw 'Zip MySQL introuvable sur cdn.mysql.com' }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $f = Join-Path $tmp 'mysql.zip'
    Invoke-WebRequest -Uri $my.Url -OutFile $f -UseBasicParsing
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash
    Add-Release 'MySql' $my.Ver $my.Url $hash (Get-Item $f).Length 'Zip' @() "mysql-$($my.Ver)-winx64"
} finally { Remove-Item -Recurse -Force $tmp }

$order = 'Apache', 'Php', 'MySql', 'Mailpit', 'Node', 'Composer'
$sorted = $releases | Sort-Object { $order.IndexOf($_.component) }, @{ Expression = { [version]$_.version }; Descending = $true }
$json = [ordered]@{ releases = @($sorted) } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($Output, $json, (New-Object Text.UTF8Encoding($false)))
Write-Host "Manifeste écrit : $Output ($($releases.Count) versions)"
