#Requires -Version 5.1
$scriptRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'C:\Tools' }
. (Join-Path $scriptRoot 'lib\SmartPowerPlan.Functions.ps1')
Add-Type -AssemblyName System.Drawing
$iconPath = Get-TrayIconPath -ScriptRoot $scriptRoot
$dir = Split-Path -Parent $iconPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$size = 32
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(0, 120, 215))
$white = [System.Drawing.Brushes]::White
$g.FillEllipse($white, 10, 6, 12, 20)
$g.FillRectangle($white, 14, 4, 4, 8)
$g.Dispose()

$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$stream = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create)
try { $icon.Save($stream) } finally { $stream.Close(); $icon.Dispose(); $bmp.Dispose() }
Write-Host "Tray icon: $iconPath"
