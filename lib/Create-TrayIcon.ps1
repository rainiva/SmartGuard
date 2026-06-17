#Requires -Version 5.1
$scriptRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'D:\Project\SmartGuard' }
. (Join-Path $scriptRoot 'lib\SmartGuard.Functions.ps1')

$iconPath = Get-TrayIconPath -ScriptRoot $scriptRoot
if (-not (Test-Path -LiteralPath $iconPath)) {
    Write-Error "Missing bundled tray icon: $iconPath"
    exit 1
}

Write-Host "Tray icon: $iconPath"
