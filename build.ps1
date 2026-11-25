#requires -Version 5.1
<#
.SYNOPSIS
  Teste puis publie StackR (App + CLI) dans publish\StackR\, et compile l'installeur si Inno Setup est present.
#>
[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Invoke-Step([string]$Label, [scriptblock]$Action) {
    Write-Host "==> $Label" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Echec : $Label (code $LASTEXITCODE)" }
}

$out = Join-Path $root 'publish\StackR'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

if (-not $SkipTests) {
    Invoke-Step 'Tests' { dotnet test (Join-Path $root 'StackR.sln') -c Release }
}

# StackR.App.exe et stackr.exe doivent rester cote a cote : l'application relance
# stackr.exe en eleve (UAC) pour synchroniser le fichier hosts.
Invoke-Step 'Publication de StackR (application)' {
    dotnet publish (Join-Path $root 'src\StackR.App\StackR.App.csproj') -c Release -p:PublishProfile=win-x64 -o $out
}
Invoke-Step 'Publication de stackr (CLI)' {
    dotnet publish (Join-Path $root 'src\StackR.Cli\StackR.Cli.csproj') -c Release -p:PublishProfile=win-x64 -o $out
}

# NB : StackR.exe et stackr.exe ne pourraient pas coexister (Windows est insensible a la casse).
foreach ($exe in 'StackR.App.exe', 'stackr.exe') {
    if (-not (Test-Path (Join-Path $out $exe))) { throw "Fichier attendu absent : $exe" }
}
Write-Host "Publie dans $out" -ForegroundColor Green

# Installeur (optionnel)
$iscc = $null
$cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($cmd) { $iscc = $cmd.Source }
if (-not $iscc) {
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, "$env:LOCALAPPDATA\Programs")) {
        if (-not $base) { continue }
        $candidate = Join-Path $base 'Inno Setup 6\ISCC.exe'
        if (Test-Path $candidate) { $iscc = $candidate; break }
    }
}
if ($iscc) {
    Invoke-Step "Compilation de l'installeur" { & $iscc (Join-Path $root 'installer\StackR.iss') }
    Write-Host "Installeur : $(Join-Path $root 'publish\StackR-Setup.exe')" -ForegroundColor Green
} else {
    Write-Host 'Inno Setup 6 introuvable : installeur ignore (https://jrsoftware.org/isinfo.php).' -ForegroundColor Yellow
}
