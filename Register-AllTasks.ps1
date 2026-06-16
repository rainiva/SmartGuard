# Register Core + Tray scheduled tasks
Write-Host 'Registering Core (admin required)...'
& (Join-Path $PSScriptRoot 'Register-SmartGuardTask.ps1')
Write-Host 'Registering Tray...'
& (Join-Path $PSScriptRoot 'Register-TrayTask.ps1')
Write-Host 'Done. Log off and log on, or run Tray manually:'
Write-Host '  powershell -Sta -File D:\Project\SmartGuard\lib\SmartGuard.Tray.ps1'