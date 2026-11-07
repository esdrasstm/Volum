# Monta o pacote da Microsoft Store (Volum-<versão>.msix) para enviar no Partner Center.
# A Store assina o pacote na publicação: aqui ele sai sem assinatura.
#   powershell -ExecutionPolicy Bypass -File Store\build-msix.ps1 -Version 0.3.0
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent          # pasta do projeto (LeveMixerWeb)
$out = Join-Path $PSScriptRoot 'out'
$app = Join-Path $out 'app'
$kit = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory | Sort-Object Name -Descending |
  ForEach-Object { Join-Path $_.FullName 'x64' } | Where-Object { Test-Path (Join-Path $_ 'makeappx.exe') } | Select-Object -First 1
if (-not $kit) { throw 'Windows SDK não encontrado (makeappx.exe).' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null

# 1. O app completo (com o .NET junto: funciona sem instalar nada)
dotnet publish (Join-Path $root 'Volum.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -o $app
if ($LASTEXITCODE) { throw 'dotnet publish falhou' }

# 2. Manifesto (com a versão) e ícones
$manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw -Encoding UTF8
$manifest = $manifest.Replace('Version="0.0.0.0"', "Version=""$Version.0""")
[IO.File]::WriteAllText((Join-Path $app 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))
Copy-Item (Join-Path $PSScriptRoot 'Assets') (Join-Path $app 'Assets') -Recurse

# 3. Índice de recursos (para o Windows achar os ícones em cada tamanho)
Push-Location $out
& (Join-Path $kit 'makepri.exe') createconfig /cf priconfig.xml /dq pt-BR_en-US_es-ES /o | Out-Null
& (Join-Path $kit 'makepri.exe') new /pr $app /cf priconfig.xml /mn (Join-Path $app 'AppxManifest.xml') /of (Join-Path $app 'resources.pri') /o
if ($LASTEXITCODE) { Pop-Location; throw 'makepri falhou' }
Pop-Location

# 4. O pacote
$msix = Join-Path $out "Volum-$Version.msix"
& (Join-Path $kit 'makeappx.exe') pack /d $app /p $msix /o
if ($LASTEXITCODE) { throw 'makeappx falhou' }
Write-Host ''
Write-Host "Pacote pronto: $msix"
