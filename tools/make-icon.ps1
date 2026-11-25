# Génère src/StackR.App/Assets/stackr.ico : losange arrondi « R », dégradé violet -> cyan.
# Usage : powershell -File tools/make-icon.ps1   (script ponctuel, aucune dépendance ajoutée à l'application)
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\StackR.App\Assets\stackr.ico'
$sizes = 16, 24, 32, 48, 64, 256

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Losange = carré arrondi tourné de 45 degrés, centré.
    $half = $s / 2.0
    $side = $s * 0.66
    $r = $side * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $x = -$side / 2; $y = -$side / 2; $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $side - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $side - $d, $y + $side - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $side - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Rotate(45)
    $m.Translate(0, 0, 'Append')
    $path.Transform($m)
    $m2 = New-Object System.Drawing.Drawing2D.Matrix
    $m2.Translate($half, $half)
    $path.Transform($m2)

    $b = $path.GetBounds()
    $p1 = New-Object System.Drawing.PointF $b.Left, $b.Top
    $p2 = New-Object System.Drawing.PointF $b.Right, $b.Bottom
    $c1 = [System.Drawing.ColorTranslator]::FromHtml('#7C3AED')
    $c2 = [System.Drawing.ColorTranslator]::FromHtml('#06B6D4')
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p1, $p2, $c1, $c2
    $g.FillPath($brush, $path)

    # « R » blanc centré.
    $fontSize = [single]($s * 0.36)
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $rect = New-Object System.Drawing.RectangleF 0, ($s * 0.01), $s, $s
    $g.DrawString('R', $font, [System.Drawing.Brushes]::White, $rect, $sf)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$frames = foreach ($s in $sizes) { , (New-Frame $s) }

$fs = [System.IO.File]::Create((Resolve-Path (Split-Path $out)).Path + '\stackr.ico')
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Close()
Write-Host "Icône écrite : $out"
