$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'src/StartFlow.App/Assets'
New-Item -ItemType Directory -Force $folder | Out-Null
$bitmap = [System.Drawing.Bitmap]::new(64,64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(24,91,195))
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White,5)
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
$graphics.DrawLine($pen,16,19,35,19)
$graphics.DrawLine($pen,16,32,28,32)
$graphics.DrawLine($pen,16,45,39,45)
$graphics.DrawLine($pen,39,45,49,32)
$graphics.DrawLine($pen,40,32,49,32)
$graphics.DrawLine($pen,49,32,49,41)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [IO.File]::Create((Join-Path $folder 'StartFlow.ico'))
$icon.Save($stream); $stream.Dispose(); $icon.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
