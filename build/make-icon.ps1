# Generates assets/ImpiousBonum.ico: an orange (#FFAA00) rounded tile with three bars (the same mark as the tray icon),
# drawn separately at each size so small sizes stay crisp. Frames are PNG-compressed, which Windows supports for all sizes.
#   pwsh build/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$output = Join-Path $PSScriptRoot '..\assets\ImpiousBonum.ico'
New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null

function New-Frame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    # Tile: full-bleed rounded square.
    $radius = [Math]::Max(2, $size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $max = $size - 1
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($max - $d, 0, $d, $d, 270, 90)
    $path.AddArc($max - $d, $max - $d, $d, $d, 0, 90)
    $path.AddArc(0, $max - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xFF, 0xAA, 0x00))
    $g.FillPath($orange, $path)

    # Three bars, like a little bar chart; heights echo the tray icon.
    $black = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x10, 0x10, 0x10))
    $barWidth = $size * 0.14
    $gap = $size * 0.085
    $left = ($size - (3 * $barWidth + 2 * $gap)) / 2
    $bottom = $size * 0.78
    $heights = 0.26, 0.52, 0.38
    for ($i = 0; $i -lt 3; $i++) {
        $h = $size * $heights[$i]
        $x = $left + $i * ($barWidth + $gap)
        if ($size -ge 32) {
            $bar = New-Object System.Drawing.Drawing2D.GraphicsPath
            $r = $barWidth * 0.3
            $bar.AddArc($x, $bottom - $h, $r * 2, $r * 2, 180, 90)
            $bar.AddArc($x + $barWidth - $r * 2, $bottom - $h, $r * 2, $r * 2, 270, 90)
            $bar.AddLine($x + $barWidth, $bottom, $x, $bottom)
            $bar.CloseFigure()
            $g.FillPath($black, $bar)
        } else {
            # Snap to whole pixels at tiny sizes so the bars don't blur.
            $g.SmoothingMode = 'None'
            $g.FillRectangle($black, [Math]::Round($x), [Math]::Round($bottom - $h), [Math]::Max(1, [Math]::Round($barWidth)), [Math]::Round($h))
            $g.SmoothingMode = 'AntiAlias'
        }
    }

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

$frames = $sizes | ForEach-Object { , (New-Frame $_) }

# ICO container: ICONDIR, one ICONDIRENTRY per frame, then the PNG data.
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $writer.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $writer.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $output)).Path + '\ImpiousBonum.ico', $file.ToArray())
Write-Host "Wrote $output ($($file.Length) bytes, sizes $($sizes -join ', '))"
