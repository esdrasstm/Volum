# Compila o equalizador nativo e roda os testes numéricos.
# Sai com erro se o filtro não bater com a conta — o app C# não segue com um motor furado.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root "build"
New-Item -ItemType Directory -Force -Path $build | Out-Null

$sources = @(
    "eq_engine.cpp", "eq_engine.h", "eq_params.h", "eq_ids.h", "apo.cpp", "preview.cpp", "eq_test.cpp", "VolumEqApo.def", "compile.bat"
) | ForEach-Object { Join-Path $root $_ }
$dll = Join-Path $build "VolumEqApo.dll"
$exe = Join-Path $build "eq_test.exe"
$newest = ($sources | Get-Item | Measure-Object LastWriteTime -Maximum).Maximum
if ((Test-Path $dll) -and (Test-Path $exe) -and (Get-Item $dll).LastWriteTime -ge $newest -and (Get-Item $exe).LastWriteTime -ge $newest) {
    exit 0
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "Não achei o Visual Studio (vswhere)." }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "O Visual Studio está sem as ferramentas de C++." }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

& cmd /c "call `"$root\compile.bat`" `"$vcvars`" `"$root`""
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
