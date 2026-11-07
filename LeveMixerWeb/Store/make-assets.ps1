# Gera o ícone do Volum em todos os lugares: Volum.ico (.exe e instalador) e os ícones do pacote da
# Microsoft Store (Store\Assets). Desenho: círculo preto com borda clarinha e o logo branco das três
# barrinhas (o mesmo do ícone da bandeja, IconFactory em Helpers.cs, e do site).
# Rodar de novo só se o ícone mudar:  powershell -ExecutionPolicy Bypass -File Store\make-assets.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force $out | Out-Null

# Desenha o logo num quadrado de "size" px (o desenho original é de 32 × 32)
function New-Logo([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  $k = $size / 32.0
  $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 9, 9, 9))
  $g.FillEllipse($bg, 0, 0, $size - 1, $size - 1)
  $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([Math]::Max(1, $k))
  $g.DrawEllipse($ring, 0.5 * $k, 0.5 * $k, $size - 1 - $k, $size - 1 - $k)
  $line = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (2.5 * $k)
  $line.StartCap = 'Round'; $line.EndCap = 'Round'
  $knob = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
  $xs = 9, 16, 23; $ys = 20, 11, 17
  for ($i = 0; $i -lt 3; $i++) {
    $g.DrawLine($line, $xs[$i] * $k, 7 * $k, $xs[$i] * $k, 25 * $k)
    $g.FillEllipse($knob, ($xs[$i] - 3.5) * $k, ($ys[$i] - 3.5) * $k, 7 * $k, 7 * $k)
  }
  $g.Dispose(); $bg.Dispose(); $ring.Dispose(); $line.Dispose(); $knob.Dispose()
  return $bmp
}

# Põe o logo de "logo" px no meio de uma imagem w × h transparente
function Save-Png([string]$name, [int]$w, [int]$h, [int]$logo) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  $l = New-Logo $logo
  $g.DrawImage($l, [int](($w - $logo) / 2), [int](($h - $logo) / 2), $logo, $logo)
  $g.Dispose(); $l.Dispose()
  $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}

# Ícones da Store
Get-ChildItem $out -Filter *.png | Remove-Item
Save-Png 'StoreLogo.png' 50 50 50
Save-Png 'Square44x44Logo.png' 44 44 44
Save-Png 'Square150x150Logo.png' 150 150 112
Save-Png 'Wide310x150Logo.png' 310 150 112
foreach ($s in 16, 24, 32, 48, 256) {
  Save-Png "Square44x44Logo.targetsize-${s}_altform-unplated.png" $s $s $s
  Save-Png "Square44x44Logo.targetsize-${s}.png" $s $s $s
}

# Volum.ico com vários tamanhos (PNG dentro do .ico, aceito do Windows Vista em diante)
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
  $l = New-Logo $s; $ms = New-Object IO.MemoryStream
  $l.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $l.Dispose()
  , $ms.ToArray()
}
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $len = $pngs[$i].Length
  $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
  $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $root 'Volum.ico'), $ico.ToArray())
Write-Host "Ícones gerados: $out e Volum.ico"
