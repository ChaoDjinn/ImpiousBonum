<#
.SYNOPSIS
  Renders the screenshots in the README and the layout gallery, from made-up readings.

.DESCRIPTION
  Builds the app, then draws each picture with ImpiousBonum.exe --snapshot … --demo, which fills every widget with the
  same made-up readings each time (Core/Demo/DemoReadings.cs): a gaming PC part-way through an evening, at 21:47, with
  music on. Nothing from this PC appears in them.

    docs/images/dashboard.png       the default layout
    docs/images/themes.png          the default layout in eight of the built-in themes
    docs/images/widgets/<name>.png  the widgets close up, each from a small layout in build/screenshots
    docs/images/social-preview.png  the picture shown when the repository's link is shared (build/social-preview.json)
    gallery/images/<layout>.png     each layout in the gallery

  The editor's screenshot (docs/images/editor.png) is taken by hand, since it's a window rather than a layout: run
    ImpiousBonum.exe --data-dir <an empty folder> --demo
  (it runs alongside an installed copy), open the editor, select the GPU graph and capture the window.

.EXAMPLE
  pwsh build/screenshots.ps1
#>
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Add-Type -AssemblyName System.Drawing

dotnet build (Join-Path $root 'src/ImpiousBonum.App') -c $Configuration --nologo -v quiet
$exe = Join-Path $root "src/ImpiousBonum.App/bin/$Configuration/net10.0-windows/ImpiousBonum.exe"

# A data folder of its own with every layout to draw, so nothing touches your settings.
$data = Join-Path ([System.IO.Path]::GetTempPath()) "ImpiousBonum.Screenshots.$([Guid]::NewGuid().ToString('N'))"
$layouts = New-Item -ItemType Directory -Force (Join-Path $data 'layouts')
try {
    $default = Get-Content (Join-Path $root 'src/ImpiousBonum.App/Layout/default-layout.json') -Raw
    Set-Content (Join-Path $layouts 'Default.json') $default -NoNewline
    $gallery = (Get-Content (Join-Path $root 'gallery/index.json') -Raw | ConvertFrom-Json).layouts |
        Where-Object { $_.file -like '*.json' }
    foreach ($entry in $gallery) {
        Copy-Item (Join-Path $root "gallery/$($entry.file)") $layouts
    }
    $widgets = Get-ChildItem (Join-Path $PSScriptRoot 'screenshots') -Filter '*.json'
    foreach ($file in $widgets) {
        Copy-Item $file.FullName (Join-Path $layouts "Widgets $($file.Name)")
    }
    Copy-Item (Join-Path $PSScriptRoot 'social-preview.json') (Join-Path $layouts 'Social preview.json')

    # The default layout with a saved theme instead of its own colours.
    $themes = 'Outrun', 'Nord', 'Dracula', 'Solarized Light', 'Amber', 'Cyberpunk', 'Blueprint', 'Catppuccin'
    $ownTheme = [regex]::new('"theme": \{[^}]*\}', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($theme in $themes) {
        Set-Content (Join-Path $layouts "Theme $theme.json") $ownTheme.Replace($default, "`"theme`": `"$theme`"", 1) -NoNewline
    }

    function Snapshot([string] $layout, [string] $file) {
        New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
        # A Windows app: PowerShell doesn't wait for it unless asked.
        $run = Start-Process $exe -ArgumentList '--data-dir', "`"$data`"", '--snapshot', "`"$file`"", '--layout', "`"$layout`"", '--demo' -Wait -PassThru
        if ($run.ExitCode -ne 0) { throw "Couldn't draw $layout." }
        if ($file.StartsWith($root.Path)) { Write-Host "  $([System.IO.Path]::GetRelativePath($root, $file))" }
    }

    Write-Host 'Drawing:'
    Snapshot 'Default' (Join-Path $root 'docs/images/dashboard.png')
    foreach ($entry in $gallery) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($entry.file)
        Snapshot $name (Join-Path $root "gallery/images/$name.png")
    }
    foreach ($file in $widgets) {
        Snapshot "Widgets $($file.BaseName)" (Join-Path $root "docs/images/widgets/$($file.BaseName).png")
    }
    Snapshot 'Social preview' (Join-Path $root 'docs/images/social-preview.png')

    # The themes, two to a row with their names underneath, on GitHub's dark grey so they read in light and dark mode.
    $tiles = @(foreach ($theme in $themes) {
        $file = Join-Path $data "theme-$theme.png"
        Snapshot "Theme $theme" $file
        [pscustomobject]@{ Name = $theme; File = $file }
    })
    $tileW = 940; $tileH = 235; $gap = 20; $label = 34; $columns = 2
    $rows = [Math]::Ceiling($tiles.Count / $columns)
    $width = $columns * $tileW + ($columns + 1) * $gap
    $height = $rows * ($tileH + $label + $gap) + $gap / 2
    $canvas = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::FromArgb(0x16, 0x1B, 0x22))
    $font = New-Object System.Drawing.Font 'Segoe UI', 15, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xC9, 0xD1, 0xD9))
    for ($i = 0; $i -lt $tiles.Count; $i++) {
        $x = $gap + ($i % $columns) * ($tileW + $gap)
        $y = $gap + [Math]::Floor($i / $columns) * ($tileH + $label + $gap)
        $image = [System.Drawing.Image]::FromFile($tiles[$i].File)
        $corner = New-Object System.Drawing.Drawing2D.GraphicsPath
        $r = 14
        $corner.AddArc($x, $y, $r, $r, 180, 90); $corner.AddArc($x + $tileW - $r, $y, $r, $r, 270, 90)
        $corner.AddArc($x + $tileW - $r, $y + $tileH - $r, $r, $r, 0, 90); $corner.AddArc($x, $y + $tileH - $r, $r, $r, 90, 90)
        $corner.CloseFigure()
        $g.SetClip($corner)
        $g.DrawImage($image, $x, $y, $tileW, $tileH)
        $g.ResetClip()
        $g.DrawString($tiles[$i].Name, $font, $ink, $x + 2, $y + $tileH + 8)
        $image.Dispose()
    }
    $g.Dispose()
    $themesFile = Join-Path $root 'docs/images/themes.png'
    $canvas.Save($themesFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    Write-Host "  $([System.IO.Path]::GetRelativePath($root, $themesFile))"
}
finally {
    Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
}
