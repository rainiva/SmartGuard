# Restart-Tray.ps1 - stop old tray and start SmartGuard.Tray.exe
#Requires -Version 5.1
$root = if ($PSScriptRoot) { $PSScriptRoot } else { 'D:\Project\SmartGuard' }
$trayExe = Join-Path $root 'bin\SmartGuard.Tray.exe'

Get-Process -Name 'SmartGuard.Tray' -ErrorAction SilentlyContinue |
    ForEach-Object {
        Write-Host "Stopping tray PID=$($_.Id)"
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }

Start-Sleep -Milliseconds 600

if (-not (Test-Path -LiteralPath $trayExe)) {
    Write-Error "SmartGuard.Tray.exe not found at $trayExe"
    exit 1
}

Start-Process -FilePath $trayExe -ArgumentList "--root `"$root`"" -WorkingDirectory $root
Write-Host 'Tray restarted (SmartGuard.Tray.exe). Check the notification area.'
