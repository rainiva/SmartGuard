#Requires -Version 5.1
$scriptRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'D:\Project\SmartGuard' }
$iconPath = Join-Path $scriptRoot 'lib\SmartGuard.ico'

if (-not (Test-Path -LiteralPath $iconPath)) {
    Write-Error "Missing bundled tray icon: $iconPath"
    exit 1
}

Write-Host "Tray icon: $iconPath"
